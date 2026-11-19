use leptos::prelude::*;
use leptos::task::spawn_local;
use leptos_router::hooks::use_params_map;

use control_plane_domain::{Attention, EventStreamEnvelope, Overview, short_digest};

use crate::api;
use crate::components::ui::{
    Chip, EmptyBlock, LoadingBlock, MetricCard, Notice, Panel, PanelActions, error_view, time_ago,
};
use crate::context::AppContext;

#[component]
pub fn OverviewPage() -> impl IntoView {
    let context = AppContext::provide();
    let params = use_params_map();
    let instance = move || params.get().get("instance").unwrap_or_default();
    let refresh = context.refresh;
    let has_write = Signal::derive(move || context.can_write());

    let overview = LocalResource::new(move || {
        let instance = instance();
        let _ = refresh.get();
        async move {
            if !api::is_client() || instance.is_empty() {
                None
            } else {
                Some(api::overview(&instance).await)
            }
        }
    });

    let dispatch = move |_| {
        let instance = instance();
        let csrf = context.csrf.get();
        spawn_local(async move {
            match api::dispatch(&instance, &csrf, 16).await {
                Ok(result) => {
                    context.notify_success(format!(
                        "Dispatch cycle accepted: {}",
                        result.job.map(|job| job.job_id).unwrap_or_default()
                    ));
                    context.touch();
                }
                Err(error) => context.notify_error(error),
            }
        });
    };

    view! {
        <section class="page">
            <header class="page-header">
                <div>
                    <h1 class="page-title">Factory command center</h1>
                    <p class="page-subtitle">
                        "What the factory is doing now, what is waiting for a human, and which evidence is behind each claim."
                    </p>
                </div>
                <div class="page-actions">
                    <button class="btn" on:click=move |_| context.touch()>"Refresh"</button>
                    <button
                        class="btn btn-primary"
                        disabled=move || !has_write.get()
                        on:click=dispatch
                    >"Start dispatch cycle"</button>
                </div>
            </header>

            <Suspense fallback=move || view! { <LoadingBlock label="Loading the factory command center…"/> }>
                {move || match overview.get().flatten() {
                    None => view! { <LoadingBlock label="Loading the factory command center…"/> }.into_any(),
                    Some(Err(error)) => view! { {error_view(error)} }.into_any(),
                    Some(Ok(data)) => view! { <OverviewBody data=data/> }.into_any(),
                }}
            </Suspense>
        </section>
    }
}

