use leptos::prelude::*;
use leptos::task::spawn_local;
use leptos_router::hooks::use_params_map;
use serde_json::json;

use control_plane_domain::{
    AgentSession, Profile, format_duration_seconds, format_relative, short_digest,
};

use crate::api;
use crate::components::ui::{
    Chip, EmptyBlock, LoadingBlock, MetricCard, Panel, error_view, time_ago,
};
use crate::components::{Timeline, TimelineItem};
use crate::context::AppContext;

#[component]
pub fn AgentDetailPage() -> impl IntoView {
    let context = AppContext::provide();
    let params = use_params_map();
    let instance = Signal::derive(move || params.get().get("instance").unwrap_or_default());
    let definition_id =
        Signal::derive(move || params.get().get("definition_id").unwrap_or_default());
    let refresh = context.refresh;

    let agent = LocalResource::new(move || {
        let instance = instance.get();
        let definition_id = definition_id.get();
        let _ = refresh.get();
        async move {
            if !api::is_client() || instance.is_empty() || definition_id.is_empty() {
                None
            } else {
                Some(api::agent(&instance, &definition_id).await)
            }
        }
    });

    view! {
        <section class="page">
            <div class="row wrap">
                <a class="tiny" href=format!("/i/{}/agents", instance.get())>"← Agents"</a>
            </div>
            <Suspense fallback=move || view! { <LoadingBlock label="Loading role, profiles and session evidence…"/> }>
                {move || match agent.get().flatten() {
                    None => view! { <LoadingBlock label="Loading role, profiles and session evidence…"/> }.into_any(),
                    Some(Err(error)) => view! { {error_view(error)} }.into_any(),
                    Some(Ok(agent)) => view! { <AgentBody agent=agent instance=instance.get()/> }.into_any(),
                }}
            </Suspense>
        </section>
    }
}

