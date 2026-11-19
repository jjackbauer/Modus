use leptos::prelude::*;
use leptos::task::spawn_local;
use leptos_router::hooks::use_params_map;

use control_plane_domain::{
    AgentSession, Attempt, Attention, Contract, Diff, EventStreamEnvelope, FactoryEvent, Lineage,
    Metric, NextAction, Planning, Release, ReviewExecution, ReviewHistory, Stage, WorkItemDetail,
    format_duration_seconds, indicator_label, indicator_tone, render_markdown, review_kind_label,
    short_digest, short_sha, stage_tone, state_label, state_tone,
};

use crate::api;
use crate::components::ui::{
    Chip, DigestLine, EmptyBlock, InfoRow, LoadingBlock, MetricCard, Notice, Panel, PanelActions,
    error_view, time_ago, timestamp,
};
use crate::context::AppContext;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Tab {
    Lifecycle,
    Agent,
    Evidence,
    Source,
    Lineage,
    Events,
}

#[derive(Debug, Clone)]
enum Dialog {
    ApproveContract { digest: String },
    RejectContract { digest: String },
    ApproveMerge { digest: String },
    Serve { duration: i32 },
}

#[component]
pub fn WorkDetailPage() -> impl IntoView {
    let context = AppContext::provide();
    let params = use_params_map();
    let instance = Signal::derive(move || params.get().get("instance").unwrap_or_default());
    let work_item_id = Signal::derive(move || params.get().get("work_item_id").unwrap_or_default());
    let tab = RwSignal::new(Tab::Lifecycle);
    let dialog = RwSignal::new(None::<Dialog>);
    let refresh = context.refresh;

    let detail = LocalResource::new(move || {
        let instance = instance.get();
        let work_item_id = work_item_id.get();
        let _ = refresh.get();
        async move {
            if !api::is_client() || instance.is_empty() || work_item_id.is_empty() {
                None
            } else {
                Some(api::work_item(&instance, &work_item_id).await)
            }
        }
    });

    view! {
        <Suspense fallback=move || view! { <LoadingBlock label="Loading the work item from durable factory state…"/> }>
            {move || match detail.get().flatten() {
                None => view! { <LoadingBlock label="Loading the work item from durable factory state…"/> }.into_any(),
                Some(Err(error)) => view! { {error_view(error)} }.into_any(),
                Some(Ok(data)) => view! {
                    <WorkDetailBody
                        data=data
                        instance=instance
                        tab=tab
                        dialog=dialog
                    />
                }.into_any(),
            }}
        </Suspense>
        <ActionDialog dialog=dialog instance=instance work_item_id=work_item_id/>
    }
}

