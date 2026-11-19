use leptos::prelude::*;
use leptos_router::hooks::use_params_map;

use control_plane_domain::{AgentDetail, short_digest};

use crate::api;
use crate::components::ui::{Chip, EmptyBlock, LoadingBlock, Notice, Panel, error_view, time_ago};
use crate::components::{Timeline, TimelineItem};
use crate::context::AppContext;

#[component]
pub fn ImprovementPage() -> impl IntoView {
    let context = AppContext::provide();
    let params = use_params_map();
    let instance = Signal::derive(move || params.get().get("instance").unwrap_or_default());
    let selected = RwSignal::new(String::new());
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

    Effect::new(move |_| {
        if selected.get().is_empty() {
            if let Some(Ok(agents)) = agents.get().flatten() {
                if let Some(first) = agents.first() {
                    selected.set(first.definition_id.clone());
                }
            }
        }
    });

    let detail = LocalResource::new(move || {
        let instance = instance.get();
        let definition = selected.get();
        let _ = refresh.get();
        async move {
            if !api::is_client() || instance.is_empty() || definition.is_empty() {
                None
            } else {
                Some(api::agent(&instance, &definition).await)
            }
        }
    });

    view! {
        <section class="page">
            <header class="page-header">
                <div>
                    <h1 class="page-title">Improvement & knowledge</h1>
                    <p class="page-subtitle">
                        "What the factory's adaptation plane is attempting to learn, which immutable profile versions exist, and which changes were promoted or rolled back."
                    </p>
                </div>
                <div class="page-actions">
                    <button class="btn" on:click=move |_| context.touch()>"Refresh"</button>
                </div>
            </header>

            <Notice kind="accent">
                <strong>"This is not a second self-improvement engine. "</strong>
                <span>"Proposals, evaluations and promotions remain the factory's responsibility. The control plane only displays its versioned adaptation evidence and provides the governed profile-management surface."</span>
            </Notice>

            <Suspense fallback=move || view! { <LoadingBlock label="Loading adaptation evidence…"/> }>
                {move || match agents.get().flatten() {
                    None => view! { <LoadingBlock label="Loading adaptation evidence…"/> }.into_any(),
                    Some(Err(error)) => view! { {error_view(error)} }.into_any(),
                    Some(Ok(agents)) => view! {
                        <div class="row wrap">
                            <label class="field">
                                <span class="label">"Role"</span>
                                <select class="select" on:change=move |event| selected.set(event_target_value(&event))>
                                    {agents.into_iter().map(|agent| view! {
                                        <option value=agent.definition_id.clone() selected=move || selected.get() == agent.definition_id>{agent.role}</option>
                                    }).collect_view()}
                                </select>
                            </label>
                        </div>
                    }.into_any(),
                }}
            </Suspense>

            <Suspense fallback=move || view! { <LoadingBlock label="Loading profile history and adaptation events…"/> }>
                {move || match detail.get().flatten() {
                    None => view! { <LoadingBlock label="Loading profile history and adaptation events…"/> }.into_any(),
                    Some(Err(error)) => view! { {error_view(error)} }.into_any(),
                    Some(Ok(agent)) => view! { <ImprovementBody agent=agent/> }.into_any(),
                }}
            </Suspense>
        </section>
    }
}