#[component]
fn AgentBody(agent: control_plane_domain::AgentDetail, instance: String) -> impl IntoView {
    let context = AppContext::provide();
    let summary = agent.summary;
    let profiles = agent.profiles;
    let sessions = agent.sessions;
    let attempts = agent.attempts;
    let improvements = agent.improvement_events;
    let metrics = agent.metrics;
    let constraints = agent.constraints;
    let active_version = summary.active_profile_version.unwrap_or_default();
    let next_version = profiles
        .iter()
        .map(|profile| profile.version)
        .max()
        .unwrap_or(0)
        + 1;
    let active_profile = profiles.iter().find(|profile| profile.active).cloned();
    let publisher_instance = instance.clone();
    let publisher_definition = summary.definition_id.clone();

    view! {
        <header class="page-header">
            <div>
                <div class="row wrap">
                    <Chip label=summary.definition_id.clone() tone="neutral"/>
                    <Chip label=if summary.active_profile_id.is_some() { "PROFILE ACTIVE".to_string() } else { "NO ACTIVE PROFILE".to_string() } tone=if summary.active_profile_id.is_some() { "success" } else { "warn" }/>
                </div>
                <h1 class="page-title">{summary.role.clone()}</h1>
                <p class="page-subtitle">{summary.objective.clone()}</p>
            </div>
            <div class="page-actions">
                <button class="btn" on:click=move |_| context.touch()>"Refresh"</button>
                <RollbackButton instance=instance.clone() definition_id=summary.definition_id.clone() enabled=summary.active_profile_id.is_some() and=context.is_admin()/>
            </div>
        </header>

        <div class="metric-grid">
            <MetricCard label="Published versions".to_string() value=summary.published_profile_count.to_string() unit=None basis=None tone="" measured=true/>
            <MetricCard label="Sessions".to_string() value=summary.session_count.to_string() unit=None basis=None tone="active" measured=true/>
            <MetricCard label="Attempts".to_string() value=summary.attempt_count.to_string() unit=None basis=None tone="" measured=true/>
            <MetricCard label="Success / failure".to_string() value=format!("{} / {}", summary.success_count, summary.failure_count) unit=None basis=Some("Durable traces and attempts".to_string()) tone="" measured=true/>
            <MetricCard
                label="Input tokens".to_string()
                value=if summary.tokens_measured { summary.input_tokens.to_string() } else { "Not measured".to_string() }
                unit=if summary.tokens_measured { Some("tokens".to_string()) } else { None }
                basis=Some("Provider usage in durable traces".to_string())
                tone=""
                measured=summary.tokens_measured
            />
            <MetricCard
                label="Output tokens".to_string()
                value=if summary.tokens_measured { summary.output_tokens.to_string() } else { "Not measured".to_string() }
                unit=if summary.tokens_measured { Some("tokens".to_string()) } else { None }
                basis=Some("Provider usage in durable traces".to_string())
                tone=""
                measured=summary.tokens_measured
            />
        </div>

        <div class="split">
            <div class="stack">
                <Panel title="Role constraints".to_string()>
                    <div class="stack">
                        <ul class="small muted">{constraints.into_iter().map(|constraint| view! { <li>{constraint}</li> }).collect_view()}</ul>
                        <div class="grid grid-2">
                            <div><div class="label">"Permission ceiling"</div><div class="tiny mono">{summary.permission_ceiling.join(", ")}</div></div>
                            <div><div class="label">"Required capabilities"</div><div class="tiny mono">{summary.required_capabilities.join(", ")}</div></div>
                        </div>
                        <div><div class="label">"Implemented by"</div><div class="tiny muted">{summary.implemented_by.join(" · ")}</div></div>
                    </div>
                </Panel>

                <Panel title="Profile versions".to_string()>
                    {if profiles.is_empty() {
                        view! { <EmptyBlock title="No published profile" detail="The role is declared, but no immutable profile version is published for it. The factory will refuse dispatch that requires a pinned profile."/> }.into_any()
                    } else {
                        view! {
                            <div class="table-wrap">
                                <table class="data">
                                    <thead><tr><th>"Version"</th><th>"Model"</th><th>"Budget"</th><th>"Tools"</th><th>"Digest"</th><th>"Active"</th><th>"Action"</th></tr></thead>
                                    <tbody>
                                        {profiles.iter().cloned().map(|profile| view! { <ProfileRow profile=profile instance=instance.clone() definition_id=summary.definition_id.clone()/> }).collect_view()}
                                    </tbody>
                                </table>
                            </div>
                        }.into_any()
                    }}
                </Panel>

                <Panel title="Session evidence".to_string()>
                    {if sessions.is_empty() {
                        view! { <EmptyBlock title="No session evidence" detail="No durable model session trace exists for this role. That does not imply the role is unhealthy; some roles are deterministic stage handlers."/> }.into_any()
                    } else {
                        view! {
                            <Timeline label="Session evidence">
                                {sessions.into_iter().map(|session| view! { <SessionRow session=session/> }).collect_view()}
                            </Timeline>
                        }.into_any()
                    }}
                </Panel>

                <Panel title="Improvement and adaptation".to_string()>
                    <div class="stack">
                        <p class="small muted">"Proposals and applied changes are distinct. This control plane does not run a second self-improvement engine; it displays the factory's versioned adaptation events and profile history."</p>
                        {if improvements.is_empty() {
                            view! { <EmptyBlock title="No adaptation events recorded" detail="Reflection, candidate evaluation, promotion and rollback events appear here when the factory's adaptation plane records them."/> }.into_any()
                        } else {
                            view! {
                                <Timeline label="Improvement and adaptation events">
                                    {improvements.into_iter().map(|event| view! {
                                        <TimelineItem time=time_ago(Some(&event.time_utc.to_rfc3339()))>
                                            <div class="event-title"><Chip label=event.r#type.clone() tone="info"/></div>
                                            <div class="small muted">{event.summary.clone().unwrap_or_default()}</div>
                                            <details class="evidence"><summary>"Technical data"</summary><pre>{serde_json::to_string_pretty(&event.data).unwrap_or_default()}</pre></details>
                                        </TimelineItem>
                                    }).collect_view()}
                                </Timeline>
                            }.into_any()
                        }}
                    </div>
                </Panel>
            </div>

            <div class="stack">
                <Panel title="Measured performance".to_string()>
                    <div class="stack">
                        {metrics.into_iter().map(|metric| view! {
                            <div class="stack" style="gap:2px">
                                <div class="row-between">
                                    <span class="muted">{metric.label.clone()}</span>
                                    <span class="mono">{metric.value.map(|value| format!("{value:.2} {}", metric.unit)).unwrap_or_else(|| "Not measured".to_string())}</span>
                                </div>
                                <div class="tiny faint">{metric.basis.clone()}</div>
                            </div>
                        }).collect_view()}
                    </div>
                </Panel>

                <Panel title="Attempt history".to_string()>
                    {if attempts.is_empty() {
                        view! { <div class="tiny faint">"No implementation attempts for this role."</div> }.into_any()
                    } else {
                        view! {
                            <div class="stack">
                                {attempts.into_iter().map(|attempt| view! {
                                    <div class="tiny">
                                        <div class="row-between"><span class="mono">{attempt.session_id.clone()}</span><Chip label=attempt.outcome.to_uppercase() tone=if attempt.outcome == "succeeded" { "success" } else { "danger" }/></div>
                                        <div class="faint">{attempt.failure_category.clone().unwrap_or_default()}</div>
                                        <div class="mono faint">{short_digest(attempt.head_sha.as_deref())}</div>
                                    </div>
                                }).collect_view()}
                            </div>
                        }.into_any()
                    }}
                </Panel>

                <ProfilePublisher
                    instance=publisher_instance
                    definition_id=publisher_definition
                    next_version=next_version
                    active_profile=active_profile
                />
            </div>
        </div>

        <div class="tiny faint">
            {format!("Active profile version: {} · last activity {}", active_version, format_relative(summary.last_activity_utc.as_deref(), chrono::Utc::now()))}
        </div>
    }
}

