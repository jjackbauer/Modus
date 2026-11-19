//! Overview: the operator's command centre.
//!
//! The page answers four questions in reading order: what is true now (health strip), what needs a
//! human (one row per work item + action), where the work is (authoritative state counts), what is
//! running and what happened (serving truth and durable activity). Counts are never placed in
//! decorative tiles and no metric appears without the definition that produced it.

use leptos::prelude::*;
use leptos::task::spawn_local;
use leptos_router::hooks::use_params_map;

use control_plane_domain::{
    Attention, EventStreamEnvelope, Overview, ReleaseListItem, short_sha,
};
use control_plane_domain::labels::{
    outcome_label, serving_status_explanation, serving_status_label, serving_status_tone,
    severity_label, severity_tone,
};

use crate::api;
use crate::components::feedback::EmptyState;
use crate::components::layout::{PageHeader, Section};
use crate::components::status::{
    EntityIdentity, FreshnessIndicator, MetadataList, MetadataRow, StatusIndicator,
};
use crate::components::table::{Column, DataTable};
use crate::components::ui::{LoadingBlock, Notice, error_view, time_ago, timestamp};
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
            <PageHeader
                title="Factory overview"
                subtitle="What the factory is doing now, what is waiting for a human, and which evidence supports each claim."
            >
                <button class="btn" on:click=move |_| context.touch()>"Refresh"</button>
                <button
                    class="btn btn-primary"
                    disabled=move || !has_write.get()
                    on:click=dispatch
                >"Start dispatch cycle"</button>
            </PageHeader>

            <Suspense fallback=move || view! { <LoadingBlock label="Loading the factory command centre…"/> }>
                {move || match overview.get().flatten() {
                    None => view! { <LoadingBlock label="Loading the factory command centre…"/> }.into_any(),
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
    let instance = Signal::derive(move || params.get().get("instance").unwrap_or_default());

    let stale = data.stale;
    let errors = data.errors.clone();
    let health = data.health.clone();
    let dispatch_status = data.dispatch.clone();
    let approvals = data.approvals.clone();
    let releases = data.releases.clone();
    let work_counts = data.work_counts.clone();
    let attention = data.attention.clone();
    let recent = data.recent_events.clone();
    let agents = data.agents.clone();
    let meta = data.meta.clone();
    let total = data.total_work_items;
    let failed = data.failed_work_items;
    let generated_at = data.generated_at_utc.clone();
    let event_count = data
        .health
        .as_ref()
        .map(|health| health.event_count)
        .unwrap_or(0);

    let health_state = health.as_ref().map(|health| (health.healthy, health.detail.clone()));
    let last_event = recent
        .iter()
        .max_by_key(|event| event.sequence)
        .map(|event| event.time_utc.to_rfc3339());
    let dispatch_label = match &dispatch_status {
        Some(status) if status.running => ("Running", "active"),
        Some(status) if !status.recent.is_empty() => ("Idle", "neutral"),
        Some(_) => ("Not run in this adapter process", "neutral"),
        None => ("Unavailable", "neutral"),
    };
    let runner = meta
        .as_ref()
        .map(|meta| meta.implementation_runner.clone())
        .unwrap_or_else(|| "unavailable".to_string());
    let runner_for_notice = runner.clone();
    let execution_enabled = meta
        .as_ref()
        .map(|meta| meta.execution_enabled)
        .unwrap_or(false);
    let runner_available = meta
        .as_ref()
        .map(|meta| meta.implementation_runner != "unavailable")
        .unwrap_or(false);
    let dispatch_current = dispatch_status.as_ref().and_then(|status| status.current.clone());
    let dispatch_recent = dispatch_status
        .as_ref()
        .and_then(|status| status.recent.first().cloned());
    let dispatch_available = dispatch_status.is_some();
    let dispatch_error = dispatch_recent.as_ref().and_then(|job| job.error.clone());
    let dispatch_has_recent = dispatch_recent.is_some();
    let agent_sessions: i32 = agents.iter().map(|agent| agent.session_count).sum();
    let agent_attempts: i32 = agents.iter().map(|agent| agent.attempt_count).sum();

    let release_items: Vec<ReleaseListItem> = releases
        .as_ref()
        .map(|list| list.items.clone())
        .unwrap_or_default();
    let release_count_text = serving_count(&release_items);
    let recent_releases: Vec<ReleaseListItem> = release_items.iter().take(3).cloned().collect();
    let approval_summary = approvals
        .as_ref()
        .filter(|inbox| !inbox.items.is_empty())
        .map(|inbox| {
            format!(
                "{} merge · {} contract · {} template policy",
                inbox.merge_count, inbox.contract_count, inbox.template_count
            )
        });

    let instance_for_links = instance;
    view! {
        <div class="stack">
            {stale.then(move || view! {
                <Notice kind="warn">
                    <strong>"Data may be stale. "</strong>
                    <span>"The control plane could not refresh every projection. The last durable facts are still shown, but do not treat them as current."</span>
                    {(!errors.is_empty()).then(move || view! {
                        <details class="evidence">
                            <summary>"Refresh errors"</summary>
                            <pre>{errors.join("\n")}</pre>
                        </details>
                    })}
                </Notice>
            })}

            {health.as_ref().and_then(|health| health.detail.clone()).map(|detail| view! {
                <Notice kind="danger"><strong>"Factory health: "</strong><span>{detail}</span></Notice>
            })}

            {(!runner_available || !execution_enabled).then(move || view! {
                <Notice kind="warn">
                    <strong>"Execution is constrained. "</strong>
                    <span>{format!(
                        "Implementation runner: {runner_for_notice}; execution enabled: {}. Dispatch can still advance deterministic stages, but implementation will fail with a named category.",
                        if execution_enabled { "yes" } else { "no" }
                    )}</span>
                </Notice>
            })}

            <div class="strip" aria-label="Factory state at a glance">
                <span class="strip-item">
                    <span class="strip-key">"Factory"</span>
                    {match health_state {
                        Some((true, _)) => view! { <StatusIndicator label="Healthy".to_string() tone="success"/> }.into_any(),
                        Some((false, _)) => view! { <StatusIndicator label="Needs attention".to_string() tone="danger"/> }.into_any(),
                        None => view! { <StatusIndicator label="Unavailable".to_string() tone="neutral"/> }.into_any(),
                    }}
                </span>
                <span class="strip-item">
                    <span class="strip-key">"Work items"</span>
                    <span class="strip-value">{total}</span>
                </span>
                <span class="strip-item">
                    <span class="strip-key">"Durable events"</span>
                    <span class="strip-value">{event_count}</span>
                </span>
                <span class="strip-item">
                    <span class="strip-key">"Last event"</span>
                    <span class="strip-value" title=timestamp(last_event.as_deref())>
                        {time_ago(last_event.as_deref())}
                    </span>
                </span>
                <span class="strip-item">
                    <span class="strip-key">"Dispatch"</span>
                    <StatusIndicator label=dispatch_label.0.to_string() tone=dispatch_label.1/>
                </span>
                <span class="strip-item">
                    <span class="strip-key">"Checked"</span>
                    <span class="strip-value" title=timestamp(Some(generated_at.as_str()))>
                        {clock_utc(&generated_at)}
                    </span>
                </span>
            </div>

            <div class="split">
                <div class="stack">
                    <Section
                        id="overview-decisions"
                        title="Decisions waiting"
                        count=Some(format!("{} open · one per work item and action", attention.len()))
                        note=Some("Every row is one operator decision. The same pending authorization reported by two projections appears once.".to_string())
                    >
                        {if attention.is_empty() {
                            view! {
                                <EmptyState
                                    title="Nothing requires a human right now"
                                    detail="Pending authorizations, failures and unhealthy supervision appear here with the exact action and its consequence."
                                />
                            }.into_any()
                        } else {
                            view! {
                                <div class="attention-list">
                                    {attention.into_iter().map(|item| view! {
                                        <AttentionRow item=item instance=instance_for_links/>
                                    }).collect_view()}
                                </div>
                            }.into_any()
                        }}
                    </Section>

                    <Section
                        id="overview-stages"
                        title="Where the work is"
                        count=Some(format!("{total} work items"))
                        note=Some("Counts are authoritative factory work states grouped into pipeline stages. The pipeline route explains queueing and derived durations; no forecast is inferred.".to_string())
                        actions=Some(view! {
                            <a class="btn btn-sm" href=format!("/i/{}/pipeline", instance.get())>"Open pipeline"</a>
                        }.into_any())
                    >
                        <div class="stage-strip" role="list" aria-label="Work items by factory stage">
                            {stage_line(work_counts.clone(), failed)}
                        </div>
                    </Section>

                    <Section
                        id="overview-serving"
                        title="Serving now"
                        count=Some(release_count_text)
                        note=Some("Serving state is reconciled from the adapter's tracked process, not from the last journal row. A journal row that says serving but has no supervised process is reported as not currently serving.".to_string())
                    >
                        {if release_items.is_empty() {
                            view! {
                                <EmptyState
                                    title="No release has reached the serve stage"
                                    detail="A release appears here after a reviewed head is merged and post-merge verification is recorded."
                                />
                            }.into_any()
                        } else {
                            let row_count = release_items.len();
                            view! {
                                <DataTable
                                    caption="Serving state per release".to_string()
                                    columns=vec![
                                        Column::text("State"),
                                        Column::text("Release"),
                                        Column::text("What the operator should believe"),
                                    ]
                                    rows=row_count
                                >
                                    {release_items.into_iter().map(|release| view! { <ServingRow release=release instance=instance.get()/> }).collect_view()}
                                </DataTable>
                            }.into_any()
                        }}
                    </Section>

                    <Section
                        id="overview-activity"
                        title="Recent durable activity"
                        count=Some(format!("{} of {} events", recent.len().min(12), event_count))
                        note=Some("Every row is a durable factory event. The event type is technical detail, not a heading.".to_string())
                    >
                        <LiveActivityFeed fallback=recent.clone() instance=instance/>
                    </Section>
                </div>

                <div class="stack">
                    <section class="aside-block" aria-labelledby="overview-pending-title">
                        <h2 class="aside-title" id="overview-pending-title">"Pending authorizations"</h2>
                        {match approval_summary {
                            Some(summary) => view! {
                                <div class="stack">
                                    <p class="small">{summary}</p>
                                    <a class="btn btn-sm" href=format!("/i/{}/quality", instance.get())>
                                        "Open the approvals inbox"
                                    </a>
                                    <p class="bind-hint">
                                        "The decisions themselves are listed once under “Decisions waiting”. The inbox is the audit view of the same governed authorizations."
                                    </p>
                                </div>
                            }.into_any(),
                            None => view! {
                                <EmptyState
                                    title="No pending authorization"
                                    detail="Contract proposals, merge authorizations and template policies appear here when the factory is waiting for a human."
                                />
                            }.into_any(),
                        }}
                    </section>

                    <section class="aside-block" aria-labelledby="overview-dispatch-title">
                        <h2 class="aside-title" id="overview-dispatch-title">"Dispatch"</h2>
                        {if !dispatch_available {
                            view! {
                                <p class="muted small">"Dispatch status is unavailable while the factory is disconnected."</p>
                            }.into_any()
                        } else {
                            view! {
                                <div class="stack">
                                    {dispatch_current.map(|job| view! {
                                        <MetadataList>
                                            <MetadataRow label="Current job">
                                                <code class="mono wrap">{job.job_id.clone()}</code>
                                            </MetadataRow>
                                            <MetadataRow label="State">
                                                <StatusIndicator label=outcome_label(&job.state).to_string() tone="active"/>
                                            </MetadataRow>
                                            <MetadataRow label="Started">
                                                <span title=timestamp(Some(&job.started_at_utc.to_rfc3339()))>
                                                    {time_ago(Some(&job.started_at_utc.to_rfc3339()))}
                                                </span>
                                            </MetadataRow>
                                        </MetadataList>
                                    })}
                                    {dispatch_recent.map(|job| view! {
                                        <MetadataList>
                                            <MetadataRow label="Last job">
                                                <code class="mono wrap">{job.job_id.clone()}</code>
                                            </MetadataRow>
                                            <MetadataRow label="State">
                                                <StatusIndicator
                                                    label=outcome_label(&job.state).to_string()
                                                    tone=if job.state == "completed" { "success" } else { "danger" }
                                                />
                                            </MetadataRow>
                                            <MetadataRow label="Claimed">
                                                <span>{job.claimed}</span>
                                            </MetadataRow>
                                            <MetadataRow label="Dispatched">
                                                <span>{job.dispatched}</span>
                                            </MetadataRow>
                                            <MetadataRow label="Blocked">
                                                <span>{job.blocked}</span>
                                            </MetadataRow>
                                            <MetadataRow label="Failed">
                                                <span>{job.failed}</span>
                                            </MetadataRow>
                                        </MetadataList>
                                    })}
                                    {dispatch_error.map(|error| view! {
                                        <p class="form-error small">{error}</p>
                                    })}
                                    {(!dispatch_has_recent).then(|| view! {
                                        <p class="muted small">"No dispatch cycle has run in this adapter process."</p>
                                    })}
                                    <p class="bind-hint">
                                        "The dispatcher owns execution claims. Opening or refreshing this page does not start agent work."
                                    </p>
                                </div>
                            }.into_any()
                        }}
                    </section>

                    <section class="aside-block" aria-labelledby="overview-capacity-title">
                        <h2 class="aside-title" id="overview-capacity-title">"Execution capacity"</h2>
                        <MetadataList>
                            <MetadataRow label="Runner">
                                <code class="mono wrap">{runner}</code>
                            </MetadataRow>
                            <MetadataRow label="Execution enabled">
                                <span class="strong">{if execution_enabled { "Yes" } else { "No" }}</span>
                            </MetadataRow>
                            <MetadataRow label="Agent sessions">
                                <span class="mono">{agent_sessions}</span>
                            </MetadataRow>
                            <MetadataRow label="Implementation attempts">
                                <span class="mono">{agent_attempts}</span>
                            </MetadataRow>
                        </MetadataList>
                        <p class="bind-hint">
                            "The deterministic demo runner performs real worktree, build and commit work. It is not a model and not independent acceptance. Token usage is not measured for these sessions, so it is not shown as zero."
                        </p>
                    </section>

                    <section class="aside-block" aria-labelledby="overview-releases-title">
                        <h2 class="aside-title" id="overview-releases-title">"Recent releases"</h2>
                        {if recent_releases.is_empty() {
                            view! {
                                <p class="muted small">
                                    "A reviewed candidate becomes a release only after merge and post-merge verification."
                                </p>
                            }.into_any()
                        } else {
                            view! {
                                <div class="stack">
                                    {recent_releases.into_iter().map(|release| view! {
                                        <ReleaseRow release=release instance=instance.get()/>
                                    }).collect_view()}
                                    <a class="btn btn-sm" href=format!("/i/{}/releases", instance.get())>"Open releases"</a>
                                </div>
                            }.into_any()
                        }}
                    </section>

                    <section class="aside-block" aria-labelledby="overview-freshness-title">
                        <h2 class="aside-title" id="overview-freshness-title">"Freshness"</h2>
                        <FreshnessIndicator
                            connected=context.activity.connected
                            stale=context.activity.stale
                            gateway_error=context.activity.gateway_error
                            last_success=Signal::derive({
                                let generated_at = generated_at.clone();
                                move || context.activity.last_heartbeat.get().or_else(|| Some(generated_at.clone()))
                            })
                        />
                        <p class="bind-hint">
                            "This page was projected by the control plane at "
                            <span title=timestamp(Some(generated_at.as_str()))>{clock_utc(&generated_at)}</span>
                            ". A transport disconnect would change only the stream state, never the factory truth."
                        </p>
                    </section>
                </div>
            </div>
        </div>
    }
}

#[component]
fn AttentionRow(item: Attention, instance: Signal<String>) -> impl IntoView {
    let work_item = item.work_item_id.clone().unwrap_or_default();
    let href = if work_item.is_empty() {
        format!("/i/{}/quality", instance.get())
    } else {
        format!("/i/{}/work/{}", instance.get(), work_item)
    };
    let action = attention_action(item.action.as_deref());
    let severity = severity_label(&item.severity).to_string();
    let tone = severity_tone(&item.severity);
    let raw = item.severity.clone();
    view! {
        <article class=format!("attention attention-{}", if item.severity == "critical" { "danger" } else if item.severity == "warning" { "warn" } else { "info" })>
            <div class="attention-bar" aria-hidden="true"></div>
            <div class="attention-body">
                <div class="attention-title">
                    <StatusIndicator label=severity tone=tone raw=raw/>
                    <h3 class="attention-heading">{item.title.clone()}</h3>
                </div>
                <dl class="attention-row"><dt>"What happened"</dt><dd>{item.what_happened.clone()}</dd></dl>
                <dl class="attention-row"><dt>"Why it matters"</dt><dd>{item.why_it_matters.clone()}</dd></dl>
                <dl class="attention-row"><dt>"Safe action"</dt><dd>{item.safe_action.clone()}</dd></dl>
                <dl class="attention-row"><dt>"Consequence"</dt><dd>{item.consequence.clone()}</dd></dl>
                <div class="row wrap">
                    <a class="btn btn-sm" href=href>{action}</a>
                    {(!work_item.is_empty()).then(|| view! {
                        <EntityIdentity label="Work item".to_string() value=work_item.clone()/>
                    })}
                </div>
            </div>
        </article>
    }
}

fn attention_action(action: Option<&str>) -> &'static str {
    match action {
        Some("approve-merge") => "Review and authorize merge",
        Some("approve-contract") => "Review the contract proposal",
        Some("approve-template") => "Review the template policy",
        Some("inspect-events") => "Inspect the event history",
        _ => "Inspect the evidence",
    }
}