#[component]
fn OverviewBody(data: Overview) -> impl IntoView {
    let context = AppContext::provide();
    let params = use_params_map();
    let instance_signal = Signal::derive(move || params.get().get("instance").unwrap_or_default());
    let health = data.health.clone();
    let stale = data.stale;
    let errors = data.errors.clone();
    let approvals = data.approvals.clone();
    let dispatch = data.dispatch.clone();
    let work_counts = data.work_counts.clone();
    let agents = data.agents.clone();
    let tokens = data.tokens.clone();
    let attention = data.attention.clone();
    let recent = data.recent_events.clone();

    let meta = data.meta.clone();
    let releases = data.releases.clone();

    let in_progress = count_states(
        &work_counts,
        &[
            "InImplementation",
            "InReview",
            "DeliveryValidation",
            "ReleaseCandidate",
        ],
    );
    let waiting = approvals
        .as_ref()
        .map(|inbox| inbox.items.len() as i32)
        .unwrap_or(0);
    let blocked = data.failed_work_items;
    let released = count_states(&work_counts, &["Released", "OperationallyValidated"]);
    let total = data.total_work_items;

    let runner_available = meta
        .as_ref()
        .map(|meta| meta.implementation_runner != "unavailable")
        .unwrap_or(false);
    let runner_name = meta
        .as_ref()
        .map(|meta| meta.implementation_runner.clone())
        .unwrap_or_else(|| "unknown".to_string());
    let runner_name_notice = runner_name.clone();
    let execution_enabled = meta
        .as_ref()
        .map(|meta| meta.execution_enabled)
        .unwrap_or(false);

    view! {
        <div class="stack">
            {stale.then(move || view! {
                <Notice kind="warn">
                    <strong>"Data may be stale. "</strong>
                    <span>"The control plane could not refresh every projection. The last durable facts are still shown, but do not treat them as current."</span>
                    {(!errors.is_empty()).then(move || view! { <details class="evidence"><summary>"Refresh errors"</summary><pre>{errors.join("\n")}</pre></details> })}
                </Notice>
            })}

            {health.as_ref().and_then(|health| health.detail.clone()).map(|detail| view! {
                <Notice kind="danger"><strong>"Factory health: "</strong><span>{detail}</span></Notice>
            })}

            {(!runner_available || !execution_enabled).then(move || view! {
                <Notice kind="warn">
                    <strong>"Execution is constrained. "</strong>
                    <span>{format!(
                        "Implementation runner: {runner_name_notice}; execution enabled: {}. Dispatch can still advance deterministic stages, but implementation will fail with a named category.",
                        if execution_enabled { "yes" } else { "no" }
                    )}</span>
                </Notice>
            })}

            <div class="metric-grid">
                <MetricCard label="Work in flight".to_string() value=in_progress.to_string() unit=None basis=None tone="active" measured=true/>
                <MetricCard label="Needs authorization".to_string() value=waiting.to_string() unit=None basis=None tone="waiting" measured=true/>
                <MetricCard label="Blocked or failed".to_string() value=blocked.to_string() unit=None basis=None tone="danger" measured=true/>
                <MetricCard label="Released".to_string() value=released.to_string() unit=None basis=None tone="success" measured=true/>
                <MetricCard
                    label="Token consumption".to_string()
                    value=if tokens.measured { format!("{}", tokens.input_tokens + tokens.output_tokens) } else { "Not measured".to_string() }
                    unit=if tokens.measured { Some("tokens".to_string()) } else { None }
                    basis=Some(tokens.basis.clone())
                    tone=""
                    measured=tokens.measured
                />
                <MetricCard label="Total work items".to_string() value=total.to_string() unit=None basis=None tone="" measured=true/>
            </div>

            <div class="split">
                <div class="stack">
                    <section class="attention-section">
                        <div class="section-head">
                            <h2 class="section-title">"Needs attention"</h2>
                            <span class="tiny faint">{format!("{} item(s)", attention.len())}</span>
                        </div>
                        {if attention.is_empty() {
                            view! { <EmptyBlock title="Nothing requires a human right now" detail="Approvals, failures and unhealthy supervision will appear here with the exact action and consequence."/> }.into_any()
                        } else {
                            view! { <div class="attention-list">{attention.into_iter().map(|item| view! { <AttentionCard item=item/> }).collect_view()}</div> }.into_any()
                        }}
                    </section>

                    <PanelActions title="Factory pipeline".to_string() actions=view! {
                        <a class="btn btn-sm" href=format!("/i/{}/pipeline", instance_signal.get())>"Open pipeline"</a>
                    }.into_any()>
                        <div class="stage-strip">
                            {stage_strip(work_counts.clone(), blocked)}
                        </div>
                        <div class="tiny faint" style="margin-top:12px">
                            "Counts are authoritative factory work states. The pipeline view explains queueing and stage metrics."
                        </div>
                    </PanelActions>

                    <PanelActions title="Live activity".to_string() actions=view! {
                        <ConnectionChip/>
                    }.into_any()>
                        <LiveActivityFeed fallback=recent.clone() instance=instance_signal/>
                    </PanelActions>
                </div>

                <div class="stack">
                    <Panel title="Pending authorizations".to_string()>
                        {match approvals {
                            Some(inbox) if !inbox.items.is_empty() => view! {
                                <div class="stack">
                                    {inbox.items.into_iter().take(5).map(|item| {
                                        let href = format!("/i/{}/quality", instance_signal.get());
                                        view! {
                                            <a class="approval-row" href=href>
                                                <div class="row-between">
                                                    <Chip label=item.kind.to_uppercase() tone="waiting"/>
                                                    <span class="tiny faint">{time_ago(item.created_at_utc.as_deref())}</span>
                                                </div>
                                                <strong>{item.title}</strong>
                                                <div class="tiny faint mono">{short_digest(Some(&item.digest))}</div>
                                            </a>
                                        }
                                    }).collect_view()}
                                </div>
                            }.into_any(),
                            _ => view! { <EmptyBlock title="No pending approvals" detail="Contract proposals, merge authorizations and template policies will appear here."/> }.into_any(),
                        }}
                    </Panel>

                    <Panel title="Dispatch".to_string()>
                        {match dispatch {
                            Some(status) => view! {
                                <div class="stack">
                                    {status.current.map(|job| view! {
                                        <div class="stack">
                                            <div class="row-between"><span class="mono">{job.job_id}</span><span class="chip chip-active">"CLAIMING"</span></div>
                                            <div class="tiny faint">"Started "{time_ago(Some(&job.started_at_utc.to_rfc3339()))}</div>
                                        </div>
                                    })}
                                    {status.recent.first().map(|job| view! {
                                        <div class="stack">
                                            <div class="row-between">
                                                <span class="mono tiny">{job.job_id.clone()}</span>
                                                <Chip label=job.state.to_uppercase() tone=if job.state == "completed" { "success" } else { "danger" }/>
                                            </div>
                                            <div class="tiny faint">
                                                {format!("claimed {} · dispatched {} · blocked {} · failed {}", job.claimed, job.dispatched, job.blocked, job.failed)}
                                            </div>
                                            {job.error.clone().map(|error| view! { <div class="form-error">{error}</div> })}
                                        </div>
                                    })}
                                    {status.recent.is_empty().then(|| view! { <div class="tiny faint">"No dispatch cycle has run yet in this adapter process."</div> })}
                                </div>
                            }.into_any(),
                            None => view! { <div class="tiny faint">"Dispatch status unavailable while the factory is disconnected."</div> }.into_any(),
                        }}
                    </Panel>

                    <Panel title="Execution capacity".to_string()>
                        <div class="stack">
                            <div class="row-between"><span class="muted">"Implementation runner"</span><span class="mono tiny">{runner_name}</span></div>
                            <div class="row-between"><span class="muted">"Execution enabled"</span><span>{if execution_enabled { "Yes" } else { "No" }}</span></div>
                            <div class="row-between"><span class="muted">"Agent sessions recorded"</span><span class="mono">{agents.iter().map(|agent| agent.session_count).sum::<i32>()}</span></div>
                            <div class="row-between"><span class="muted">"Implementation attempts"</span><span class="mono">{agents.iter().map(|agent| agent.attempt_count).sum::<i32>()}</span></div>
                            <div class="tiny faint">
                                "The factory dispatcher owns execution claims. Opening this page does not start agent work."
                            </div>
                        </div>
                    </Panel>

                    <Panel title="Recent releases".to_string()>
                        {match releases {
                            Some(list) if !list.items.is_empty() => view! {
                                <div class="stack">
                                    {list.items.into_iter().take(4).map(|release| view! {
                                        <div class="release-row">
                                            <div class="row-between">
                                                <a href=format!("/i/{}/releases/{}", instance_signal.get(), release.work_item_id)>{release.title}</a>
                                                <Chip label=release.serving_status.to_uppercase() tone=if release.verified { "success" } else { "waiting" }/>
                                            </div>
                                            <div class="tiny faint mono">{short_digest(release.merged_sha.as_deref())}</div>
                                        </div>
                                    }).collect_view()}
                                </div>
                            }.into_any(),
                            _ => view! { <EmptyBlock title="No releases yet" detail="A reviewed candidate becomes a release only after merge and post-merge verification."/> }.into_any(),
                        }}
                    </Panel>
                </div>
            </div>
        </div>
    }
}