#[component]
fn ImprovementBody(agent: AgentDetail) -> impl IntoView {
    let summary = agent.summary;
    let profiles = agent.profiles;
    let events = agent.improvement_events;
    let sessions = agent.sessions;

    view! {
        <div class="split">
            <div class="stack">
                <Panel title="Immutable profile history".to_string()>
                    {if profiles.is_empty() {
                        view! { <EmptyBlock title="No published versions" detail="This role has no immutable profile version yet. The definition still declares its ceiling and required evaluation gates."/> }.into_any()
                    } else {
                        view! {
                            <div class="stack">
                                {profiles.into_iter().map(|profile| view! {
                                    <div class="profile-history">
                                        <div class="row-between">
                                            <div class="row">
                                                <Chip label=format!("v{}", profile.version) tone=if profile.active { "success" } else { "neutral" }/>
                                                {profile.parent_version.map(|parent| view! { <span class="tiny faint">"from v"{parent}</span> })}
                                            </div>
                                            <span class="tiny faint">{time_ago(Some(&profile.published_at_utc.to_rfc3339()))}</span>
                                        </div>
                                        <div class="small muted">{profile.change_reason.clone()}</div>
                                        <div class="space-between tiny">
                                            <span class="mono">{profile.model_provider.clone()}" / "{profile.preferred_model.clone()}</span>
                                            <span class="mono">{short_digest(Some(&profile.content_digest))}</span>
                                        </div>
                                        <div class="tiny faint">"Gates: "{profile.hard_gates.join(", ")}" · rollout: "{profile.candidate_rollout}</div>
                                    </div>
                                }).collect_view()}
                            </div>
                        }.into_any()
                    }}
                </Panel>

                <Panel title="Adaptation events".to_string()>
                    {if events.is_empty() {
                        view! { <EmptyBlock title="No adaptation events recorded" detail="Reflection requested, candidate evaluated, profile promoted and profile rolled back appear here only when the factory records them. No activity is fabricated for this page."/> }.into_any()
                    } else {
                        view! {
                            <Timeline label="Adaptation events">
                                {events.into_iter().map(|event| view! {
                                    <TimelineItem time=time_ago(Some(&event.time_utc.to_rfc3339()))>
                                        <div class="event-title">
                                            <Chip label=adaptation_kind(&event.r#type).to_string() tone=adaptation_tone(&event.r#type)/>
                                            <span class="tiny faint mono">{event.r#type.clone()}</span>
                                        </div>
                                        <div class="small muted">{event.summary.clone().unwrap_or_default()}</div>
                                        <details class="evidence"><summary>"Evidence data"</summary><pre>{serde_json::to_string_pretty(&event.data).unwrap_or_default()}</pre></details>
                                    </TimelineItem>
                                }).collect_view()}
                            </Timeline>
                        }.into_any()
                    }}
                </Panel>
            </div>

            <div class="stack">
                <Panel title="What this means".to_string()>
                    <div class="stack small muted">
                        <p><strong>"Proposal"</strong>" is a factory-recorded reflection or candidate evaluation. It does not change the active profile."</p>
                        <p><strong>"Applied"</strong>" is a promotion or rollback recorded by the factory. The active pointer affects future sessions only; a running session keeps its pinned version."</p>
                        <p><strong>"Observed"</strong>" is downstream evidence gathered after promotion. This control plane does not rank agents by raw activity or invent improvement scores."</p>
                    </div>
                </Panel>

                <Panel title="Recent session outcomes".to_string()>
                    {if sessions.is_empty() {
                        view! { <div class="tiny faint">"No durable sessions for this role."</div> }.into_any()
                    } else {
                        view! {
                            <div class="stack">
                                {sessions.into_iter().take(8).map(|session| view! {
                                    <div class="row-between">
                                        <span class="tiny mono">{short_digest(Some(&session.session_id))}</span>
                                        <Chip label=session.outcome.to_uppercase() tone=if session.outcome == "completed" { "success" } else { "danger" }/>
                                    </div>
                                }).collect_view()}
                            </div>
                        }.into_any()
                    }}
                </Panel>

                <Panel title="Active profile".to_string()>
                    <div class="stack">
                        <div class="row-between"><span class="muted">"Role"</span><span>{summary.role}</span></div>
                        <div class="row-between"><span class="muted">"Active version"</span><span>{summary.active_profile_version.map(|version| format!("v{version}")).unwrap_or_else(|| "none".to_string())}</span></div>
                        <div class="row-between"><span class="muted">"Published versions"</span><span>{summary.published_profile_count}</span></div>
                        <div class="row-between"><span class="muted">"Last activity"</span><span>{summary.last_activity_utc.clone().unwrap_or_else(|| "none".to_string())}</span></div>
                    </div>
                </Panel>
            </div>
        </div>
    }
}

fn adaptation_kind(event_type: &str) -> &'static str {
    match event_type {
        "factory.agent.reflection-requested.v1" => "PROPOSAL",
        "factory.agent.candidate-evaluated.v1" => "EVALUATION",
        "factory.agent.profile-promoted.v1" => "APPLIED",
        "factory.agent.profile-rolled-back.v1" => "ROLLBACK",
        "factory.capability.promoted.v1" => "CAPABILITY APPLIED",
        "factory.capability.rolled-back.v1" => "CAPABILITY ROLLBACK",
        _ => "EVENT",
    }
}

fn adaptation_tone(event_type: &str) -> &'static str {
    match event_type {
        "factory.agent.profile-promoted.v1" | "factory.capability.promoted.v1" => "success",
        "factory.agent.profile-rolled-back.v1" | "factory.capability.rolled-back.v1" => "warn",
        "factory.agent.candidate-evaluated.v1" => "info",
        _ => "neutral",
    }
}
