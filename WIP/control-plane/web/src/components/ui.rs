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

#[component]
pub fn EmptyBlock(title: &'static str, detail: &'static str) -> impl IntoView {
    view! {
        <div class="empty-state">
            <div class="empty-mark" aria-hidden="true">"∅"</div>
            <h2>{title}</h2>
            <p>{detail}</p>
        </div>
    }
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

#[component]
pub fn MiniBar(label: String, value: f64, max: f64, display: String) -> impl IntoView {
    let percent = if max <= 0.0 {
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
