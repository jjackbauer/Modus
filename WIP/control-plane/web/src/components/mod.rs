pub mod planning;
pub mod shell;
pub mod ui;

use leptos::prelude::*;

#[component]
pub fn PageHeader(
    title: impl IntoView + 'static,
    subtitle: impl IntoView + 'static,
    children: Children,
) -> impl IntoView {
    view! {
        <header class="page-header">
            <div>
                <h1 class="page-title">{title}</h1>
                <p class="page-subtitle">{subtitle}</p>
            </div>
            <div class="page-actions">{children()}</div>
        </header>
    }
}

#[component]
pub fn EmptyState(title: &'static str, detail: &'static str) -> impl IntoView {
    view! {
        <div class="empty-state">
            <div class="empty-mark" aria-hidden="true">"∅"</div>
            <h2>{title}</h2>
            <p>{detail}</p>
        </div>
    }
}

#[component]
pub fn LoadingState(label: &'static str) -> impl IntoView {
    view! {
        <div class="loading-state" role="status" aria-live="polite">
            <span class="spinner" aria-hidden="true"></span>
            <span>{label}</span>
        </div>
    }
}

#[component]
pub fn ErrorState(summary: String, detail: Option<String>) -> impl IntoView {
    view! {
        <div class="error-state" role="alert">
            <strong>"The control plane could not load this view."</strong>
            <p>{summary}</p>
            {detail.map(|detail| view! { <pre>{detail}</pre> })}
        </div>
    }
}

#[component]
pub fn ToneChip(label: String, tone: &'static str) -> impl IntoView {
    view! { <span class=format!("chip chip-{tone}")>{label}</span> }
}
