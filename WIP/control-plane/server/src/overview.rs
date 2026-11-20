use std::collections::BTreeMap;

use control_plane_domain::{
    AgentSummary, ApprovalItem, Attention, EventStreamEnvelope, Overview, TokenSummary,
    WorkItemDetail, WorkItemSummary,
};
use control_plane_factory_client::{ClientError, FactoryClient};

/// Aggregates the adapter's read models into the Overview projection. This is a read-only
/// composition, not a second work-state authority: every field points back to an adapter object.
pub async fn build(client: &FactoryClient) -> Overview {
    // These reads are independent. Sequential awaits made the landing page latency the sum of
    // every adapter round-trip; on a large fixture that exceeded the request timeout.
    let (meta, health, factory, dispatch, approvals, releases, agents, work) = tokio::join!(
        client.meta(),
        client.health(),
        client.factory(),
        client.dispatch_status(),
        async {
            tokio::time::timeout(
                std::time::Duration::from_secs(8),
                client.approvals(),
            )
            .await
            .unwrap_or_else(|_| {
                Err(ClientError::Transport(
                    "overview approvals timed out after 8s".to_string(),
                ))
            })
        },
        client.releases(),
        client.agents(),
        client.work_items(None, None, None, Some("updated-desc"), 500, 0),
    );

    let mut errors = Vec::new();
    for (label, error) in [
        ("meta", meta.as_ref().err()),
        ("health", health.as_ref().err()),
        ("factory", factory.as_ref().err()),
        ("dispatch", dispatch.as_ref().err()),
        ("approvals", approvals.as_ref().err()),
        ("releases", releases.as_ref().err()),
        ("agents", agents.as_ref().err()),
        ("work-items", work.as_ref().err()),
    ] {
        if let Some(error) = error {
            errors.push(format!("{label}: {error}"));
        }
    }

    let work_items = work
        .as_ref()
        .map(|page| page.items.clone())
        .unwrap_or_default();
    let mut work_counts = BTreeMap::new();
    for state in [
        "Proposed",
        "RequirementsAligned",
        "ArchitectureRefined",
        "ReadyForImplementation",
        "InImplementation",
        "InReview",
        "ReadyForDelivery",
        "DeliveryValidation",
        "ReleaseCandidate",
        "Released",
        "OperationallyValidated",
        "Blocked",
        "NeedsRework",
        "Failed",
        "Cancelled",
        "Closed",
    ] {
        work_counts.insert(state.to_string(), 0);
    }
    for item in &work_items {
        *work_counts.entry(item.state.clone()).or_insert(0) += 1;
    }

    // Only fetch details for items that can carry operator attention. This bounds the work of the
    // landing page while still explaining why each item is stuck.
    let flagged: Vec<&WorkItemSummary> = work_items
        .iter()
        .filter(|item| {
            item.awaiting_contract_approval
                || item.awaiting_merge_approval
                || matches!(item.state.as_str(), "Failed" | "Blocked" | "NeedsRework")
                || item.last_failure.is_some()
        })
        .take(12)
        .collect();

    let failed_work_items = work_items
        .iter()
        .filter(|item| {
            item.last_failure.is_some()
                || matches!(item.state.as_str(), "Failed" | "Blocked" | "NeedsRework")
        })
        .count() as i32;

    let detail_results = futures_util::future::join_all(flagged.iter().map(|item| {
        let work_item_id = item.work_item_id.clone();
        async move {
            let result = client.work_item(&work_item_id).await;
            (work_item_id, result)
        }
    }))
    .await;
    let mut details = Vec::new();
    for (work_item_id, result) in detail_results {
        match result {
            Ok(detail) => details.push(detail),
            Err(error) => errors.push(format!("detail {work_item_id}: {error}")),
        }
    }

    let attention = canonical_attention(
        approvals
            .as_ref()
            .map(|inbox| inbox.items.as_slice())
            .unwrap_or(&[]),
        &details,
    );

    let token_measured = agents
        .as_ref()
        .map(|agents| agents.iter().any(|agent| agent.tokens_measured))
        .unwrap_or(false);
    let tokens = TokenSummary {
        measured: token_measured,
        input_tokens: agents
            .as_ref()
            .map(|agents| {
                agents
                    .iter()
                    .map(|agent: &AgentSummary| i64::from(agent.input_tokens))
                    .sum()
            })
            .unwrap_or(0),
        output_tokens: agents
            .as_ref()
            .map(|agents| {
                agents
                    .iter()
                    .map(|agent| i64::from(agent.output_tokens))
                    .sum()
            })
            .unwrap_or(0),
        models: Vec::new(),
        basis: if token_measured {
            "Sum of provider-reported usage in durable session traces. Model-level attribution is unavailable in this projection."
                .to_string()
        } else {
            "Not measured: no durable provider usage exists for these sessions. This is not zero consumption."
                .to_string()
        },
    };

    let latest = health
        .as_ref()
        .map(|health| health.latest_event_sequence)
        .unwrap_or(-1);
    let after = (latest - 50).max(0);
    let recent = client.events(after, 50).await;
    let recent_events: Vec<EventStreamEnvelope> = match &recent {
        Ok(page) => page.items.clone(),
        Err(error) => {
            errors.push(format!("events: {error}"));
            Vec::new()
        }
    };

    let stale = errors.len() > 0
        || health
            .as_ref()
            .map(|health| !health.healthy)
            .unwrap_or(true)
        || meta.is_err();

    Overview {
        meta: meta.ok(),
        health: health.ok(),
        factory: factory.ok(),
        dispatch: dispatch.ok(),
        approvals: approvals.ok(),
        releases: releases.ok(),
        agents: agents.unwrap_or_default(),
        work_counts,
        total_work_items: work_items.len() as i32,
        failed_work_items,
        recent_events,
        attention,
        tokens,
        errors,
        stale,
        generated_at_utc: chrono::Utc::now().to_rfc3339(),
    }
}

