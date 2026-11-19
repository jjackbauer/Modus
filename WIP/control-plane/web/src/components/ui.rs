use leptos::prelude::*;

use control_plane_domain::ApiError;
use control_plane_domain::labels::{format_relative, short_digest};

use crate::api::UiError;

#[component]
pub fn Chip(label: String, tone: &'static str) -> impl IntoView {
    view! { <span class=format!("chip chip-{tone}")>{label}</span> }
}

#[component]
pub fn Panel(title: String, children: Children) -> impl IntoView {
    view! {
        <section class="panel">
            <header class="panel-header">
                <h2 class="panel-title">{title}</h2>
            </header>
            <div class="panel-body">{children()}</div>
        </section>
    }
}

#[component]
pub fn PanelActions(title: String, actions: AnyView, children: Children) -> impl IntoView {
    view! {
        <section class="panel">
            <header class="panel-header">
                <h2 class="panel-title">{title}</h2>
                <div class="row wrap">{actions}</div>
            </header>
            <div class="panel-body">{children()}</div>
        </section>
    }
}

#[component]
pub fn MetricCard(
    label: String,
    value: String,
    unit: Option<String>,
    basis: Option<String>,
    tone: &'static str,
    measured: bool,
) -> impl IntoView {
    view! {
        <div class=format!("metric {}", if measured { "metric-measured" } else { "metric-not-measured" })>
            <div class="metric-label">
                <span>{label}</span>
                {(!tone.is_empty()).then(|| view! { <span class=format!("dot dot-{tone}")></span> })}
            </div>
            <div class="metric-value">
                {value}
                {unit.map(|unit| view! { <span class="metric-unit">{unit}</span> })}
            </div>
            {basis.map(|basis| view! { <div class="metric-basis">{basis}</div> })}
        </div>
    }
}

#[component]
pub fn InfoRow(label: String, children: Children) -> impl IntoView {
    view! {
        <div class="info-row">
            <div class="info-label">{label}</div>
            <div class="info-value">{children()}</div>
        </div>
    }
}

#[component]
pub fn DigestLine(label: String, value: Option<String>) -> impl IntoView {
    view! {
        <div class="digest-line">
            <span class="tiny faint">{label}</span>
            <code class="mono" title=value.clone().unwrap_or_default()>{short_digest(value.as_deref())}</code>
        </div>
    }
}

#[component]
pub fn Notice(kind: &'static str, children: Children) -> impl IntoView {
    view! { <div class=format!("banner banner-{kind}")>{children()}</div> }
}

/// Kept as a thin alias so existing call sites converge on one empty-state implementation instead
/// of a second visual variant.
#[component]
pub fn EmptyBlock(title: &'static str, detail: &'static str) -> impl IntoView {
    view! { <crate::components::feedback::EmptyState title=title detail=detail/> }
}

#[component]
pub fn LoadingBlock(label: &'static str) -> impl IntoView {
    view! {
        <div class="loading-state" role="status" aria-live="polite">
            <span class="spinner" aria-hidden="true"></span>
            <span>{label}</span>
        </div>
    }
}

pub fn error_view(error: UiError) -> impl IntoView {
    view! {
        <div class="error-state" role="alert">
            <strong>"The control plane could not complete this request."</strong>
            <p>{error.message.clone()}</p>
            <div class="tiny mono">{format!("{} · {}", error.status, error.code)}</div>
            {error.retryable.then(|| view! { <p class="small">"This error may be transient. Retry after checking the factory connection."</p> })}
        </div>
    }
}

pub fn error_from_api(error: ApiError) -> UiError {
    UiError {
        status: 0,
        code: error.error.code,
        message: error.error.message,
        category: error.error.category,
        retryable: error.error.retryable,
    }
}

pub fn time_ago(value: Option<&str>) -> String {
    format_relative(value, chrono::Utc::now())
}

pub fn timestamp(value: Option<&str>) -> String {
    let Some(value) = value else {
        return "Not recorded".to_string();
    };
    chrono::DateTime::parse_from_rfc3339(value)
        .map(|value| {
            value
                .with_timezone(&chrono::Utc)
                .format("%Y-%m-%d %H:%M:%S UTC")
                .to_string()
        })
        .unwrap_or_else(|_| value.to_string())
}

pub fn tone_for_role(role: &str) -> &'static str {
    match role {
        "admin" => "danger",
        "operator" => "active",
        _ => "neutral",
    }
}

/// Copies a value an operator may need to quote, and reports success on the button itself. The
/// clipboard API is unavailable outside a secure context, so the failure is silent here by design:
/// the value stays selectable either way.
pub fn copy_text(value: &str, copied: RwSignal<bool>) {
    #[cfg(target_arch = "wasm32")]
    {
        if let Some(window) = web_sys::window() {
            let promise = window.navigator().clipboard().write_text(value);
            leptos::task::spawn_local(async move {
                let _ = wasm_bindgen_futures::JsFuture::from(promise).await;
                copied.set(true);
            });
        }
    }
    #[cfg(not(target_arch = "wasm32"))]
    {
        let _ = (value, copied);
    }
}

/// A digest an operator is about to bind a decision to: always shown in full, wrap-safe, and
/// copyable in one action. Truncating this value is a safety defect, not a density choice.
#[component]
pub fn DigestCopy(label: String, value: Option<String>) -> impl IntoView {
    let usable = value.filter(|value| !value.is_empty());
    let display = usable
        .clone()
        .unwrap_or_else(|| "Not recorded".to_string());
    let copy_label = format!("Copy {label} to clipboard");
    let copied = RwSignal::new(false);

    view! {
        <div class="digest-copy">
            <div class="label">{label}</div>
            <div class="digest-copy-row">
                <code class="mono digest-copy-value">{display}</code>
                {usable.map(|value| {
                    view! {
                        <button
                            class="btn btn-quiet btn-sm"
                            type="button"
                            aria-label=copy_label.clone()
                            on:click=move |_| {
                                let value = value.clone();
                                copy_text(&value, copied);
                            }
                        >
                            <span aria-live="polite">
                                {move || if copied.get() { "Copied" } else { "Copy" }}
                            </span>
                        </button>
                    }
                })}
            </div>
        </div>
    }
}

#[component]
pub fn MiniBar(label: String, value: f64, max: f64, display: String) -> impl IntoView {    let percent = if max <= 0.0 {
        0.0
    } else {
        (value / max * 100.0).clamp(0.0, 100.0)
    };
    view! {
        <div class="mini-bar">
            <div class="row-between tiny">
                <span>{label}</span>
                <span class="mono">{display}</span>
            </div>
            <div class="bar-track">
                <div class="bar-fill" style=format!("width:{percent}%")></div>
            </div>
        </div>
    }
}
