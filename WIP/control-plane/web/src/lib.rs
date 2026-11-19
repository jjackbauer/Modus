#![recursion_limit = "256"]

use leptos::prelude::*;
use leptos_router::components::{Route, Router, Routes};
use leptos_router::path;

pub mod activity;
pub mod api;
pub mod components;
pub mod context;
pub mod pages;
pub mod template_hygiene;

use components::shell::AppShell;
use context::AppContext;

#[component]
pub fn App() -> impl IntoView {
    let context = AppContext::provide();
    context::install_session_loader(context);
    view! {
        <Router>
            <AppShell>
                <Routes fallback=|| view! { <pages::not_found::NotFound/> }>
                    <Route path=path!("/i/:instance/overview") view=pages::overview::OverviewPage/>
                    <Route path=path!("/i/:instance/work") view=pages::work::WorkQueuePage/>
                    <Route path=path!("/i/:instance/work/:work_item_id") view=pages::work_detail::WorkDetailPage/>
                    <Route path=path!("/i/:instance/pipeline") view=pages::pipeline::PipelinePage/>
                    <Route path=path!("/i/:instance/agents") view=pages::agents::AgentsPage/>
                    <Route path=path!("/i/:instance/agents/:definition_id") view=pages::agent_detail::AgentDetailPage/>
                    <Route path=path!("/i/:instance/quality") view=pages::quality::QualityPage/>
                    <Route path=path!("/i/:instance/releases") view=pages::releases::ReleasesPage/>
                    <Route path=path!("/i/:instance/releases/:work_item_id") view=pages::release_detail::ReleaseDetailPage/>
                    <Route path=path!("/i/:instance/analytics") view=pages::analytics::AnalyticsPage/>
                    <Route path=path!("/i/:instance/improvement") view=pages::improvement::ImprovementPage/>
                    <Route path=path!("/i/:instance/settings") view=pages::settings::SettingsPage/>
                </Routes>
            </AppShell>
        </Router>
    }
}

/// Extracts the instance id from `/i/{instance}/...` for chrome rendered outside the matched route.
pub fn instance_from_path(path: &str) -> Option<String> {
    let mut parts = path.split('/').filter(|part| !part.is_empty());
    if parts.next()? != "i" {
        return None;
    }
    let instance = parts.next()?.to_string();
    if parts.next().is_some() {
        Some(instance)
    } else {
        None
    }
}
