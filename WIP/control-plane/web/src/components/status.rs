//! Status, identity and freshness primitives.
//!
//! One status never appears twice on a surface: the authoritative state, the derived health and
//! the transport freshness each own a distinct primitive. Every label comes from the domain
//! dictionary (`control_plane_domain::labels`), so no page can invent a second name for the same
//! fact. Status is text plus a restrained mark; the text alone is sufficient, which is why the
//! dot is `aria-hidden`.

use leptos::prelude::*;

use control_plane_domain::labels::{freshness_view, health_label, health_tone};

use crate::components::ui::{copy_text, time_ago, timestamp};

/// Restricts the tone to the documented set so a caller cannot smuggle arbitrary CSS into a
/// status class.
pub fn tone_class(tone: &str) -> &'static str {
    match tone {
        "success" => "success",
        "danger" => "danger",
        "warn" => "warn",
        "waiting" => "waiting",
        "active" => "active",
        "info" => "info",
        _ => "neutral",
    }
}

/// Text plus tone. Colour is never the only carrier: remove it and the label still answers the
/// question. `title` may carry the raw code for traceability.
#[component]
pub fn StatusIndicator(
    #[prop(into)] label: String,
    tone: &'static str,
    #[prop(optional)] raw: Option<String>,
) -> impl IntoView {
    let class = format!("status status-{}", tone_class(tone));
    view! {
        <span class=class title=raw.unwrap_or_default()>
            <span class="dot" aria-hidden="true"></span>
            <span class="status-label">{label}</span>
        </span>
    }
}

/// Derived execution health of a work item. The authoritative state is rendered separately; this
/// answers "what is the execution doing", never "which state is the item in".
#[component]
pub fn HealthState(health: String) -> impl IntoView {
    let raw = health.clone();
    view! { <StatusIndicator label=health_label(&health).to_string() tone=health_tone(&health) raw=raw/> }
}

/// A full identifier an operator may need to quote or paste elsewhere. It is never truncated,
/// wraps at any character instead of forcing a horizontal scrollbar, and offers one-click copy.
#[component]
pub fn EntityIdentity(
    #[prop(into)] label: String,
    #[prop(into)] value: String,
    #[prop(optional)] href: Option<String>,
) -> impl IntoView {
    let copied = RwSignal::new(false);
    let copy_label = format!("Copy {label} {value} to clipboard");
    let value_for_copy = value.clone();
    let value_for_display = value.clone();
    view! {
        <span class="entity-identity">
            <span class="entity-identity-label">{label}</span>
            {match href {
                Some(href) => view! {
                    <a class="mono entity-identity-value" href=href>{value_for_display.clone()}</a>
                }.into_any(),
                None => view! {
                    <code class="mono entity-identity-value">{value_for_display.clone()}</code>
                }.into_any(),
            }}
            <button
                class="btn btn-quiet btn-xs"
                type="button"
                aria-label=copy_label
                on:click=move |_| copy_text(&value_for_copy, copied)
            >
                <span aria-live="polite">{move || if copied.get() { "Copied" } else { "Copy" }}</span>
            </button>
        </span>
    }
}

/// Label/value metadata. The list is a `dl`, so a screen reader keeps each label attached to its
/// value instead of reading a flat run of text.
#[component]
pub fn MetadataList(children: Children) -> impl IntoView {
    view! { <dl class="metadata-list">{children()}</dl> }
}

#[component]
pub fn MetadataRow(#[prop(into)] label: String, children: Children) -> impl IntoView {
    view! {
        <div>
            <dt>{label}</dt>
            <dd>{children()}</dd>
        </div>
    }
}

/// Transport freshness of the activity projection plus the last time a fact arrived. It never
/// claims the factory failed: a disconnect changes delivery, not factory truth.
#[component]
pub fn FreshnessIndicator(
    #[prop(into)] connected: Signal<bool>,
    #[prop(into)] stale: Signal<bool>,
    #[prop(into)] gateway_error: Signal<Option<String>>,
    #[prop(into)] last_success: Signal<Option<String>>,
) -> impl IntoView {
    let state = move || {
        freshness_view(
            connected.get(),
            stale.get(),
            gateway_error.get().as_deref(),
        )
    };
    view! {
        <div class="freshness">
            <div role="status" aria-live="polite">
                {move || {
                    let state = state();
                    view! { <StatusIndicator label=state.label().to_string() tone=state.tone() raw=state.code().to_string()/> }
                }}
            </div>
            <dl class="metadata-list compact">
                <div>
                    <dt>"Last successful update"</dt>
                    <dd
                        class="strong"
                        title=move || timestamp(last_success.get().as_deref())
                    >
                        {move || match last_success.get() {
                            Some(value) => time_ago(Some(value.as_str())),
                            None => "Not yet received in this session".to_string(),
                        }}
                    </dd>
                </div>
                <div>
                    <dt>"Stream"</dt>
                    <dd class="muted">{move || state().detail()}</dd>
                </div>
            </dl>
        </div>
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn only_documented_tones_become_css_classes() {
        assert_eq!(tone_class("success"), "success");
        assert_eq!(tone_class("danger"), "danger");
        assert_eq!(tone_class("waiting"), "waiting");
        assert_eq!(tone_class("success; background:url(x)"), "neutral");
        assert_eq!(tone_class(""), "neutral");
    }
}