/// Stable identity of one operator action. Two projections that describe the same pending
/// authorization must collapse into one inbox item, so the key is the governed
/// (work item, action) pair rather than a projection-specific id.
fn attention_key(work_item_id: Option<&str>, action: Option<&str>, id: &str) -> String {
    match (work_item_id, action) {
        (Some(work_item_id), Some(action)) if !work_item_id.is_empty() && !action.is_empty() => {
            format!("{work_item_id}|{action}")
        }
        _ => format!("id|{id}"),
    }
}

fn severity_rank(severity: &str) -> u8 {
    match severity {
        "critical" => 0,
        "warning" => 1,
        "info" => 2,
        _ => 3,
    }
}

fn merge_attention_items(items: impl Iterator<Item = Attention>) -> Vec<Attention> {
    let mut merged: BTreeMap<String, Attention> = BTreeMap::new();
    for item in items {
        let key = attention_key(item.work_item_id.as_deref(), item.action.as_deref(), &item.id);
        match merged.get_mut(&key) {
            None => {
                merged.insert(key, item);
            }
            Some(existing) => {
                // Keep the fuller explanation from the work-detail projection and the governed
                // endpoint from the approvals inbox: one row with both facts.
                let id = existing.id.clone();
                let endpoint = existing.endpoint.clone().or_else(|| item.endpoint.clone());
                let action = existing.action.clone().or_else(|| item.action.clone());
                let work_item_id = existing
                    .work_item_id
                    .clone()
                    .or_else(|| item.work_item_id.clone());
                let severity = if severity_rank(&item.severity) < severity_rank(&existing.severity) {
                    item.severity.clone()
                } else {
                    existing.severity.clone()
                };
                *existing = Attention {
                    id,
                    severity,
                    title: item.title,
                    what_happened: item.what_happened,
                    why_it_matters: item.why_it_matters,
                    safe_action: item.safe_action,
                    consequence: item.consequence,
                    work_item_id,
                    action,
                    endpoint,
                };
            }
        }
    }

    let mut attention: Vec<Attention> = merged.into_values().collect();
    attention.sort_by(|left, right| {
        severity_rank(&left.severity)
            .cmp(&severity_rank(&right.severity))
            .then_with(|| left.work_item_id.cmp(&right.work_item_id))
            .then_with(|| left.title.cmp(&right.title))
    });
    attention
}

fn approval_attention(item: &ApprovalItem) -> Attention {
    Attention {
        id: item.id.clone(),
        severity: "warning".to_string(),
        title: format!("Approval required: {}", item.title),
        what_happened: item.summary.clone(),
        why_it_matters: "The factory will not advance this work item until the governed authorization is recorded."
            .to_string(),
        safe_action: "Inspect the exact digest and binding conditions, then approve or reject through the approvals inbox."
            .to_string(),
        consequence: item.effect_of_approval.join("; "),
        work_item_id: (!item.work_item_id.is_empty()).then(|| item.work_item_id.clone()),
        action: Some(item.approve_action.clone()),
        endpoint: Some(item.endpoint.clone()),
    }
}

