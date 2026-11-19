use leptos::prelude::*;

pub mod agent_detail;
pub mod agents;
pub mod analytics;
pub mod improvement;
pub mod not_found;
pub mod overview;
pub mod pipeline;
pub mod quality;
pub mod release_detail;
pub mod releases;
pub mod settings;
pub mod work;
pub mod work_detail;

#[component]
pub fn Placeholder(title: &'static str, detail: &'static str) -> impl IntoView {
    view! {
        <section class="page">
            <h1 class="page-title">{title}</h1>
            <p class="page-subtitle">{detail}</p>
            <div class="panel muted-panel">
                <p>"This view is being wired to the live factory adapter."</p>
            </div>
        </section>
    }
}