#[component]
fn ServingRow(release: ReleaseListItem, instance: String) -> impl IntoView {
    let status = release.serving_status.clone();
    let label = serving_status_label(Some(&status)).to_string();
    let tone = serving_status_tone(Some(&status));
    let explanation = serving_status_explanation(Some(&status)).to_string();
    let href = format!("/i/{}/work/{}", instance, release.work_item_id);
    view! {
        <tr>
            <td><StatusIndicator label=label tone=tone raw=status.clone()/></td>
            <td>
                <a href=href>{release.title.clone()}</a>
                <div class="id-faint mono tiny">{release.work_item_id.clone()}</div>
            </td>
            <td class="small">{explanation}</td>
        </tr>
    }
}

#[component]
fn ReleaseRow(release: ReleaseListItem, instance: String) -> impl IntoView {
    let status = release.serving_status.clone();
    let label = serving_status_label(Some(&status)).to_string();
    let tone = serving_status_tone(Some(&status));
    view! {
        <div class="release-row">
            <div class="row-between">
                <a class="release-title" href=format!("/i/{}/releases/{}", instance, release.work_item_id)>
                    {release.title.clone()}
                </a>
            </div>
            <div class="release-meta">
                <span class="mono">{release.work_item_id.clone()}</span>
                <span>" · "</span>
                <span class="mono">{short_sha(release.merged_sha.as_deref())}</span>
            </div>
            <StatusIndicator label=label tone=tone raw=status.clone()/>
        </div>
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
                    <EmptyState
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
                                        <div class="event-time" title=timestamp(Some(&event.time_utc.to_rfc3339()))>
                                            {time_ago(Some(&event.time_utc.to_rfc3339()))}
                                        </div>
                                        <div class="event-main">
                                            <div class="event-title">
                                                {if work_item.is_empty() {
                                                    view! { <span>{event.summary.clone().unwrap_or_else(|| event.r#type.clone())}</span> }.into_any()
                                                } else {
                                                    view! { <a href=href>{event.summary.clone().unwrap_or_else(|| event.r#type.clone())}</a> }.into_any()
                                                }}
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

fn stage_line(counts: std::collections::BTreeMap<String, i32>, failed: i32) -> impl IntoView {
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
    stages
        .into_iter()
        .map(|(label, states)| {
            let count = if label == "Attention" {
                failed
            } else {
                count_states(&counts, states)
            };
            let attention = label == "Attention" && count > 0;
            view! {
                <div class=if attention { "stage-cell no-dot attention" } else { "stage-cell no-dot" } role="listitem">
                    <span class="stage-cell-label">{label}</span>
                    <span class="stage-cell-count">{count}</span>
                </div>
            }
        })
        .collect_view()
}

fn count_states(counts: &std::collections::BTreeMap<String, i32>, states: &[&str]) -> i32 {
    states
        .iter()
        .map(|state| counts.get(*state).copied().unwrap_or(0))
        .sum()
}

fn serving_count(items: &[ReleaseListItem]) -> String {
    let count = |wanted: &str| {
        items
            .iter()
            .filter(|item| item.serving_status.eq_ignore_ascii_case(wanted))
            .count()
    };
    format!(
        "{} serving · {} starting · {} stopping",
        count("serving"),
        count("starting"),
        count("stopping")
    )
}

/// Exact UTC clock for a strip value; the full timestamp stays available on reveal.
fn clock_utc(value: &str) -> String {
    match chrono::DateTime::parse_from_rfc3339(value) {
        Ok(parsed) => parsed
            .with_timezone(&chrono::Utc)
            .format("%H:%M UTC")
            .to_string(),
        Err(_) => "Not recorded".to_string(),
    }
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