#[component]
fn AttentionCard(item: Attention) -> impl IntoView {
    let context = AppContext::provide();
    let section = context
        .session
        .get()
        .and_then(|session| {
            session
                .instances
                .first()
                .map(|instance| instance.id.clone())
        })
        .unwrap_or_default();
    let work_item = item.work_item_id.clone().unwrap_or_default();
    let href = if work_item.is_empty() {
        format!("/i/{section}/quality")
    } else {
        format!("/i/{section}/work/{work_item}")
    };
    let tone = match item.severity.as_str() {
        "critical" => "attention-danger",
        "warning" => "attention-warn",
        _ => "attention-info",
    };
    view! {
        <article class=format!("attention {tone}")>
            <div class="attention-bar"></div>
            <div class="attention-body">
                <div class="attention-title">
                    <Chip label=item.severity.to_uppercase() tone=match item.severity.as_str() { "critical" => "danger", "warning" => "warn", _ => "info" }/>
                    <span>{item.title}</span>
                </div>
                <dl class="attention-row"><dt>"What happened"</dt><dd>{item.what_happened}</dd></dl>
                <dl class="attention-row"><dt>"Why it matters"</dt><dd>{item.why_it_matters}</dd></dl>
                <dl class="attention-row"><dt>"Safe action"</dt><dd>{item.safe_action}</dd></dl>
                <dl class="attention-row"><dt>"Consequence"</dt><dd>{item.consequence}</dd></dl>
                <div class="row">
                    <a class="btn btn-sm" href=href>"Inspect"</a>
                    {item.action.map(|action| view! { <span class="tiny faint mono">{action}</span> })}
                </div>
            </div>
        </article>
    }
}