#[component]
fn ProfileRow(profile: Profile, instance: String, definition_id: String) -> impl IntoView {
    let context = AppContext::provide();
    let confirming = RwSignal::new(false);
    let busy = RwSignal::new(false);
    let version = profile.version;
    let instance_value = instance.clone();
    let definition_value = definition_id.clone();

    let promote = move |_| {
        let csrf = context.csrf.get();
        let instance = instance_value.clone();
        let definition_id = definition_value.clone();
        busy.set(true);
        spawn_local(async move {
            let result =
                api::promote_profile(&instance, &csrf, &definition_id, version, true).await;
            busy.set(false);
            confirming.set(false);
            match result {
                Ok(_) => {
                    context.notify_success("Active pointer moved for future sessions. Running sessions keep their pinned version.");
                    context.touch();
                }
                Err(error) => context.notify_error(error),
            }
        });
    };

    view! {
        <tr>
            <td class="mono">{format!("v{}", profile.version)}</td>
            <td class="tiny">{profile.model_provider.clone()}<div class="faint">{profile.preferred_model.clone()}</div></td>
            <td class="tiny">{profile.max_steps}" steps · "{profile.max_total_tokens}" tokens · "{format_duration_seconds(profile.timeout_seconds as f64)}</td>
            <td class="tiny mono">{profile.tools.join(", ")}</td>
            <td class="tiny mono" title=profile.content_digest.clone()>{short_digest(Some(&profile.content_digest))}</td>
            <td>{if profile.active { view! { <Chip label="ACTIVE".to_string() tone="success"/> }.into_any() } else { view! { <span class="faint">"—"</span> }.into_any() }}</td>
            <td>
                {if profile.active {
                    view! { <span class="tiny faint">"Pinned for new sessions"</span> }.into_any()
                } else if confirming.get() {
                    view! {
                        <div class="row">
                            <button class="btn btn-sm btn-primary" disabled=move || busy.get() on:click=promote.clone()>"Confirm promote"</button>
                            <button class="btn btn-sm btn-quiet" on:click=move |_| confirming.set(false)>"Cancel"</button>
                        </div>
                    }.into_any()
                } else {
                    view! {
                        <button class="btn btn-sm" disabled=move || !context.is_admin() on:click=move |_| confirming.set(true)>"Promote"</button>
                    }.into_any()
                }}
            </td>
        </tr>
    }
}