/// One actionable row per pending operator decision, across both adapter projections.
pub fn canonical_attention(approvals: &[ApprovalItem], details: &[WorkItemDetail]) -> Vec<Attention> {
    merge_attention_items(
        approvals
            .iter()
            .map(approval_attention)
            .chain(details.iter().flat_map(|detail| detail.attention.iter().cloned())),
    )
}

/// Maps a client failure to a status an operator can act on without revealing the adapter token.
pub fn client_error_status(error: &ClientError) -> (u16, String, String) {
    match error {
        ClientError::Status {
            status,
            code,
            message,
            ..
        } => {
            let status = match *status {
                400 | 401 | 403 | 404 | 409 | 413 | 415 | 422 | 429 => *status,
                _ => 502,
            };
            (status, code.clone(), message.clone())
        }
        ClientError::Transport(message) => (
            503,
            "factory-unavailable".to_string(),
            format!("The factory adapter is unreachable: {message}"),
        ),
        ClientError::Decode(message) => (
            502,
            "factory-contract-mismatch".to_string(),
            format!("The factory adapter response did not match the versioned contract: {message}"),
        ),
        ClientError::Configuration(message) => (
            503,
            "factory-not-configured".to_string(),
            format!("The factory adapter is not configured correctly: {message}"),
        ),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn attention(
        id: &str,
        work_item_id: &str,
        action: &str,
        severity: &str,
        title: &str,
    ) -> Attention {
        Attention {
            id: id.to_string(),
            severity: severity.to_string(),
            title: title.to_string(),
            what_happened: String::new(),
            why_it_matters: String::new(),
            safe_action: String::new(),
            consequence: String::new(),
            work_item_id: (!work_item_id.is_empty()).then(|| work_item_id.to_string()),
            action: (!action.is_empty()).then(|| action.to_string()),
            endpoint: None,
        }
    }

    #[test]
    fn one_pending_merge_produces_one_attention_item() {
        let inbox = attention(
            "merge-WORK-1-abc",
            "WORK-1",
            "approve-merge",
            "warning",
            "Approval required: Merge",
        );
        let detail = attention(
            "attention-WORK-1-merge",
            "WORK-1",
            "approve-merge",
            "warning",
            "Merge authorization required",
        );

        let merged = merge_attention_items([inbox, detail].into_iter());

        assert_eq!(merged.len(), 1);
        assert_eq!(merged[0].title, "Merge authorization required");
        assert_eq!(merged[0].action.as_deref(), Some("approve-merge"));
    }

    #[test]
    fn distinct_actions_and_unkeyed_items_are_not_collapsed() {
        let contract = attention(
            "attention-WORK-1-contract",
            "WORK-1",
            "approve-contract",
            "warning",
            "Contract approval required",
        );
        let merge = attention(
            "attention-WORK-1-merge",
            "WORK-1",
            "approve-merge",
            "warning",
            "Merge authorization required",
        );
        let template = attention(
            "template-tpl@1",
            "",
            "approve-template",
            "info",
            "Template policy approval",
        );

        let merged = merge_attention_items([contract, merge, template].into_iter());

        assert_eq!(merged.len(), 3);
    }

    #[test]
    fn the_governed_endpoint_survives_the_collapse() {
        let mut inbox = attention(
            "merge-WORK-1-abc",
            "WORK-1",
            "approve-merge",
            "warning",
            "Approval required: Merge",
        );
        inbox.endpoint =
            Some("/api/instances/{instance}/work-items/WORK-1/merge-approvals".to_string());
        let detail = attention(
            "attention-WORK-1-merge",
            "WORK-1",
            "approve-merge",
            "critical",
            "Merge authorization required",
        );

        let merged = merge_attention_items([inbox, detail].into_iter());

        assert_eq!(merged.len(), 1);
        assert!(merged[0].endpoint.is_some());
        assert_eq!(merged[0].severity, "critical");
    }

    #[test]
    fn duplicate_ids_from_one_projection_are_collapsed_too() {
        let first = attention(
            "attention-WORK-2-failed",
            "WORK-2",
            "inspect-events",
            "critical",
            "Terminal failure",
        );
        let second = first.clone();

        let merged = merge_attention_items([first, second].into_iter());

        assert_eq!(merged.len(), 1);
    }
}