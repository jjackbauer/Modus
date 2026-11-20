use leptos::prelude::*;
use leptos_router::hooks::use_params_map;

use control_plane_domain::format_relative;

use crate::api;
use crate::components::ui::{
    LoadingBlock, Notice, error_view,
};
use crate::context::AppContext;

#[component]
pub fn AgentsPage() -> impl IntoView {
    let context = AppContext::provide();
    let params = use_params_map();
    let instance = Signal::derive(move || params.get().get("instance").unwrap_or_default());
    let refresh = context.refresh;

    let agents = LocalResource::new(move || {
        let instance = instance.get();
        let _ = refresh.get();
        async move {
            if !api::is_client() || instance.is_empty() {
                None
            } else {
                Some(api::agents(&instance).await)
            }
        }
    });

    view! {
        <section class="page">
            <header class="page-header">
                <div>
                    <h1 class="page-title">Agents</h1>
                    <p class="page-subtitle">
                        "Logical role, immutable profile version, runtime session and executing process are different things. The factory declares nine roles; only the roles with a real execution path produce sessions."
                    </p>
                </div>
                <div class="page-actions">
                    <button class="btn" on:click=move |_| context.touch()>"Refresh"</button>
                </div>
            </header>

            <Notice kind="accent">
                <strong>"No fabricated agent fleet. "</strong>
                <span>"This inventory reads the factory's role definitions, profile registry, session traces and attempt journal. It does not imply nine continuously running processes."</span>
            </Notice>

            <Transition fallback=move || view! { <LoadingBlock label="Loading registered roles, profiles and sessions…"/> }>
                {move || match agents.get().flatten() {
                    None => view! { <LoadingBlock label="Loading registered roles, profiles and sessions…"/> }.into_any(),
                    Some(Err(error)) => view! { {error_view(error)} }.into_any(),
                    Some(Ok(agents)) => {
                        let total_sessions: i32 = agents.iter().map(|agent| agent.session_count).sum();
                        let total_attempts: i32 = agents.iter().map(|agent| agent.attempt_count).sum();
                        let measured = agents.iter().any(|agent| agent.tokens_measured);
                        let tokens: i32 = agents.iter().map(|agent| agent.input_tokens + agent.output_tokens).sum();
                        view! {
                            <div class="count-summary">
                                <span><span class="faint">"Registered roles "</span><strong>{agents.len()}</strong></span>
                                <span><span class="faint">"Recorded sessions "</span><strong>{total_sessions}</strong></span>
                                <span><span class="faint">"Implementation attempts "</span><strong>{total_attempts}</strong></span>
                                <span><span class="faint">"Tokens "</span><strong>{if measured { tokens.to_string() } else { "Not measured".to_string() }}</strong></span>
                            </div>
                            <div class="tiny faint definitions">
                                "Registered roles = factory role definitions. Sessions and attempts = durable traces. Tokens = provider-reported usage, or Not measured when no trace reports it."
                            </div>

                            <div class="agent-grid">
                                {agents.into_iter().map(|agent| {
                                    let href = format!("/i/{}/agents/{}", instance.get(), agent.definition_id);
                                    let active = agent.active_profile_version.map(|version| format!("v{version}")).unwrap_or_else(|| "no active profile".to_string());
                                    view! {
                                        <a class="agent-card" href=href>
                                            <div class="row-between">
                                                <span class="mono tiny">{agent.definition_id.clone()}</span>
                                                <span class="tiny faint">{agent.published_profile_count}" profile version(s)"</span>
                                            </div>
                                            <h2>{agent.role.clone()}</h2>
                                            <p class="small muted">{agent.objective.clone()}</p>
                                            <div class="agent-stats">
                                                <div><span class="faint tiny">"Active profile"</span><strong>{active}</strong></div>
                                                <div><span class="faint tiny">"Sessions"</span><strong>{agent.session_count}</strong></div>
                                                <div><span class="faint tiny">"Attempts"</span><strong>{agent.attempt_count}</strong></div>
                                                <div><span class="faint tiny">"Last activity"</span><strong>{format_relative(agent.last_activity_utc.as_deref(), chrono::Utc::now())}</strong></div>
                                            </div>
                                            <div class="tiny faint">{agent.implemented_by.join(" · ")}</div>
                                        </a>
                                    }
                                }).collect_view()}
                            </div>
                        }.into_any()
                    }
                }}
            </Transition>
        </section>
    }
}