#[component]
fn WorkDetailBody(
    data: WorkItemDetail,
    instance: Signal<String>,
    tab: RwSignal<Tab>,
    dialog: RwSignal<Option<Dialog>>,
) -> impl IntoView {
    let context = AppContext::provide();
    let summary = data.summary.clone();
    let state = summary.state.clone();
    let indicator = summary.derived_indicator.clone();
    let title = summary.title.clone();
    let work_item_id = summary.work_item_id.clone();
    let version = summary.version;
    let health = data
        .execution_health
        .clone()
        .unwrap_or_else(|| "unknown".to_string());
    let attention = data.attention.clone();
    let next_actions = data.next_actions.clone();
    let contract = data.contract.clone();
    let release = data.release.clone();
    let serving = data.serving.clone();
    let reviews = data.reviews.clone();
    let sessions_active = data.dispatch_active && state == "InImplementation";

    let instance_value = instance.get();
    let detail_url = format!("/i/{instance_value}/work/{work_item_id}");

    let dispatch = {
        let instance = instance;
        let context = context;
        move |_| {
            let instance = instance.get();
            let csrf = context.csrf.get();
            spawn_local(async move {
                match api::dispatch(&instance, &csrf, 16).await {
                    Ok(_) => {
                        context.notify_success(
                            "Dispatch cycle accepted. The factory owns the run from here.",
                        );
                        context.touch();
                    }
                    Err(error) => context.notify_error(error),
                }
            });
        }
    };

    let stop_serve = Callback::new({
        let instance = instance;
        let context = context;
        let work_item_id = work_item_id.clone();
        move |_| {
            let instance = instance.get();
            let csrf = context.csrf.get();
            let work_item_id = work_item_id.clone();
            spawn_local(async move {
                match api::stop_serve(&instance, &csrf, &work_item_id).await {
                    Ok(_) => {
                        context.notify_success("Serve cancellation requested.");
                        context.touch();
                    }
                    Err(error) => context.notify_error(error),
                }
            });
        }
    });

    let open_contract = Callback::new({
        let dialog = dialog;
        let digest = contract.proposal_digest.clone().unwrap_or_default();
        move |_| {
            dialog.set(Some(Dialog::ApproveContract {
                digest: digest.clone(),
            }))
        }
    });
    let open_reject = Callback::new({
        let dialog = dialog;
        let digest = contract.proposal_digest.clone().unwrap_or_default();
        move |_| {
            dialog.set(Some(Dialog::RejectContract {
                digest: digest.clone(),
            }))
        }
    });
    let open_merge = Callback::new({
        let dialog = dialog;
        let digest = reviews
            .iter()
            .find(|review| review.outcome == "accepted")
            .and_then(|review| review.review_digest.clone())
            .unwrap_or_default();
        move |_| {
            dialog.set(Some(Dialog::ApproveMerge {
                digest: digest.clone(),
            }))
        }
    });
    let open_serve = Callback::new(move |_| dialog.set(Some(Dialog::Serve { duration: 60 })));

    let has_approve_contract = contract.awaiting_approval;
    let latest_accepted_review = reviews
        .iter()
        .rev()
        .find(|review| review.outcome == "accepted")
        .cloned();
    let can_merge = state == "ReadyForDelivery" && latest_accepted_review.is_some();
    let can_serve = matches!(state.as_str(), "Released" | "OperationallyValidated");
    let can_dispatch = matches!(
        state.as_str(),
        "Proposed"
            | "RequirementsAligned"
            | "ArchitectureRefined"
            | "ReadyForImplementation"
            | "InImplementation"
            | "InReview"
            | "ReadyForDelivery"
            | "DeliveryValidation"
            | "ReleaseCandidate"
            | "NeedsRework"
    );
    let is_terminal = matches!(
        state.as_str(),
        "Failed" | "Blocked" | "Cancelled" | "Closed"
    );
    let indicator_text = indicator_label(indicator.as_deref()).to_string();
    let indicator_tone_value = indicator_tone(indicator.as_deref());
    let show_indicator = indicator.is_some() && indicator_text != state_label(&state);

    view! {
        <section class="page">
            <div class="work-header">
                <div class="work-title-block">
                    <div class="row wrap">
                        <a class="tiny" href=format!("/i/{instance_value}/work")>"← Work queue"</a>
                        <span class="tiny faint mono">{work_item_id.clone()}</span>
                        <span class="tiny faint">"v"{version}</span>
                    </div>
                    <h1 class="page-title">{title}</h1>
                    <div class="row wrap">
                        <Chip label=state_label(&state).to_string() tone=state_tone(&state)/>
                        {show_indicator.then(|| view! { <Chip label=indicator_text.clone() tone=indicator_tone_value/> })}
                        <Chip label=health_label(&health).to_string() tone=health_tone(&health)/>
                        {data.dispatch_active.then(|| view! { <Chip label="DISPATCH ACTIVE".to_string() tone="active"/> })}
                        {contract.is_frozen.then(|| view! { <Chip label="CONTRACT FROZEN".to_string() tone="success"/> })}
                    </div>
                </div>
                <div class="work-actions">
                    {can_dispatch.then(|| view! {
                        <button class="btn btn-primary" disabled=move || !context.can_write() || data.dispatch_active on:click=dispatch.clone()>"Start dispatch"</button>
                    })}
                    {can_merge.then(|| view! {
                        <button class="btn" disabled=move || !context.can_write() on:click=move |event| open_merge.run(event)>"Authorize merge"</button>
                    })}
                    {has_approve_contract.then(|| view! {
                        <button class="btn" disabled=move || !context.can_write() on:click=move |event| open_contract.run(event)>"Approve contract"</button>
                    })}
                    {can_serve.then(|| {
                        if serving.active {
                            view! { <button class="btn btn-danger" disabled=move || !context.can_write() on:click=move |event| stop_serve.run(event)>"Stop serve"</button> }.into_any()
                        } else {
                            view! { <button class="btn" disabled=move || !context.can_write() on:click=move |event| open_serve.run(event)>"Start serve"</button> }.into_any()
                        }
                    })}
                    <button class="btn btn-quiet" on:click=move |_| context.touch()>"Refresh"</button>
                </div>
            </div>

            <div class="work-facts">
                <div><span class="faint">"Repository"</span><span class="mono">{summary.repository.clone()}</span></div>
                <div><span class="faint">"Requirement"</span><span class="mono">{short_digest(Some(&summary.requirement_digest))}</span></div>
                <div><span class="faint">"Frozen contract"</span><span class="mono">{short_digest(summary.frozen_contract_digest.as_deref())}</span></div>
                <div><span class="faint">"Review digest"</span><span class="mono">{short_digest(summary.review_digest.as_deref())}</span></div>
                <div><span class="faint">"Created"</span><span title=timestamp(summary.created_at_utc.as_deref())>{time_ago(summary.created_at_utc.as_deref())}</span></div>
                <div><span class="faint">"Updated"</span><span title=timestamp(summary.updated_at_utc.as_deref())>{time_ago(summary.updated_at_utc.as_deref())}</span></div>
            </div>

            {(!attention.is_empty()).then(|| view! {
                <div class="attention-list compact-attention">
                    {attention.iter().cloned().map(|item| view! { <AttentionCompact item=item/> }).collect_view()}
                </div>
            })}

            {summary.last_failure.clone().map(|failure| view! {
                <Notice kind="danger">
                    <strong>"Last failure: "</strong>
                    <span class="mono">{failure}</span>
                </Notice>
            })}

            <div class="tabs" role="tablist">
                {tab_button(tab, Tab::Lifecycle, "Lifecycle")}
                {tab_button(tab, Tab::Agent, "Agent activity")}
                {tab_button(tab, Tab::Evidence, "Evidence")}
                {tab_button(tab, Tab::Source, "Source & diff")}
                {tab_button(tab, Tab::Lineage, "Lineage")}
                {tab_button(tab, Tab::Events, "Events")}
            </div>

            {move || match tab.get() {
                Tab::Lifecycle => view! {
                    <LifecycleTab
                        data=data.clone()
                        contract=contract.clone()
                        on_approve=open_contract.clone()
                        on_reject=open_reject.clone()
                        on_merge=open_merge.clone()
                        can_merge=can_merge
                        can_serve=can_serve
                        serving=serving.clone()
                        on_serve=open_serve.clone()
                        on_stop_serve=stop_serve.clone()
                        next_actions=next_actions.clone()
                        is_terminal=is_terminal
                    />
                }.into_any(),
                Tab::Agent => view! {
                    <AgentTab
                        instance=instance.get()
                        work_item_id=work_item_id.clone()
                        dispatch_active=sessions_active
                        attempts=data.attempts.clone()
                    />
                }.into_any(),
                Tab::Evidence => view! {
                    <EvidenceTab data=data.clone()/>
                }.into_any(),
                Tab::Source => view! {
                    <SourceTab instance=instance.get() work_item_id=work_item_id.clone()/>
                }.into_any(),
                Tab::Lineage => view! {
                    <LineageTab instance=instance.get() work_item_id=work_item_id.clone()/>
                }.into_any(),
                Tab::Events => view! {
                    <EventsTab events=data.events.clone() instance=instance.get()/>
                }.into_any(),
            }}

            <div class="tiny faint mono">{detail_url}</div>
        </section>
    }
}

