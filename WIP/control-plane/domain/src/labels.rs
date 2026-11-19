//! Presentation-neutral labels and tones. Pages use these so the same state never has two
//! different names in two screens.

use chrono::{DateTime, Utc};

use crate::api::Serving;

pub fn state_label(state: &str) -> &str {
    match state {
        "Proposed" => "Proposed",
        "RequirementsAligned" => "Requirements aligned",
        "ArchitectureRefined" => "Architecture refined",
        "ReadyForImplementation" => "Ready for implementation",
        "InImplementation" => "In implementation",
        "InReview" => "In review",
        "ReadyForDelivery" => "Ready for delivery",
        "DeliveryValidation" => "Delivery validation",
        "ReleaseCandidate" => "Release candidate",
        "Released" => "Released",
        "OperationallyValidated" => "Operationally validated",
        "Blocked" => "Blocked",
        "NeedsRework" => "Needs rework",
        "Failed" => "Failed",
        "Cancelled" => "Cancelled",
        "Closed" => "Closed",
        other => other,
    }
}

pub fn state_tone(state: &str) -> &'static str {
    match state {
        "Released" | "OperationallyValidated" => "success",
        "Failed" => "danger",
        "Blocked" | "NeedsRework" | "Cancelled" => "warn",
        "InImplementation" | "InReview" | "DeliveryValidation" | "ReleaseCandidate" => "active",
        "ReadyForDelivery" => "waiting",
        "Proposed" | "RequirementsAligned" | "ArchitectureRefined" | "ReadyForImplementation" => {
            "neutral"
        }
        _ => "neutral",
    }
}

pub fn indicator_label(indicator: Option<&str>) -> &str {
    match indicator.unwrap_or_default() {
        "waiting-contract-approval" => "Waiting for contract approval",
        "waiting-merge-approval" => "Waiting for merge approval",
        "active-implementation" => "Active implementation",
        "running-verification" => "Running verification",
        "verification-failed" => "Verification failed",
        "implementation-failed" => "Implementation failed",
        "needs-rework" => "Needs rework",
        "blocked" => "Blocked",
        "terminal-failure" => "Terminal failure",
        "released" => "Released",
        "operationally-validated" => "Operationally validated",
        "delivery-in-progress" => "Delivery in progress",
        "release-in-progress" => "Release in progress",
        "awaiting-intake-dispatch" => "Awaiting dispatch",
        _ => indicator.unwrap_or("Unknown"),
    }
}

pub fn indicator_tone(indicator: Option<&str>) -> &'static str {
    match indicator.unwrap_or_default() {
        "waiting-contract-approval" | "waiting-merge-approval" => "waiting",
        "terminal-failure" | "verification-failed" | "implementation-failed" => "danger",
        "needs-rework" | "blocked" => "warn",
        "released" | "operationally-validated" => "success",
        "active-implementation"
        | "running-verification"
        | "delivery-in-progress"
        | "release-in-progress" => "active",
        _ => "neutral",
    }
}

pub fn severity_tone(severity: &str) -> &'static str {
    match severity {
        "critical" => "danger",
        "warning" => "warn",
        "info" => "info",
        _ => "neutral",
    }
}

pub fn stage_tone(state: &str) -> &'static str {
    match state {
        "completed" => "success",
        "active" => "active",
        "waiting-authorization" => "waiting",
        "needs-rework" => "warn",
        "rejected" | "failed" => "danger",
        "blocked" => "danger",
        "pending" => "neutral",
        _ => "neutral",
    }
}

/// One operator-facing answer to "is this release running right now?".
///
/// The adapter's journal is historical evidence: a record that says `serving` is not proof that a
/// process is alive after the adapter restarted. `serving_view` therefore never reports `serving`
/// unless the adapter currently tracks a running supervised window. A stale live-looking status is
/// reported as `unknown` with the last observation, so no surface can disagree about it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ServingView {
    pub code: &'static str,
    pub label: &'static str,
    pub tone: &'static str,
    pub detail: Option<String>,
}

const JOURNAL_LIVE_STATUSES: [&str; 3] = ["starting", "running", "serving"];

pub fn serving_view(serving: &Serving) -> ServingView {
    let status = serving.status.as_deref().unwrap_or_default().to_ascii_lowercase();

    if serving.active {
        if status == "starting" {
            return ServingView {
                code: "starting",
                label: "Starting",
                tone: "active",
                detail: None,
            };
        }
        return ServingView {
            code: "serving",
            label: "Serving",
            tone: "success",
            detail: None,
        };
    }

    match status.as_str() {
        "stopping" => ServingView {
            code: "stopping",
            label: "Stopping",
            tone: "active",
            detail: None,
        },
        "failed" | "crashed" => ServingView {
            code: "failed",
            label: "Serve failed",
            tone: "danger",
            detail: serving.detail.clone(),
        },
        "stopped" => ServingView {
            code: "stopped",
            label: "Stopped",
            tone: "neutral",
            detail: serving.detail.clone(),
        },
        "expired" => ServingView {
            code: "expired",
            label: "Serve window expired",
            tone: "neutral",
            detail: serving.detail.clone(),
        },
        live if JOURNAL_LIVE_STATUSES.contains(&live) => ServingView {
            code: "unknown",
            label: "Not currently serving",
            tone: "warn",
            detail: Some(format!(
                "The last serve journal record says \"{live}\", but this adapter tracks no supervised process. Treat the release as not serving until a new serve window proves a running process."
            )),
        },
        "unknown" => ServingView {
            code: "unknown",
            label: "Not currently serving",
            tone: "warn",
            detail: serving.detail.clone(),
        },
        _ => ServingView {
            code: "not-served",
            label: "Not serving",
            tone: "neutral",
            detail: serving.detail.clone(),
        },
    }
}