#[component]
fn RollbackButton(
    instance: String,
    definition_id: String,
    enabled: bool,
    and: bool,
) -> impl IntoView {
    let context = AppContext::provide();
    let busy = RwSignal::new(false);
    view! {
        <button class="btn btn-sm" disabled=move || !enabled || !and || busy.get() on:click=move |_| {
            let csrf = context.csrf.get();
            let instance = instance.clone();
            let definition_id = definition_id.clone();
            busy.set(true);
            spawn_local(async move {
                let result = api::rollback_profile(&instance, &csrf, &definition_id).await;
                busy.set(false);
                match result {
                    Ok(_) => {
                        context.notify_success("Active pointer restored to the previous published version.");
                        context.touch();
                    }
                    Err(error) => context.notify_error(error),
                }
            });
        }>"Roll back active profile"</button>
    }
}

#[component]
fn SessionRow(session: AgentSession) -> impl IntoView {
    view! {
        <TimelineItem time=time_ago(Some(&session.recorded_at_utc.to_rfc3339()))>
                <div class="event-title">
                    <strong class="mono">{session.session_id.clone()}</strong>
                    <Chip label=session.outcome.to_uppercase() tone=if session.outcome == "completed" { "success" } else { "danger" }/>
                    <span class="tiny faint">{session.model_id.clone()}</span>
                </div>
                <div class="tiny faint">
                    {if session.tokens_measured { format!("{} in / {} out tokens", session.input_tokens, session.output_tokens) } else { "Token usage not measured".to_string() }}
                    " · profile v"{session.profile_version}
                </div>
                <div class="small muted">{session.summary.clone()}</div>
                {(!session.tool_invocations.is_empty()).then(|| view! {
                    <details class="evidence">
                        <summary>{format!("{} tool invocation(s)", session.tool_invocations.len())}</summary>
                        <pre>{serde_json::to_string_pretty(&session.tool_invocations).unwrap_or_default()}</pre>
                    </details>
                })}
        </TimelineItem>
    }
}

