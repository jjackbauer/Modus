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

/// What an operator should believe about one release serving state. This is a statement about
/// evidence, not advice: it says what the adapter can prove right now.
pub fn serving_status_explanation(status: Option<&str>) -> &'static str {
    match status.unwrap_or_default().to_ascii_lowercase().as_str() {
        "serving" => {
            "The adapter tracks a running supervised process for this revision. The served URL responds only if the frozen contract passed against that process."
        }
        "starting" => {
            "The supervised process is starting. The adapter publishes the served URL only after the frozen contract passes against the running process."
        }
        "stopping" => "The bounded serve window is ending; the process is being stopped by the factory.",
        "stopped" => {
            "The last bounded window ended normally. The release and its verification evidence remain recorded."
        }
        "expired" => {
            "The bounded serve window expired. The release and its verification evidence remain recorded."
        }
        "failed" | "crashed" => {
            "The serve attempt failed. The failure detail belongs to the release record; it is not a claim about the merged revision."
        }
        "unknown" | "stale" => {
            "The journal says this revision was serving, but the adapter tracks no supervised process for it. Treat the release as not serving until a new serve window proves a running process."
        }
        _ => "No serve record exists for this revision. A merge is not a running application.",
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

/// One vocabulary for a recorded outcome, shared by every surface that shows one. A code this
/// build does not know is returned unchanged: a future factory outcome must stay readable as
/// itself instead of being renamed, hidden or uppercased by the console.
pub fn outcome_label(value: &str) -> &str {
    match value {
        "passed" => "Passed",
        "failed" => "Failed",
        "succeeded" => "Succeeded",
        "completed" => "Completed",
        "cancelled" => "Cancelled",
        "accepted" => "Accepted",
        "rejected" => "Rejected",
        "pending" => "Pending",
        "not-run" => "Not run",
        "running" => "Running",
        "starting" => "Starting",
        "stopping" => "Stopping",
        "timeout" | "timed-out" => "Timed out",
        _ => value,
    }
}

/// Operator-facing severity. `critical` is a statement about consequence, not decoration.
pub fn severity_label(value: &str) -> &str {
    match value {
        "critical" => "Critical",
        "warning" => "Warning",
        "info" => "Info",
        _ => value,
    }
}

/// Derived execution health of one work item. This is the single label set used by the work
/// header, the queue and any attention row that reports health; it never renames the
/// authoritative factory state.
pub fn health_label(health: &str) -> &str {
    match health {
        "serving" => "Serving",
        "operational" => "Operational",
        "release-verified" => "Release verified",
        "terminal-failure" => "Terminal failure",
        "blocked" => "Blocked",
        "waiting-authorization" => "Waiting for authorization",
        "awaiting-rework" => "Awaiting rework",
        "executing" => "Executing",
        "dispatching" => "Dispatching",
        "awaiting-dispatch" => "Awaiting dispatch",
        "idle" => "Idle",
        other => other,
    }
}

pub fn health_tone(health: &str) -> &'static str {
    match health {
        "serving" | "operational" | "release-verified" => "success",
        "terminal-failure" | "blocked" => "danger",
        "waiting-authorization" | "awaiting-rework" | "awaiting-dispatch" => "waiting",
        "executing" | "dispatching" => "active",
        _ => "neutral",
    }
}

/// One lifecycle stage's state as the work item shows it. Stage states are derived from the
/// authoritative state plus evidence, so they are labelled in the same dictionary as everything
/// else and never uppercased to look like a status code.
pub fn stage_state_label(state: &str) -> &str {
    match state {
        "completed" => "Completed",
        "active" => "Active",
        "waiting-authorization" => "Waiting for authorization",
        "needs-rework" => "Needs rework",
        "rejected" => "Rejected",
        "failed" => "Failed",
        "blocked" => "Blocked",
        "pending" => "Pending",
        other => other,
    }
}