/// Label for the release-list `servingStatus` string, which is computed by the adapter without the
/// `active` flag available on the detail view. A stale live-looking status is still never "Serving".
pub fn serving_status_label(status: Option<&str>) -> &'static str {
    match status.unwrap_or_default().to_ascii_lowercase().as_str() {
        "serving" => "Serving",
        "starting" => "Starting",
        "stopping" => "Stopping",
        "stopped" => "Stopped",
        "expired" => "Serve window expired",
        "failed" | "crashed" => "Serve failed",
        "unknown" | "stale" => "Not currently serving",
        _ => "Not serving",
    }
}

pub fn serving_status_tone(status: Option<&str>) -> &'static str {
    match status.unwrap_or_default().to_ascii_lowercase().as_str() {
        "serving" => "success",
        "starting" | "stopping" => "active",
        "failed" | "crashed" => "danger",
        "unknown" | "stale" => "warn",
        _ => "neutral",
    }
}

pub fn review_kind_label(kind: &str) -> &str {    match kind {
        "Validation" => "Validation",
        "RequirementCoverage" => "Requirement coverage",
        "ArchitecturalConformance" => "Architectural conformance",
        "Security" => "Security",
        other => other,
    }
}

pub fn role_label(role: &str) -> &'static str {
    match role {
        "viewer" => "Read-only viewer",
        "operator" => "Operator",
        "admin" => "Administrator",
        _ => "Unknown role",
    }
}

pub fn format_duration_seconds(seconds: f64) -> String {
    if !seconds.is_finite() || seconds < 0.0 {
        return "—".to_string();
    }

    if seconds < 60.0 {
        return format!("{:.0}s", seconds);
    }

    let minutes = seconds / 60.0;
    if minutes < 60.0 {
        return format!("{:.0}m", minutes);
    }

    let hours = minutes / 60.0;
    if hours < 48.0 {
        return format!("{:.1}h", hours);
    }

    format!("{:.1}d", hours / 24.0)
}

pub fn format_relative(timestamp: Option<&str>, now: DateTime<Utc>) -> String {
    let Some(timestamp) = timestamp else {
        return "not recorded".to_string();
    };
    let Ok(parsed) = DateTime::parse_from_rfc3339(timestamp) else {
        return timestamp.to_string();
    };
    let parsed = parsed.with_timezone(&Utc);
    let delta = now.signed_duration_since(parsed);
    let seconds = delta.num_seconds();

    if seconds < 0 {
        return "in the future".to_string();
    }
    if seconds < 60 {
        return format!("{seconds}s ago");
    }
    if seconds < 3600 {
        return format!("{}m ago", seconds / 60);
    }
    if seconds < 86_400 {
        return format!("{}h ago", seconds / 3600);
    }
    format!("{}d ago", seconds / 86_400)
}

pub fn short_digest(value: Option<&str>) -> String {
    match value {
        None => "—".to_string(),
        Some(value) if value.len() <= 18 => value.to_string(),
        Some(value) => format!("{}…{}", &value[..12], &value[value.len() - 4..]),
    }
}

pub fn short_sha(value: Option<&str>) -> String {
    value
        .filter(|sha| !sha.is_empty())
        .map(|sha| sha.chars().take(12).collect())
        .unwrap_or_else(|| "—".to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn unknown_states_are_preserved() {
        assert_eq!(state_label("SomeFutureState"), "SomeFutureState");
        assert_eq!(state_tone("SomeFutureState"), "neutral");
    }

    #[test]
    fn duration_formatting_is_readable() {
        assert_eq!(format_duration_seconds(42.0), "42s");
        assert_eq!(format_duration_seconds(600.0), "10m");
    }

    fn serving(active: bool, status: &str) -> Serving {
        Serving {
            work_item_id: Some("WORK-1".to_string()),
            merged_sha: None,
            status: Some(status.to_string()),
            process_id: None,
            restart_count: None,
            detail: None,
            recorded_at_utc: None,
            active,
            url: None,
            job_id: None,
            supervised_until_utc: None,
        }
    }

    #[test]
    fn a_stale_journal_record_is_never_reported_as_serving() {
        for stale in ["serving", "running", "starting"] {
            let view = serving_view(&serving(false, stale));
            assert_eq!(view.code, "unknown", "stale {stale} must not be live");
            assert_ne!(view.label, "Serving");
            assert!(view.detail.is_some());
        }
        assert_eq!(
            serving_status_label(Some("serving")),
            "Serving",
            "the adapter only returns 'serving' once it tracks a live window"
        );
    }

    #[test]
    fn an_active_window_reports_serving_and_terminal_journal_states_stay_terminal() {
        assert_eq!(serving_view(&serving(true, "running")).code, "serving");
        assert_eq!(serving_view(&serving(true, "starting")).code, "starting");
        assert_eq!(serving_view(&serving(false, "failed")).code, "failed");
        assert_eq!(serving_view(&serving(false, "stopped")).code, "stopped");
        assert_eq!(serving_view(&serving(false, "expired")).code, "expired");
        assert_eq!(serving_view(&serving(false, "")).code, "not-served");
    }
}
