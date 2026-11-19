use leptos::prelude::*;
use leptos_router::hooks::use_params_map;

use control_plane_domain::{format_relative, short_digest};

use crate::api;
use crate::components::ui::{
    Chip, EmptyBlock, LoadingBlock, MetricCard, Notice, Panel, error_view,
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

            <Suspense fallback=move || view! { <LoadingBlock label="Loading registered roles, profiles and sessions…"/> }>
                {move || match agents.get().flatten() {
                    None => view! { <LoadingBlock label="Loading registered roles, profiles and sessions…"/> }.into_any(),
                    Some(Err(error)) => view! { {error_view(error)} }.into_any(),
                    Some(Ok(agents)) => {
                        let total_sessions: i32 = agents.iter().map(|agent| agent.session_count).sum();
                        let total_attempts: i32 = agents.iter().map(|agent| agent.attempt_count).sum();
                        let measured = agents.iter().any(|agent| agent.tokens_measured);
                        let tokens: i32 = agents.iter().map(|agent| agent.input_tokens + agent.output_tokens).sum();
                        view! {
                            <div class="metric-grid">
                                <MetricCard label="Registered roles".to_string() value=agents.len().to_string() unit=None basis=Some("Factory role definitions".to_string()) tone="" measured=true/>
                                <MetricCard label="Recorded sessions".to_string() value=total_sessions.to_string() unit=None basis=Some("Durable session traces".to_string()) tone="active" measured=true/>
                                <MetricCard label="Implementation attempts".to_string() value=total_attempts.to_string() unit=None basis=Some("Attempt journals".to_string()) tone="" measured=true/>
                                <MetricCard
                                    label="Tokens".to_string()
                                    value=if measured { tokens.to_string() } else { "Not measured".to_string() }
                                    unit=if measured { Some("tokens".to_string()) } else { None }
                                    basis=Some(if measured { "Provider-reported usage".to_string() } else { "No measured provider usage in durable traces".to_string() })
                                    tone=""
                                    measured=measured
                                />
                            </div>

                            <div class="agent-grid">
                                {agents.into_iter().map(|agent| {
                                    let href = format!("/i/{}/agents/{}", instance.get(), agent.definition_id);
                                    let active = agent.active_profile_version.map(|version| format!("v{version}")).unwrap_or_else(|| "no active profile".to_string());
                                    view! {
                                        <a class="agent-card" href=href>
                                            <div class="row-between">
                                                <Chip label=agent.definition_id.replace("factory.", "").to_uppercase() tone="neutral"/>
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
            </Suspense>
        </section>
    }
}