#[component]
fn LiveActivityFeed(fallback: Vec<EventStreamEnvelope>, instance: Signal<String>) -> impl IntoView {
    let context = AppContext::provide();
    view! {
        {move || {
            let feed = live_feed(context, &fallback);
            if feed.is_empty() {
                view! {
                    <EmptyBlock
                        title="No durable activity yet"
                        detail="Submit a requirement and start a dispatch cycle; every state transition will appear here from the factory event log."
                    />
                }
                    .into_any()
            } else {
                view! {
                    <div class="timeline compact">
                        {feed
                            .into_iter()
                            .map(|event| {
                                let work_item = event.work_item_id.clone().unwrap_or_default();
                                let href = format!("/i/{}/work/{work_item}", instance.get());
                                view! {
                                    <div class="event">
                                        <div class="event-time">{time_ago(Some(&event.time_utc.to_rfc3339()))}</div>
                                        <div class="event-main">
                                            <div class="event-title">
                                                <a href=href>{event.summary.clone().unwrap_or_else(|| event.r#type.clone())}</a>
                                            </div>
                                            <div class="event-type">{event.r#type.clone()}</div>
                                        </div>
                                    </div>
                                }
                            })
                            .collect_view()}
                    </div>
                }
                    .into_any()
            }
        }}
    }
}

#[component]
fn ConnectionChip() -> impl IntoView {
    let context = AppContext::provide();
    let connected = context.activity.connected;
    let stale = context.activity.stale;
    let label = move || {
        if connected.get() && !stale.get() {
            "LIVE"
        } else if stale.get() {
            "STALE"
        } else {
            "RECONNECTING"
        }
    };
    view! {
        <span class=move || format!("chip chip-{}", if connected.get() && !stale.get() { "success" } else { "warn" })>{label}</span>
    }
}

fn count_states(counts: &std::collections::BTreeMap<String, i32>, states: &[&str]) -> i32 {
    states
        .iter()
        .map(|state| counts.get(*state).copied().unwrap_or(0))
        .sum()
}

fn stage_strip(counts: std::collections::BTreeMap<String, i32>, failed: i32) -> impl IntoView {
    let stages = [
        (
            "Intake",
            [
                "Proposed",
                "RequirementsAligned",
                "ArchitectureRefined",
                "ReadyForImplementation",
            ]
            .as_slice(),
        ),
        ("Implementation", &["InImplementation", "NeedsRework"]),
        ("Review", &["InReview"]),
        ("Delivery", &["ReadyForDelivery", "DeliveryValidation"]),
        ("Release", &["ReleaseCandidate"]),
        ("Released", &["Released", "OperationallyValidated"]),
        ("Attention", &["Blocked", "Failed"]),
    ];
    let rows = stages
        .into_iter()
        .map(|(label, states)| {
            let count = if label == "Attention" {
                failed
            } else {
                count_states(&counts, states)
            };
            let tone = match label {
                "Attention" => "danger",
                "Released" => "success",
                "Implementation" | "Review" => "active",
                "Delivery" | "Release" => "waiting",
                _ => "neutral",
            };
            view! {
                <div class="stage-cell">
                    <div class=format!("dot dot-{tone}")></div>
                    <div class="stage-cell-label">{label}</div>
                    <div class="stage-cell-count">{count}</div>
                </div>
            }
        })
        .collect_view();
    rows
}

fn live_feed(context: AppContext, fallback: &[EventStreamEnvelope]) -> Vec<EventStreamEnvelope> {
    let mut events = context.activity.events.get();
    if events.is_empty() {
        events = fallback.to_vec();
    } else if events.len() < 8 {
        for event in fallback {
            if !events
                .iter()
                .any(|existing| existing.sequence == event.sequence)
            {
                events.push(event.clone());
            }
        }
    }
    events.sort_by_key(|event| std::cmp::Reverse(event.sequence));
    events.truncate(12);
    events
}
