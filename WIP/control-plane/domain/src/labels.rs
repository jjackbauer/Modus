//! Presentation-neutral labels and tones. Pages use these so the same state never has two
//! different names in two screens.

use chrono::{DateTime, Utc};

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

pub fn review_kind_label(kind: &str) -> &str {
    match kind {
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
}