/// Delivery freshness of the activity projection. A transport problem is never phrased as a
/// factory failure: only the delivery of new facts is affected, and the last durable facts stay
/// visible.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Freshness {
    Live,
    Reconnecting,
    Stale,
    Disconnected,
}

impl Freshness {
    pub fn code(self) -> &'static str {
        match self {
            Freshness::Live => "live",
            Freshness::Reconnecting => "reconnecting",
            Freshness::Stale => "stale",
            Freshness::Disconnected => "disconnected",
        }
    }

    pub fn label(self) -> &'static str {
        match self {
            Freshness::Live => "Live",
            Freshness::Reconnecting => "Reconnecting",
            Freshness::Stale => "Stale",
            Freshness::Disconnected => "Disconnected",
        }
    }

    pub fn tone(self) -> &'static str {
        match self {
            Freshness::Live => "success",
            Freshness::Reconnecting => "active",
            Freshness::Stale => "warn",
            Freshness::Disconnected => "danger",
        }
    }

    pub fn detail(self) -> &'static str {
        match self {
            Freshness::Live => "New factory events arrive as they are recorded.",
            Freshness::Reconnecting => {
                "The activity stream is reconnecting. Durable facts stay readable; only delivery is delayed."
            }
            Freshness::Stale => {
                "The stream is open but its last update is older than expected. Last durable facts are shown."
            }
            Freshness::Disconnected => {
                "The activity stream is disconnected. Nothing on this page is a factory failure because of it."
            }
        }
    }
}

/// One decision about transport freshness, used by every surface instead of each page inventing
/// its own wording for the same connection state.
pub fn freshness_view(connected: bool, stale: bool, gateway_error: Option<&str>) -> Freshness {
    if connected && !stale {
        return Freshness::Live;
    }
    if connected {
        return Freshness::Stale;
    }
    match gateway_error {
        Some(message) if message.to_ascii_lowercase().contains("reconnect") => Freshness::Reconnecting,
        Some(_) => Freshness::Disconnected,
        None => Freshness::Reconnecting,
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

    #[test]
    fn unknown_outcomes_are_preserved_instead_of_uppercased_or_hidden() {
        assert_eq!(outcome_label("passed"), "Passed");
        assert_eq!(outcome_label("accepted"), "Accepted");
        assert_eq!(outcome_label("quarantined"), "quarantined");
        assert_eq!(severity_label("critical"), "Critical");
        assert_eq!(severity_label("urgent"), "urgent");
    }

    #[test]
    fn health_labels_cover_every_derived_health_state_the_adapter_publishes() {
        for health in [
            "serving",
            "operational",
            "release-verified",
            "terminal-failure",
            "blocked",
            "waiting-authorization",
            "awaiting-rework",
            "executing",
            "dispatching",
            "awaiting-dispatch",
            "idle",
        ] {
            assert_ne!(health_label(health), health, "{health} needs an operator label");
        }
        assert_eq!(health_label("serving"), "Serving");
        assert_eq!(health_tone("serving"), "success");
        assert_eq!(health_tone("terminal-failure"), "danger");
        assert_eq!(health_tone("made-up"), "neutral");
    }

    #[test]
    fn transport_freshness_never_becomes_a_factory_failure() {
        assert_eq!(freshness_view(true, false, None), Freshness::Live);
        assert_eq!(freshness_view(true, true, None), Freshness::Stale);
        assert_eq!(
            freshness_view(false, true, Some("The activity stream is reconnecting.")),
            Freshness::Reconnecting
        );
        assert_eq!(
            freshness_view(false, true, Some("Permission denied for the stream.")),
            Freshness::Disconnected
        );
        assert_eq!(freshness_view(false, true, None), Freshness::Reconnecting);
        assert_eq!(Freshness::Live.tone(), "success");
        assert_eq!(Freshness::Disconnected.tone(), "danger");
        assert_ne!(Freshness::Stale.label(), "Failed");
    }
}