#[component]
fn LifecycleTab(
    data: WorkItemDetail,
    contract: Contract,
    on_approve: Callback<leptos::ev::MouseEvent>,
    on_reject: Callback<leptos::ev::MouseEvent>,
    on_merge: Callback<leptos::ev::MouseEvent>,
    can_merge: bool,
    can_serve: bool,
    serving: control_plane_domain::Serving,
    on_serve: Callback<leptos::ev::MouseEvent>,
    on_stop_serve: Callback<leptos::ev::MouseEvent>,
    next_actions: Vec<NextAction>,
    is_terminal: bool,
) -> impl IntoView {
    let context = AppContext::provide();
    let stages = data.stages;
    let reviews = data.reviews;
    let release = data.release;
    let review_digest = reviews
        .iter()
        .rev()
        .find(|review| review.outcome == "accepted")
        .and_then(|review| review.review_digest.clone());

    view! {
        <div class="split">
            <div class="stack">
                <Panel title="Workflow".to_string()>
                    <div class="stage-rail">
                        {stages.into_iter().map(|stage| view! { <StageRow stage=stage/> }).collect_view()}
                    </div>
                </Panel>

                <ContractPanel contract=contract on_approve=on_approve on_reject=on_reject/>

                <ReviewMatrix reviews=reviews.clone()/>

                <ReleaseEvidence release=release.clone()/>

                <ServingPanel serving=serving on_serve=on_serve on_stop_serve=on_stop_serve can_serve=can_serve/>
            </div>

            <div class="stack">
                <Panel title="Next legal actions".to_string()>
                    {if next_actions.is_empty() {
                        view! { <EmptyBlock title="No action offered" detail="The control plane does not expose a transition the factory has not authorised."/> }.into_any()
                    } else {
                        view! {
                            <div class="stack">
                                {next_actions.into_iter().map(|action| view! {
                                    <div class="action-row">
                                        <div class="row-between">
                                            <strong>{action.label}</strong>
                                            <Chip label=action.required_role.to_uppercase() tone=if action.available { "active" } else { "neutral" }/>
                                        </div>
                                        <p class="small muted">{action.reason}</p>
                                        <p class="tiny faint">{action.consequence}</p>
                                        {(!action.available).then(|| view! { <div class="tiny form-error">"Unavailable in this composition."</div> })}
                                    </div>
                                }).collect_view()}
                            </div>
                        }.into_any()
                    }}
                    <div class="tiny faint">
                        "Factory legal targets: "
                        {data.legal_state_targets.join(" · ")}
                    </div>
                </Panel>

                <Panel title="Merge authorization".to_string()>
                    {if can_merge {
                        view! {
                            <div class="stack">
                                <p class="small muted">"A separate human authorization is required. It binds the exact review evidence, head/base revisions, contract digest and policy versions."</p>
                                <div class="digest-line"><span class="tiny faint">"Review digest"</span><code class="mono">{short_digest(review_digest.as_deref())}</code></div>
                                <button class="btn btn-primary" disabled=move || !context.can_write() on:click=move |event| on_merge.run(event)>"Open authorization"</button>
                            </div>
                        }.into_any()
                    } else {
                        view! { <div class="tiny faint">"Merge authorization becomes available only after an accepted review leaves the item ReadyForDelivery."</div> }.into_any()
                    }}
                </Panel>

                <Panel title="Planning metadata".to_string()>
                    <crate::components::planning::PlanningEditor planning=data.planning.clone() version=data.summary.version work_item_id=data.summary.work_item_id.clone()/>
                </Panel>

                {is_terminal.then(|| view! {
                    <Notice kind="danger">
                        <strong>"Terminal state. "</strong>
                        <span>"The control plane does not reopen Failed, Blocked, Cancelled or Closed work. Submit a revised requirement for a new governed item."</span>
                    </Notice>
                })}
            </div>
        </div>
    }
}

#[component]
fn StageRow(stage: Stage) -> impl IntoView {
    let tone = stage_tone(&stage.state);
    let icon = match stage.state.as_str() {
        "completed" => "✓",
        "active" => "›",
        "waiting-authorization" => "!",
        "failed" | "rejected" | "blocked" => "×",
        "needs-rework" => "↻",
        _ => "·",
    };
    let tone_class = format!("stage stage-{tone}");
    let duration = stage
        .duration_seconds
        .map(|seconds| format_duration_seconds(seconds as f64));
    view! {
        <div class=tone_class>
            <div class="stage-marker">{icon}</div>
            <div class="stage-body">
                <div class="stage-head">
                    <span class="stage-name">{stage.label}</span>
                    <Chip label=stage.state.to_uppercase() tone=tone/>
                    {duration.map(|duration| view! { <span class="tiny faint mono">{duration}</span> })}
                    {stage.attempts.gt(&1).then(|| view! { <span class="tiny faint">{stage.attempts}" attempt(s)"</span> })}
                </div>
                <div class="stage-meta">
                    {stage.actor.clone().map(|actor| view! { <span>{actor}</span> })}
                    {stage.entered_at_utc.as_ref().map(|value| view! { <span title=timestamp(Some(value))>{time_ago(Some(value))}</span> })}
                </div>
                {stage.decision.clone().map(|decision| view! { <div class="stage-detail">{decision}</div> })}
                {stage.failure_category.clone().map(|failure| view! { <div class="form-error tiny">{failure}</div> })}
                {(!stage.evidence.is_empty()).then(|| view! {
                    <details class="evidence">
                        <summary>"Evidence"</summary>
                        <div class="stack" style="margin-top:6px">
                            {stage.evidence.into_iter().map(|evidence| view! {
                                <div class="tiny">
                                    <span class="faint">{evidence.role}</span>
                                    {evidence.session_id.map(|session| view! { <span class="mono">" · "{session}</span> })}
                                    {evidence.artifact_digest.map(|digest| view! { <span class="mono">" · "{short_digest(Some(&digest))}</span> })}
                                    {evidence.summary.map(|summary| view! { <div class="faint">{summary}</div> })}
                                </div>
                            }).collect_view()}
                        </div>
                    </details>
                })}
            </div>
        </div>
    }
}

#[component]
fn ContractPanel(
    contract: Contract,
    on_approve: Callback<leptos::ev::MouseEvent>,
    on_reject: Callback<leptos::ev::MouseEvent>,
) -> impl IntoView {
    let context = AppContext::provide();
    let scenarios = contract.scenarios.clone();
    let unresolved = contract.unresolved_questions.clone();
    view! {
        <Panel title="Frozen acceptance contract".to_string()>
            <div class="stack">
                <div class="row wrap">
                    <Chip
                        label=if contract.is_frozen { "FROZEN".to_string() } else if contract.awaiting_approval { "AWAITING APPROVAL".to_string() } else { "OPEN".to_string() }
                        tone=if contract.is_frozen { "success" } else if contract.awaiting_approval { "waiting" } else { "neutral" }
                    />
                    <Chip label=if contract.executable { "EXECUTABLE HTTP".to_string() } else { "NOT EXECUTABLE".to_string() } tone=if contract.executable { "active" } else { "danger" }/>
                </div>
                <div class="grid grid-2">
                    <DigestLine label="Proposal digest".to_string() value=contract.proposal_digest.clone()/>
                    <DigestLine label="Frozen digest".to_string() value=contract.frozen_digest.clone()/>
                    <DigestLine label="Accepted contract digest".to_string() value=contract.accepted_contract_digest.clone()/>
                    <DigestLine label="Requirement digest".to_string() value=contract.requirement_digest.clone()/>
                </div>
                {(!scenarios.is_empty()).then(|| view! {
                    <div class="table-wrap">
                        <table class="data">
                            <thead><tr><th>"Scenario"</th><th>"Kind"</th><th>"Path"</th><th>"Expectation"</th></tr></thead>
                            <tbody>{scenarios.into_iter().map(|scenario| view! {
                                <tr><td class="mono">{scenario.id}</td><td>{scenario.kind}</td><td class="mono">{scenario.path}</td><td>{scenario.expectation}</td></tr>
                            }).collect_view()}</tbody>
                        </table>
                    </div>
                })}
                {(!unresolved.is_empty()).then(|| view! {
                    <Notice kind="warn">
                        <strong>"Unresolved proposal questions. "</strong>
                        <span>"The factory refuses to freeze an ambiguous proposal; revise the requirement or resolve the observable outcome."</span>
                        <ul>{unresolved.into_iter().map(|question| view! { <li>{question}</li> }).collect_view()}</ul>
                    </Notice>
                })}
                {contract.rejection_reason.clone().map(|reason| view! { <Notice kind="danger"><strong>"Rejected: "</strong>{reason}</Notice> })}
                <div class="row">
                    {(!contract.executable && contract.is_frozen).then(|| view! {
                    <Notice kind="danger"><strong>"Non-executable frozen contract. "</strong><span>"Requirement coverage and post-merge verification cannot execute this proposal. Submit a revised requirement with an explicit acceptance contract."</span></Notice>
                })}
                {(!contract.executable && !contract.is_frozen).then(|| view! {
                    <Notice kind="warn"><strong>"Not executable. "</strong><span>"This proposal has no executable HTTP scenarios. The control plane will refuse approval because the review oracle could not run; add an acceptance contract and resubmit."</span></Notice>
                })}
                {contract.awaiting_approval.then(|| view! {
                        <button class="btn btn-primary" disabled=move || !context.can_write() || !contract.executable on:click=move |event| on_approve.run(event)>"Approve exact digest"</button>
                        <button class="btn btn-danger" disabled=move || !context.can_write() on:click=move |event| on_reject.run(event)>"Reject proposal"</button>
                    })}
                </div>
            </div>
        </Panel>
    }
}

