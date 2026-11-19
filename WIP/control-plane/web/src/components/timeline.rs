//! Ordered durable timelines.
//!
//! The timeline is a list of facts, not a feed of decorative cards. It renders an ordered list so
//! assistive technology can announce the item count and position, with one time column and one
//! content column per entry. Pages supply the entry headings, states and details; they must not
//! rebuild the list/grid contract themselves.

use leptos::prelude::*;

/// A timeline must have a name so a screen reader can distinguish it from other lists on the page.
pub fn timeline_accessible_label(label: &str) -> String {
    let trimmed = label.trim();
    if trimmed.is_empty() {
        "Timeline".to_string()
    } else {
        trimmed.to_string()
    }
}

/// An ordered list of durable events, attempts or sessions.
#[component]
pub fn Timeline(
    #[prop(into)] label: String,
    #[prop(optional)] compact: bool,
    children: Children,
) -> impl IntoView {
    let label = timeline_accessible_label(&label);
    let class = if compact { "timeline compact" } else { "timeline" };
    view! {
        <ol class=class aria-label=label>
            {children()}
        </ol>
    }
}

/// One timeline entry. The time column is optional in content but always rendered so the entries
/// stay aligned; `time_title` carries the exact UTC instant for pointer and keyboard users.
#[component]
pub fn TimelineItem(
    #[prop(into)] time: String,
    #[prop(optional, into)] time_title: String,
    children: Children,
) -> impl IntoView {
    view! {
        <li class="event">
            <div class="event-time" title=time_title>{time}</div>
            <div class="event-main">{children()}</div>
        </li>
    }
}

#[cfg(test)]
mod tests {
    use super::timeline_accessible_label;

    #[test]
    fn timeline_accessible_label_keeps_a_supplied_name() {
        assert_eq!(
            timeline_accessible_label("Implementation attempts"),
            "Implementation attempts"
        );
    }

    #[test]
    fn timeline_accessible_label_supplies_a_name_when_empty() {
        assert_eq!(timeline_accessible_label("   "), "Timeline");
        assert_eq!(timeline_accessible_label(""), "Timeline");
    }
}