#[component]
fn ProfilePublisher(
    instance: String,
    definition_id: String,
    next_version: i32,
    active_profile: Option<Profile>,
) -> impl IntoView {
    let context = AppContext::provide();
    let version = RwSignal::new(next_version);
    let reason = RwSignal::new(String::new());
    let model = RwSignal::new(
        active_profile
            .as_ref()
            .map(|profile| profile.preferred_model.clone())
            .unwrap_or_else(|| "deepseek-chat".to_string()),
    );
    let max_steps = RwSignal::new(
        active_profile
            .as_ref()
            .map(|profile| profile.max_steps)
            .unwrap_or(24),
    );
    let max_tokens = RwSignal::new(
        active_profile
            .as_ref()
            .map(|profile| profile.max_total_tokens)
            .unwrap_or(140000),
    );
    let timeout = RwSignal::new(
        active_profile
            .as_ref()
            .map(|profile| profile.timeout_seconds)
            .unwrap_or(2400),
    );
    let tools = RwSignal::new(
        active_profile
            .as_ref()
            .map(|profile| profile.tools.join(","))
            .unwrap_or_default(),
    );
    let permissions = RwSignal::new(
        active_profile
            .as_ref()
            .map(|profile| profile.requested_permissions.join(","))
            .unwrap_or_default(),
    );
    let busy = RwSignal::new(false);
    let error = RwSignal::new(None::<api::UiError>);

    view! {
        <Panel title="Publish a profile version".to_string()>
            <div class="stack">
                <p class="tiny faint">"Publishing is immutable and auditable. The definition ceiling is enforced by the factory registry; a profile cannot widen its tool permissions through this form."</p>
                <div class="form-grid">
                    <label class="field">"Version"
                        <input class="input" type="number" min="1" prop:value=move || version.get().to_string() on:input=move |event| version.set(event_target_value(&event).parse().unwrap_or(1))/>
                    </label>
                    <label class="field">"Preferred model"
                        <input class="input" prop:value=move || model.get() on:input=move |event| model.set(event_target_value(&event))/>
                    </label>
                    <label class="field span-2">"Change reason (required)"
                        <input class="input" placeholder="Why is a new immutable version needed?" prop:value=move || reason.get() on:input=move |event| reason.set(event_target_value(&event))/>
                    </label>
                    <label class="field">"Max steps"
                        <input class="input" type="number" prop:value=move || max_steps.get().to_string() on:input=move |event| max_steps.set(event_target_value(&event).parse().unwrap_or(24))/>
                    </label>
                    <label class="field">"Max tokens"
                        <input class="input" type="number" prop:value=move || max_tokens.get().to_string() on:input=move |event| max_tokens.set(event_target_value(&event).parse().unwrap_or(140000))/>
                    </label>
                    <label class="field">"Timeout seconds"
                        <input class="input" type="number" prop:value=move || timeout.get().to_string() on:input=move |event| timeout.set(event_target_value(&event).parse().unwrap_or(2400))/>
                    </label>
                    <label class="field">"Tools (capability@version)"
                        <input class="input mono" prop:value=move || tools.get() on:input=move |event| tools.set(event_target_value(&event))/>
                    </label>
                    <label class="field span-2">"Requested permissions"
                        <input class="input mono" prop:value=move || permissions.get() on:input=move |event| permissions.set(event_target_value(&event))/>
                    </label>
                </div>
                {move || error.get().map(|error| view! { <div class="form-error">{error.message}</div> })}
                <div class="row">
                    <button class="btn btn-primary" disabled=move || !context.is_admin() || busy.get() on:click=move |_| {
                        let csrf = context.csrf.get();
                        let instance = instance.clone();
                        let definition_id = definition_id.clone();
                        let reason_value = reason.get();
                        let tools_value: Vec<String> = tools.get().split(',').map(str::trim).filter(|value| !value.is_empty()).map(str::to_string).collect();
                        let permissions_value: Vec<String> = permissions.get().split(',').map(str::trim).filter(|value| !value.is_empty()).map(str::to_string).collect();
                        let body = json!({
                            "version": version.get(),
                            "changeReason": reason_value,
                            "preferredModel": model.get(),
                            "maxSteps": max_steps.get(),
                            "timeoutSeconds": timeout.get(),
                            "maxTotalTokens": max_tokens.get(),
                            "maxConsecutiveToolFailures": 3,
                            "tools": tools_value,
                            "requestedPermissions": permissions_value
                        });
                        busy.set(true);
                        error.set(None);
                        spawn_local(async move {
                            let result = api::create_profile(&instance, &csrf, &definition_id, &body).await;
                            busy.set(false);
                            match result {
                                Ok(profile) => {
                                    context.notify_success(format!("Published immutable profile version {}.", profile.version));
                                    context.touch();
                                }
                                Err(issue) => error.set(Some(issue)),
                            }
                        });
                    }>{move || if busy.get() { "Publishing…" } else { "Publish immutable version" }}</button>
                    <span class="tiny faint">"Promotion is separate from publication."</span>
                </div>
            </div>
        </Panel>
    }
}