#[component]
fn ReviewMatrix(reviews: Vec<ReviewHistory>) -> impl IntoView {
    let latest = reviews
        .iter()
        .rev()
        .find(|review| review.outcome == "accepted");
    let executions = latest
        .map(|review| review.executions.clone())
        .unwrap_or_default();
    let kinds = [
        "Validation",
        "RequirementCoverage",
        "ArchitecturalConformance",
        "Security",
    ];
    view! {
        <Panel title="Mandatory review checks".to_string()>
            {if executions.is_empty() {
                view! { <EmptyBlock title="No review evidence yet" detail="The four checks are recorded only after an implementation attempt reaches review."/> }.into_any()
            } else {
                view! {
                    <div class="review-grid">
                        {kinds.into_iter().map(|kind| {
                            let execution = executions.iter().find(|execution| execution.kind == kind).cloned();
                            match execution {
                                None => view! {
                                    <div class="review-cell review-missing">
                                        <div class="row-between"><strong>{review_kind_label(kind)}</strong><Chip label="MISSING".to_string() tone="danger"/></div>
                                        <div class="tiny faint">"A check that did not run is never implicitly passing."</div>
                                    </div>
                                }.into_any(),
                                Some(execution) => view! {
                                    <div class=format!("review-cell {}", if execution.passed { "review-pass" } else { "review-fail" })>
                                        <div class="row-between"><strong>{review_kind_label(kind)}</strong><Chip label=execution.outcome.to_uppercase() tone=if execution.passed { "success" } else { "danger" }/></div>
                                        <div class="tiny faint mono">{execution.check_id.clone()}</div>
                                        <div class="tiny faint">{execution.detail.clone()}</div>
                                        <div class="tiny mono">{short_digest(Some(&execution.artifact_digest))}</div>
                                    </div>
                                }.into_any(),
                            }
                        }).collect_view()}
                    </div>
                }.into_any()
            }}
        </Panel>
    }
}

#[component]
fn ReleaseEvidence(release: Option<Release>) -> impl IntoView {
    view! {
        <Panel title="Release and engineering evidence".to_string()>
            {match release {
                None => view! { <EmptyBlock title="No release evidence" detail="A candidate becomes a release only after merge and post-merge verification."/> }.into_any(),
                Some(release) => view! {
                    <div class="stack">
                        <div class="row wrap">
                            <Chip label=if release.release_recorded { "RELEASE RECORDED".to_string() } else { "CANDIDATE".to_string() } tone=if release.release_recorded { "success" } else { "waiting" }/>
                            {release.verification.as_ref().map(|verification| view! {
                                <Chip label=if verification.verified { "POST-MERGE VERIFIED".to_string() } else { "VERIFICATION FAILED".to_string() } tone=if verification.verified { "success" } else { "danger" }/>
                            })}
                        </div>
                        <div class="grid grid-2">
                            <DigestLine label="Merged revision".to_string() value=release.merged_sha.clone()/>
                            <DigestLine label="Merged tree".to_string() value=release.merged_tree_sha.clone()/>
                            <DigestLine label="Approved head".to_string() value=release.approved_head_sha.clone()/>
                            <DigestLine label="Base revision".to_string() value=release.base_sha.clone()/>
                        </div>
                        {release.verification.as_ref().map(|verification| view! {
                            <div class="stack">
                                <div class="tiny faint">{verification.detail.clone()}</div>
                                <div class="grid grid-2">
                                    <DigestLine label="Build evidence".to_string() value=Some(verification.build_evidence_digest.clone())/>
                                    <DigestLine label="Regression evidence".to_string() value=Some(verification.regression_evidence_digest.clone())/>
                                    <DigestLine label="Contract evidence".to_string() value=Some(verification.contract_evidence_digest.clone())/>
                                    <DigestLine label="Record digest".to_string() value=Some(verification.record_digest.clone())/>
                                </div>
                                <div class="tiny mono">{verification.scenario_summary.clone()}</div>
                            </div>
                        })}
                        <div class="engineering-split">
                            <div class="engineering-box">
                                <div class="label">"Engineering status"</div>
                                <div>{if release.verification.as_ref().map(|v| v.verified).unwrap_or(false) { "Post-merge build, regressions and contract passed on the exact merged revision." } else { "Not yet engineering-verified." }}</div>
                            </div>
                            <div class="engineering-box">
                                <div class="label">"Independent acceptance"</div>
                                <div class="muted">"Not inferred. Evaluator-owned acceptance evidence is not present in this control-plane projection, and passing local checks does not establish it."</div>
                            </div>
                        </div>
                    </div>
                }.into_any(),
            }}
        </Panel>
    }
}

