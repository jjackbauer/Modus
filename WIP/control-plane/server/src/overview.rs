use std::collections::{BTreeMap, BTreeSet};

use control_plane_domain::{
    AgentSummary, Attention, EventStreamEnvelope, Overview, TokenSummary, WorkItemSummary,
};
use control_plane_factory_client::{ClientError, FactoryClient};

/// Aggregates the adapter's read models into the Overview projection. This is a read-only
/// composition, not a second work-state authority: every field points back to an adapter object.
pub async fn build(client: &FactoryClient) -> Overview {
    let meta = client.meta().await;
    let health = client.health().await;
    let factory = client.factory().await;
    let dispatch = client.dispatch_status().await;
    let approvals = client.approvals().await;
    let releases = client.releases().await;
    let agents = client.agents().await;
    let work = client
        .work_items(None, None, None, Some("updated-desc"), 500, 0)
        .await;

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

    let mut attention = Vec::new();
    if let Ok(inbox) = &approvals {
        for item in &inbox.items {
            attention.push(Attention {
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
            });
        }
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

    let mut details = Vec::new();
    for item in flagged {
        match client.work_item(&item.work_item_id).await {
            Ok(detail) => details.push(detail),
            Err(error) => errors.push(format!("detail {}: {error}", item.work_item_id)),
        }
    }

    for detail in &details {
        for item in &detail.attention {
            if !attention.iter().any(|existing| existing.id == item.id) {
                attention.push(item.clone());
            }
        }
    }

    let severity_rank = |severity: &str| match severity {
        "critical" => 0,
        "warning" => 1,
        "info" => 2,
        _ => 3,
    };
    attention.sort_by_key(|item| severity_rank(&item.severity));
    let seen: BTreeSet<String> = attention.iter().map(|item| item.id.clone()).collect();
    attention.retain(|item| seen.contains(&item.id));

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