#[component]
fn ServingPanel(
    serving: control_plane_domain::Serving,
    on_serve: Callback<leptos::ev::MouseEvent>,
    on_stop_serve: Callback<leptos::ev::MouseEvent>,
    can_serve: bool,
) -> impl IntoView {
    let context = AppContext::provide();
    let active = serving.active;
    view! {
        <Panel title="Supervised serving".to_string()>
            <div class="stack">
                <div class="row wrap">
                    <Chip label=serving.status.clone().unwrap_or_else(|| "not-served".to_string()).to_uppercase() tone=if active { "success" } else if serving.status.as_deref() == Some("failed") { "danger" } else { "neutral" }/>
                    {active.then(|| view! { <span class="tiny faint">"Process "{serving.process_id.unwrap_or_default()}" · restarts "{serving.restart_count.unwrap_or_default()}</span> })}
                </div>
                <InfoRow label="Merged revision".to_string()><span class="mono">{short_sha(serving.merged_sha.as_deref())}</span></InfoRow>
                <InfoRow label="Last observation".to_string()>{serving.recorded_at_utc.map(|time| view! { <span>{time_ago(Some(&time.to_rfc3339()))}</span> }.into_any()).unwrap_or_else(|| view! { <span class="faint">"None"</span> }.into_any())}</InfoRow>
                {serving.detail.clone().map(|detail| view! { <div class="tiny faint">{detail}</div> })}
                {active.then(|| view! {
                    <div class="stack">
                        {serving.url.clone().map(|url| view! {
                            <a class="btn" href=url.clone() target="_blank" rel="noopener noreferrer">"Open verified preview"</a>
                        })}
                        <button class="btn btn-danger" disabled=move || !context.can_write() on:click=move |event| on_stop_serve.run(event)>"Stop supervised serve"</button>
                    </div>
                })}
                {(!active && can_serve).then(|| view! {
                    <button class="btn" disabled=move || !context.can_write() on:click=move |event| on_serve.run(event)>"Start bounded serve window"</button>
                })}
                <div class="tiny faint">
                    "A verified release is not automatically a running application. The preview link appears only while the factory owns a reachable supervised process."
                </div>
            </div>
        </Panel>
    }
}

#[component]
fn PlanningSummary(planning: Planning) -> impl IntoView {
    view! {
        <div class="stack">
            <div class="row-between"><span class="muted">"Priority"</span><span>{planning.priority.clone().unwrap_or_else(|| "—".to_string())}</span></div>
            <div class="row-between"><span class="muted">"Deadline"</span><span>{planning.deadline_utc.clone().unwrap_or_else(|| "—".to_string())}</span></div>
            <div class="row-between"><span class="muted">"Tags"</span><span>{planning.tags.join(", ")}</span></div>
            {(!planning.links.is_empty()).then(|| view! { <div class="tiny mono">{planning.links.join(" · ")}</div> })}
            {planning.notes.clone().map(|notes| view! { <div class="tiny muted">{notes}</div> })}
            <div class="tiny faint">"Control-plane planning metadata. Not factory scheduling or execution authority."</div>
        </div>
    }
}

#[component]
fn AttentionCompact(item: Attention) -> impl IntoView {
    view! {
        <div class=format!("attention attention-{}", if item.severity == "critical" { "danger" } else if item.severity == "warning" { "warn" } else { "info" })>
            <div class="attention-bar"></div>
            <div class="attention-body">
                <div class="attention-title"><Chip label=item.severity.to_uppercase() tone=if item.severity == "critical" { "danger" } else if item.severity == "warning" { "warn" } else { "info" }/><span>{item.title}</span></div>
                <div class="small muted">{item.what_happened}</div>
                <div class="tiny"><span class="faint">"Safe action: "</span>{item.safe_action}</div>
                <div class="tiny faint">{item.consequence}</div>
            </div>
        </div>
    }
}

#[component]
fn AgentTab(
    instance: String,
    work_item_id: String,
    dispatch_active: bool,
    attempts: Vec<Attempt>,
) -> impl IntoView {
    let context = AppContext::provide();
    let _ = context;
    let sessions = LocalResource::new(move || {
        let instance = instance.clone();
        let work_item_id = work_item_id.clone();
        async move {
            if !api::is_client() {
                None
            } else {
                Some(api::work_sessions(&instance, &work_item_id).await)
            }
        }
    });

    view! {
        <div class="stack">
            {dispatch_active.then(|| view! {
                <Notice kind="accent">
                    <strong>"Agent execution is in progress. "</strong>
                    <span>"The current factory runtime publishes stage events while the agent loop is running; token-level tool evidence is durable once the attempt completes. No guessed progress is displayed."</span>
                </Notice>
            })}

            <Panel title="Implementation attempts".to_string()>
                {if attempts.is_empty() {
                    view! { <EmptyBlock title="No implementation attempt yet" detail="The factory records an attempt only when the implementation stage is dispatched."/> }.into_any()
                } else {
                    view! {
                        <div class="timeline">
                            {attempts.into_iter().map(|attempt| view! {
                                <div class="event">
                                    <div class="event-time">{time_ago(Some(&attempt.finished_at_utc.to_rfc3339()))}</div>
                                    <div class="event-main">
                                        <div class="event-title">
                                            <strong>{format!("Attempt {}", attempt.attempt_number)}</strong>
                                            <Chip label=attempt.outcome.to_uppercase() tone=if attempt.outcome == "succeeded" { "success" } else { "danger" }/>
                                            <span class="tiny mono">{attempt.session_id.clone()}</span>
                                        </div>
                                        <div class="tiny faint">{attempt.failure_category.clone().unwrap_or_default()}</div>
                                        {attempt.detail.clone().map(|detail| view! { <div class="tiny muted">{detail}</div> })}
                                        <div class="tiny mono">
                                            {attempt.base_sha.as_ref().map(|sha| format!("base {}", short_sha(Some(sha)))).unwrap_or_default()}
                                            " · "
                                            {attempt.head_sha.as_ref().map(|sha| format!("head {}", short_sha(Some(sha)))).unwrap_or_default()}
                                        </div>
                                    </div>
                                </div>
                            }).collect_view()}
                        </div>
                    }.into_any()
                }}
            </Panel>

            <Panel title="Agent sessions and tool evidence".to_string()>
                <Suspense fallback=move || view! { <LoadingBlock label="Loading durable session traces…"/> }>
                    {move || match sessions.get().flatten() {
                        None => view! { <LoadingBlock label="Loading durable session traces…"/> }.into_any(),
                        Some(Err(error)) => view! { {error_view(error)} }.into_any(),
                        Some(Ok(sessions)) => view! {
                            if sessions.is_empty() {
                                view! { <EmptyBlock title="No model session trace" detail="This run has no durable model session trace. The implementation attempt journal above is still authoritative for execution outcome; deterministic runs do not fabricate model tokens or tool activity."/> }.into_any()
                            } else {
                                view! {
                                    <div class="stack">
                                        {sessions.into_iter().map(|session| view! { <SessionView session=session/> }).collect_view()}
                                    </div>
                                }.into_any()
                            }
                        }.into_any(),
                    }}
                </Suspense>
            </Panel>
        </div>
    }
}

#[component]
fn SessionView(session: AgentSession) -> impl IntoView {
    let token_text = if session.tokens_measured {
        format!(
            "{} in / {} out tokens",
            session.input_tokens, session.output_tokens
        )
    } else {
        "Token usage not measured".to_string()
    };
    view! {
        <div class="session-card">
            <div class="row-between">
                <div>
                    <strong>{session.session_id.clone()}</strong>
                    <div class="tiny faint">{session.agent_definition_id.clone()}" · profile v"{session.profile_version}</div>
                </div>
                <Chip label=session.outcome.to_uppercase() tone=if session.outcome == "completed" { "success" } else { "danger" }/>
            </div>
            <div class="row wrap tiny faint">
                <span class="mono">{session.model_id.clone()}</span>
                <span>{token_text}</span>
                <span>{session.measurement_source.clone()}</span>
                <span title=timestamp(Some(&session.recorded_at_utc.to_rfc3339()))>{time_ago(Some(&session.recorded_at_utc.to_rfc3339()))}</span>
            </div>
            <div class="small muted">{session.summary.clone()}</div>
            {session.failure_category.clone().map(|failure| view! { <div class="form-error tiny">{failure}</div> })}
            {(!session.tool_invocations.is_empty()).then(|| view! {
                <details class="evidence">
                    <summary>{format!("{} tool invocation(s)", session.tool_invocations.len())}</summary>
                    <div class="stack" style="margin-top:6px">
                        {session.tool_invocations.into_iter().map(|invocation| view! {
                            <div class="tool-invocation">
                                <div class="row-between">
                                    <strong class="mono">{invocation.tool_name.clone()}</strong>
                                    <Chip label=if invocation.failed { "FAILED".to_string() } else { "OK".to_string() } tone=if invocation.failed { "danger" } else { "success" }/>
                                </div>
                                <div class="tiny faint mono">{invocation.arguments_digest.clone()}</div>
                                <pre class="tool-excerpt">{format!("arguments:\n{}\n\nresult:\n{}", invocation.arguments_excerpt, invocation.result_excerpt)}</pre>
                            </div>
                        }).collect_view()}
                    </div>
                </details>
            })}
            {(!session.tool_names.is_empty()).then(|| view! { <div class="tiny faint">"Tools: "{session.tool_names.join(", ")}</div> })}
        </div>
    }
}

#[component]
fn EvidenceTab(data: WorkItemDetail) -> impl IntoView {
    view! {
        <div class="stack">
            <Panel title="Engineering versus independent acceptance".to_string()>
                <div class="engineering-split">
                    <div class="engineering-box">
                        <div class="label">"Engineering status"</div>
                        <p>{match &data.release {
                            Some(release) if release.verification.as_ref().map(|verification| verification.verified).unwrap_or(false) =>
                                "Post-merge verification passed on the exact merged revision.".to_string(),
                            Some(_) => "A release/candidate record exists, but verified post-merge evidence is not present.".to_string(),
                            None => "No release evidence has been recorded. Engineering status is not complete.".to_string(),
                        }}</p>
                    </div>
                    <div class="engineering-box">
                        <div class="label">"Independent acceptance"</div>
                        <p class="muted">"Not inferred. The evaluator owns its expectations and candidate-bound evidence. No action in this application can manufacture acceptance."</p>
                    </div>
                </div>
            </Panel>

            <Panel title="Review evidence".to_string()>
                {if data.reviews.is_empty() {
                    view! { <EmptyBlock title="No review history" detail="The four mandatory checks appear here after the first review."/> }.into_any()
                } else {
                    view! {
                        <div class="stack">
                            {data.reviews.into_iter().map(|review| view! { <ReviewHistoryCard review=review/> }).collect_view()}
                        </div>
                    }.into_any()
                }}
            </Panel>

            <Panel title="Durable event evidence".to_string()>
                <div class="tiny faint">"Every row below is a committed factory event. Expand an event to inspect bounded, redacted technical data."</div>
                <EventsList events=data.events instance=String::new()/>
            </Panel>
        </div>
    }
}

#[component]
fn ReviewHistoryCard(review: ReviewHistory) -> impl IntoView {
    view! {
        <div class="review-history">
            <div class="row-between">
                <div class="row">
                    <Chip label=review.outcome.to_uppercase() tone=if review.outcome == "accepted" { "success" } else { "danger" }/>
                    <span class="tiny mono">{short_digest(review.review_digest.as_deref())}</span>
                </div>
                <span class="tiny faint">{time_ago(review.recorded_at_utc.as_deref())}</span>
            </div>
            {review.reason.clone().map(|reason| view! { <div class="small form-error">{reason}</div> })}
            {review.checks_summary.clone().map(|summary| view! { <div class="tiny mono">{summary}</div> })}
            {(!review.executions.is_empty()).then(|| view! {
                <div class="review-grid">
                    {review.executions.into_iter().map(|execution| view! { <ExecutionCard execution=execution/> }).collect_view()}
                </div>
            })}
            <div class="grid grid-2 tiny">
                <DigestLine label="Reviewed SHA".to_string() value=review.head_sha.clone()/>
                <DigestLine label="Contract".to_string() value=review.contract_digest.clone()/>
                <DigestLine label="Evidence".to_string() value=review.evidence_digest.clone()/>
            </div>
        </div>
    }
}

#[component]
fn ExecutionCard(execution: ReviewExecution) -> impl IntoView {
    view! {
        <div class=format!("review-cell {}", if execution.passed { "review-pass" } else { "review-fail" })>
            <div class="row-between">
                <strong>{review_kind_label(&execution.kind).to_string()}</strong>
                <Chip label=execution.outcome.to_uppercase() tone=if execution.passed { "success" } else { "danger" }/>
            </div>
            <div class="tiny faint mono">{execution.check_id.clone()}</div>
            <div class="tiny faint">{execution.executed_by.clone()}" · "{execution.executed_version.clone()}</div>
            <div class="tiny mono">{short_digest(Some(&execution.reviewed_sha))}</div>
            <div class="tiny mono">{short_digest(Some(&execution.artifact_digest))}</div>
            <div class="tiny faint">{execution.detail.chars().take(500).collect::<String>()}</div>
        </div>
    }
}

#[component]
fn SourceTab(instance: String, work_item_id: String) -> impl IntoView {
    let context = AppContext::provide();
    let _ = context;
    let selected = RwSignal::new(Option::<String>::None);
    let diff_instance = instance.clone();
    let diff_work_item = work_item_id.clone();
    let diff = LocalResource::new(move || {
        let instance = diff_instance.clone();
        let work_item_id = diff_work_item.clone();
        async move {
            if !api::is_client() {
                None
            } else {
                Some(api::diff(&instance, &work_item_id).await)
            }
        }
    });

    view! {
        <Suspense fallback=move || view! { <LoadingBlock label="Loading reviewed source evidence…"/> }>
            {move || match diff.get().flatten() {
                None => view! { <LoadingBlock label="Loading reviewed source evidence…"/> }.into_any(),
                Some(Err(error)) => view! {
                    if error.code == "diff-not-available" {
                        view! { <EmptyBlock title="No reviewed revision yet" detail="The diff becomes available after an implementation attempt produces a committed head revision."/> }.into_any()
                    } else {
                        view! { {error_view(error)} }.into_any()
                    }
                }.into_any(),
                Some(Ok(diff)) => view! {
                    <DiffView
                        instance=instance.clone()
                        work_item_id=work_item_id.clone()
                        diff=diff
                        selected=selected
                    />
                }.into_any(),
            }}
        </Suspense>
    }
}

#[component]
fn DiffView(
    instance: String,
    work_item_id: String,
    diff: Diff,
    selected: RwSignal<Option<String>>,
) -> impl IntoView {
    let files = diff.files.clone();
    let files_for_resource = files.clone();
    let patch = diff.patch.clone();
    let selected_file = selected
        .get()
        .or_else(|| files.first().map(|file| file.path.clone()));
    let file_content = LocalResource::new(move || {
        let instance = instance.clone();
        let work_item_id = work_item_id.clone();
        let path = selected.get();
        async move {
            match path {
                Some(path) if api::is_client() => {
                    Some(api::file(&instance, &work_item_id, &path).await)
                }
                _ => None,
            }
        }
    });

    view! {
        <PanelActions
            title="Reviewed diff".to_string()
            actions=view! { <span class="tiny faint mono">{diff.base_sha.chars().take(10).collect::<String>()}" → "{diff.head_sha.chars().take(10).collect::<String>()}</span> }.into_any()
        >
            <div class="diff-shell">
                <div class="diff-files">
                    {files.into_iter().map(|file| {
                        let path = file.path.clone();
                        let active_path = selected.get().or_else(|| diff.files.first().map(|f| f.path.clone()));
                        let active = active_path.as_deref() == Some(path.as_str());
                        view! {
                            <button class:active=active class="diff-file" on:click=move |_| selected.set(Some(path.clone()))>
                                <span class="diff-file-status">{file.status.chars().next().unwrap_or('?')}</span>
                                <span>{file.path.clone()}</span>
                            </button>
                        }
                    }).collect_view()}
                </div>
                <div class="diff-content">
                    {move || {
                        let content = file_content.get().flatten();
                        match content {
                            Some(Ok(file)) => view! {
                                <pre class="diff">{file.content.clone()}</pre>
                            }.into_any(),
                            Some(Err(error)) => view! { {error_view(error)} }.into_any(),
                            None => view! {
                                <pre class="diff">{patch.lines().map(|line| {
                                    let class = if line.starts_with('+') && !line.starts_with("+++") {
                                        "diff-add"
                                    } else if line.starts_with('-') && !line.starts_with("---") {
                                        "diff-del"
                                    } else if line.starts_with("@@") {
                                        "diff-hunk"
                                    } else {
                                        "diff-line"
                                    };
                                    view! { <span class=class>{format!("{line}\n")}</span> }
                                }).collect_view()}</pre>
                            }.into_any(),
                        }
                    }}
                </div>
            </div>
            {diff.truncated.then(|| view! { <div class="tiny form-error">"The patch is truncated at the adapter byte bound; inspect individual changed files."</div> })}
            <div class="tiny faint">"Only files changed by the reviewed revision are addressable. This is not unrestricted filesystem browsing."</div>
        </PanelActions>
    }
}

#[component]
fn LineageTab(instance: String, work_item_id: String) -> impl IntoView {
    let lineage = LocalResource::new(move || {
        let instance = instance.clone();
        let work_item_id = work_item_id.clone();
        async move {
            if !api::is_client() {
                None
            } else {
                Some(api::lineage(&instance, &work_item_id).await)
            }
        }
    });

    view! {
        <Suspense fallback=move || view! { <LoadingBlock label="Loading execution lineage…"/> }>
            {move || match lineage.get().flatten() {
                None => view! { <LoadingBlock label="Loading execution lineage…"/> }.into_any(),
                Some(Err(error)) => view! { {error_view(error)} }.into_any(),
                Some(Ok(lineage)) => view! {
                    <Panel title="Execution lineage".to_string()>
                        <div class="lineage-grid">
                            {lineage.nodes.into_iter().map(|node| view! {
                                <div class=format!("lineage-node lineage-{}", node.kind)>
                                    <div class="tiny faint mono">{node.kind.clone()}</div>
                                    <strong>{node.label.clone()}</strong>
                                    {node.detail.map(|detail| view! { <div class="tiny faint">{detail}</div> })}
                                </div>
                            }).collect_view()}
                        </div>
                        <div class="tiny faint" style="margin-top:12px">{format!("{} edge(s) connect work item, stages, sessions, attempts, tools, reviews and release evidence.", lineage.edges.len())}</div>
                    </Panel>
                }.into_any(),
            }}
        </Suspense>
    }
}

#[component]
fn EventsTab(events: Vec<FactoryEvent>, instance: String) -> impl IntoView {
    let filter = RwSignal::new(String::new());
    view! {
        <Panel title="Durable events".to_string()>
            <input class="input" placeholder="Filter by event type, subject or summary" prop:value=move || filter.get() on:input=move |event| filter.set(event_target_value(&event))/>
            <EventsListFiltered events=events instance=instance filter=filter/>
        </Panel>
    }
}

#[component]
fn EventsList(events: Vec<FactoryEvent>, instance: String) -> impl IntoView {
    let filter = RwSignal::new(String::new());
    view! {
        <EventsListFiltered events=events instance=instance filter=filter/>
    }
}

#[component]
fn EventsListFiltered(
    events: Vec<FactoryEvent>,
    instance: String,
    filter: RwSignal<String>,
) -> impl IntoView {
    view! {
        <div class="timeline">
            {move || {
                let needle = filter.get().to_ascii_lowercase();
                events.iter().filter(|event| {
                    needle.is_empty()
                        || event.r#type.to_ascii_lowercase().contains(&needle)
                        || event.summary.as_deref().unwrap_or("").to_ascii_lowercase().contains(&needle)
                        || event.subject.to_ascii_lowercase().contains(&needle)
                }).rev().map(|event| {
                    let event = event.clone();
                    let href = if instance.is_empty() { None } else { Some(format!("/i/{}/work/{}", instance, event.work_item_id.clone().unwrap_or_default())) };
                    view! {
                        <div class="event">
                            <div class="event-time" title=timestamp(Some(&event.time_utc.to_rfc3339()))>{time_ago(Some(&event.time_utc.to_rfc3339()))}</div>
                            <div class="event-main">
                                <div class="event-title">
                                    {href.map(|href| view! { <a href=href>{event.summary.clone().unwrap_or_else(|| event.r#type.clone())}</a> })}
                                    {(!event.summary.is_some()).then(|| view! { <span>{event.r#type.clone()}</span> })}
                                    <Chip label=event.r#type.clone() tone="neutral"/>
                                </div>
                                <div class="tiny faint mono">{event.id.clone()}</div>
                                <details class="evidence">
                                    <summary>"Technical data"</summary>
                                    <pre>{serde_json::to_string_pretty(&event.data).unwrap_or_default()}</pre>
                                </details>
                            </div>
                        </div>
                    }
                }).collect_view()
            }}
        </div>
    }
}

fn tab_button(tab: RwSignal<Tab>, value: Tab, label: &'static str) -> impl IntoView {
    view! {
        <button class:active=move || tab.get() == value role="tab" on:click=move |_| tab.set(value)>{label}</button>
    }
}

fn health_label(health: &str) -> &'static str {
    match health {
        "serving" => "Serving",
        "operational" => "Operational",
        "release-verified" => "Release verified",
        "terminal-failure" => "Terminal failure",
        "blocked" => "Blocked",
        "waiting-authorization" => "Waiting for authorization",
        "awaiting-rework" => "Awaiting rework",
        "executing" => "Executing",
        "dispatching" => "Dispatching",
        "awaiting-dispatch" => "Awaiting dispatch",
        "idle" => "Idle",
        _ => "Unknown",
    }
}

fn health_tone(health: &str) -> &'static str {
    match health {
        "serving" | "operational" | "release-verified" => "success",
        "terminal-failure" | "blocked" => "danger",
        "waiting-authorization" | "awaiting-rework" => "waiting",
        "executing" | "dispatching" => "active",
        _ => "neutral",
    }
}

#[component]
fn ActionDialog(
    dialog: RwSignal<Option<Dialog>>,
    instance: Signal<String>,
    work_item_id: Signal<String>,
) -> impl IntoView {
    let context = AppContext::provide();
    let busy = RwSignal::new(false);
    let confirmation = RwSignal::new(String::new());
    let reason = RwSignal::new(String::new());
    let duration = RwSignal::new(60i32);
    let confirmed = RwSignal::new(false);

    let close = move |_| dialog.set(None);

    let submit = move |_| {
        let Some(current) = dialog.get() else {
            return;
        };
        let instance = instance.get();
        let work_item_id = work_item_id.get();
        let csrf = context.csrf.get();
        busy.set(true);
        let confirmed_value = confirmed.get();
        let confirmation_value = confirmation.get();
        let reason_value = reason.get();
        let duration_value = duration.get();
        spawn_local(async move {
            let result = match current {
                Dialog::ApproveContract { digest } => {
                    api::approve_contract(
                        &instance,
                        &csrf,
                        &work_item_id,
                        &control_plane_domain::ContractApproveRequest {
                            proposal_digest: digest,
                            confirmed: confirmed_value,
                        },
                        &api::new_idempotency_key(),
                    )
                    .await
                }
                Dialog::RejectContract { digest } => {
                    api::reject_contract(
                        &instance,
                        &csrf,
                        &work_item_id,
                        &control_plane_domain::ContractRejectRequest {
                            proposal_digest: digest,
                            reason: (!reason_value.trim().is_empty()).then_some(reason_value),
                        },
                        &api::new_idempotency_key(),
                    )
                    .await
                }
                Dialog::ApproveMerge { digest } => {
                    api::approve_merge(
                        &instance,
                        &csrf,
                        &work_item_id,
                        &control_plane_domain::MergeApprovalRequest {
                            review_digest: digest,
                            confirmation: confirmation_value,
                        },
                        &api::new_idempotency_key(),
                    )
                    .await
                }
                Dialog::Serve { .. } => {
                    api::start_serve(&instance, &csrf, &work_item_id, duration_value).await
                }
            };

            busy.set(false);
            match result {
                Ok(_) => {
                    context.notify_success("Command accepted by the factory adapter.");
                    dialog.set(None);
                    context.touch();
                }
                Err(error) => context.notify_error(error),
            }
        });
    };

    view! {
        {move || dialog.get().map(|current| {
            let (title, body, can_submit) = match &current {
                Dialog::ApproveContract { digest } => (
                    "Approve exact contract proposal".to_string(),
                    view! {
                        <div class="stack">
                            <p class="small muted">"Approval freezes the exact proposal digest below. The factory will refuse any later approval for a different digest."</p>
                            <DigestLine label="Proposal digest".to_string() value=Some(digest.clone())/>
                            <label class="check"><input type="checkbox" prop:checked=move || confirmed.get() on:change=move |event| confirmed.set(event_target_checked(&event))/><span>"I inspected the proposal, scenarios and unresolved questions."</span></label>
                        </div>
                    }.into_any(),
                    confirmed.get(),
                ),
                Dialog::RejectContract { digest } => (
                    "Reject contract proposal".to_string(),
                    view! {
                        <div class="stack">
                            <DigestLine label="Proposal digest".to_string() value=Some(digest.clone())/>
                            <label class="field">"Reason"
                                <textarea class="textarea small-area" prop:value=move || reason.get() on:input=move |event| reason.set(event_target_value(&event))></textarea>
                            </label>
                            <p class="tiny faint">"A rejection is durable and prevents approval of this same proposal digest."</p>
                        </div>
                    }.into_any(),
                    true,
                ),
                Dialog::ApproveMerge { digest } => (
                    "Authorize merge".to_string(),
                    view! {
                        <div class="stack">
                            <p class="small muted">"This authorizes the factory to merge the reviewed head and run post-merge verification. It does not certify independent acceptance."</p>
                            <DigestLine label="Review digest".to_string() value=Some(digest.clone())/>
                            <label class="field">"Type the exact review digest to confirm"
                                <input class="input mono" prop:value=move || confirmation.get() on:input=move |event| confirmation.set(event_target_value(&event))/>
                            </label>
                            <div class="tiny faint">"The adapter independently refuses a mismatch."</div>
                        </div>
                    }.into_any(),
                    confirmation.get().trim() == digest.trim(),
                ),
                Dialog::Serve { duration: initial } => (
                    "Start supervised serve window".to_string(),
                    view! {
                        <div class="stack">
                            <label class="field">"Duration seconds"
                                <input class="input" type="number" min="10" max="300" prop:value=move || duration.get().to_string() on:input=move |event| duration.set(event_target_value(&event).parse().unwrap_or(60))/>
                            </label>
                            <p class="tiny faint">"The factory materialises the exact released revision, proves process identity, runs the frozen contract and then supervises the process for the bounded window."</p>
                        </div>
                    }.into_any(),
                    duration.get() >= 10,
                ),
            };
            view! {
                <div class="dialog-backdrop" role="dialog" aria-modal="true" aria-label=title.clone()>
                    <div class="dialog">
                        <div class="dialog-head">
                            <h2>{title.clone()}</h2>
                            <button class="btn btn-quiet" on:click=close.clone()>"Close"</button>
                        </div>
                        <div class="dialog-body">{body}</div>
                        <div class="dialog-foot">
                            <button class="btn" on:click=close.clone()>"Cancel"</button>
                            <button class="btn btn-primary" disabled=move || busy.get() || !can_submit on:click=submit.clone()>
                                {move || if busy.get() { "Submitting…" } else { "Confirm" }}
                            </button>
                        </div>
                    </div>
                </div>
            }
        })}
    }
}
