use leptos::prelude::*;
use leptos::task::spawn_local;
use leptos_router::hooks::{use_location, use_params_map};

use control_plane_domain::{
    AgentSession, Attempt, Attention, Contract, ContractScenario, Diff, FactoryEvent, NextAction,
    Release, ReviewExecution, ReviewHistory, Stage, WorkItemDetail, format_duration_seconds,
    indicator_label, indicator_tone, outcome_label, review_kind_label, role_label, serving_view,
    severity_label, severity_tone, short_digest, short_sha, stage_state_label, stage_tone,
    state_label, state_tone,
};

use crate::api;
use crate::components::feedback::typed_confirmation_matches;
use crate::components::modal::Modal;
use crate::components::ui::{
    DigestCopy, DigestLine, EmptyBlock, InfoRow, LoadingBlock, Notice, Panel, error_view,
    time_ago, timestamp,
};
use crate::components::{
    Confirmation, DiffViewer, EvidenceCheck, EvidenceSummary, HealthState, StatusIndicator,
    TabItem, TabPanel, Tabs, TypedConfirmation,
};
use crate::context::AppContext;

/// One panel is reused for every tab, so `aria-controls` always resolves and the roving tabindex
/// follows the selected tab.
const TAB_PANEL_ID: &str = "work-tab-panel";

const TABS: [TabItem; 6] = [
    TabItem::new("lifecycle", "Lifecycle"),
    TabItem::new("agent", "Agent activity"),
    TabItem::new("evidence", "Evidence"),
    TabItem::new("source", "Source & diff"),
    TabItem::new("lineage", "Lineage"),
    TabItem::new("events", "Events"),
];

#[derive(Debug, Clone)]
enum Dialog {
    ApproveContract { digest: String },
    RejectContract { digest: String },
    ApproveMerge { digest: String },
}

#[component]
pub fn WorkDetailPage() -> impl IntoView {
    let context = AppContext::provide();
    let params = use_params_map();
    let instance = Signal::derive(move || params.get().get("instance").unwrap_or_default());
    let work_item_id = Signal::derive(move || params.get().get("work_item_id").unwrap_or_default());
    let tab = RwSignal::new("lifecycle");
    let dialog = RwSignal::new(None::<Dialog>);
    let refresh = context.refresh;
    let location = use_location();
    let requested =
        Signal::derive(move || location.query.get().get("authorize").unwrap_or_default());

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
                        requested=requested
                    />
                }.into_any(),
            }}
        </Suspense>
    }
}

#[component]
fn WorkDetailBody(
    data: WorkItemDetail,
    instance: Signal<String>,
    tab: RwSignal<&'static str>,
    dialog: RwSignal<Option<Dialog>>,
    requested: Signal<String>,
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
    let serving = data.serving.clone();
    let reviews = data.reviews.clone();
    let requirement = data.requirement.clone();
    let sessions_active = data.dispatch_active && state == "InImplementation";

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

    // One entry point per decision, all in the header action row. The lifecycle panel shows the
    // evidence and the current state, never a second button for the same mutation.
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
            .rev()
            .find(|review| review.outcome == "accepted")
            .and_then(|review| review.review_digest.clone())
            .unwrap_or_default();
        move |_| {
            dialog.set(Some(Dialog::ApproveMerge {
                digest: digest.clone(),
            }))
        }
    });

    let has_approve_contract = contract.awaiting_approval;
    let latest_accepted_review = reviews
        .iter()
        .rev()
        .find(|review| review.outcome == "accepted")
        .cloned();
    let can_merge = state == "ReadyForDelivery" && latest_accepted_review.is_some();
    let merge_digest = summary
        .review_digest
        .clone()
        .or_else(|| {
            latest_accepted_review
                .as_ref()
                .and_then(|review| review.review_digest.clone())
        })
        .unwrap_or_default();

    // `?authorize=merge` deep-links the Quality inbox to this one authorization surface.
    let deep_link_opened = RwSignal::new(false);
    let merge_digest_for_effect = merge_digest.clone();
    Effect::new(move |_| {
        if deep_link_opened.get() || !can_merge || requested.get() != "merge" {
            return;
        }
        if merge_digest_for_effect.is_empty() {
            return;
        }
        dialog.set(Some(Dialog::ApproveMerge {
            digest: merge_digest_for_effect.clone(),
        }));
        deep_link_opened.set(true);
    });
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
    let state_text = state_label(&state).to_string();

    let show_decision = has_approve_contract || can_merge;

    // The tab body renders inside a `move` closure that takes ownership of everything it
    // references. Dedicated clones keep the decision dialog bound to the same authoritative
    // objects without weakening either view.
    let tab_work_item_id = work_item_id.clone();
    let tab_contract = contract.clone();
    let tab_serving = serving.clone();
    view! {
        <section class="page">
            <div class="work-header">
                <div class="work-title-block">
                    <div class="work-id-line">
                        <span class="mono">{work_item_id.clone()}</span>
                        <span class="tiny faint">"v"{version}</span>
                        <span class="tiny faint">"Updated "{time_ago(summary.updated_at_utc.as_deref())}</span>
                    </div>
                    <h1 class="page-title">{title}</h1>
                    <div class="work-state-line">
                        <StatusIndicator
                            label=state_text
                            tone=state_tone(&state)
                            raw=state.clone()
                        />
                        {show_indicator.then(|| view! {
                            <StatusIndicator
                                label=indicator_text.clone()
                                tone=indicator_tone_value
                                raw=indicator.clone().unwrap_or_default()
                            />
                        })}
                        <HealthState health=health.clone()/>
                    </div>
                </div>
                <div class="work-actions" id="work-actions">
                    {can_dispatch.then(|| view! {
                        <button
                            class=if show_decision { "btn" } else { "btn btn-primary" }
                            disabled=move || !context.can_write() || data.dispatch_active
                            on:click=dispatch.clone()
                        >"Start dispatch"</button>
                    })}
                    {has_approve_contract.then(|| view! {
                        <button class="btn btn-primary" disabled=move || !context.can_write() on:click=move |event| open_contract.run(event)>"Approve contract"</button>
                        <button class="btn btn-quiet" disabled=move || !context.can_write() on:click=move |event| open_reject.run(event)>"Reject contract"</button>
                    })}
                    {can_merge.then(|| view! {
                        <button class="btn btn-primary" disabled=move || !context.can_write() on:click=move |event| open_merge.run(event)>"Authorize merge"</button>
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
                <AttentionLine items=attention.clone() decision_waiting=show_decision/>
            })}

            {summary.last_failure.clone().map(|failure| view! {
                <Notice kind="danger">
                    <strong>"Last failure: "</strong>
                    <span class="mono">{failure}</span>
                </Notice>
            })}

            <Tabs
                label="Work item views".to_string()
                tabs=TABS.to_vec()
                selected=tab
                panel_id=TAB_PANEL_ID
            />

            <TabPanel
                panel_id=TAB_PANEL_ID
                labelled_by=Signal::derive(move || format!("{TAB_PANEL_ID}-tab-{}", tab.get()))
            >

            {move || match tab.get() {
                "lifecycle" => view! {
                    <LifecycleTab
                        data=data.clone()
                        contract=tab_contract.clone()
                        serving=tab_serving.clone()
                        instance=instance
                        work_item_id=tab_work_item_id.clone()
                        next_actions=next_actions.clone()
                        is_terminal=is_terminal
                    />
                }.into_any(),
                "agent" => view! {
                    <AgentTab
                        instance=instance.get()
                        work_item_id=tab_work_item_id.clone()
                        dispatch_active=sessions_active
                        attempts=data.attempts.clone()
                    />
                }.into_any(),
                "evidence" => view! {
                    <EvidenceTab data=data.clone()/>
                }.into_any(),
                "source" => view! {
                    <SourceTab instance=instance.get() work_item_id=tab_work_item_id.clone()/>
                }.into_any(),
                "lineage" => view! {
                    <LineageTab instance=instance.get() work_item_id=tab_work_item_id.clone()/>
                }.into_any(),
                _ => view! {
                    <EventsTab events=data.events.clone() instance=instance.get()/>
                }.into_any(),
            }}

            </TabPanel>

            <ActionDialog
                dialog=dialog
                instance=instance
                work_item_id=Signal::derive({
                    let id = work_item_id.clone();
                    move || id.clone()
                })
                work_title=summary.title.clone()
                contract=contract.clone()
                requirement=requirement.clone()
                accepted_review=latest_accepted_review.clone()
            />
        </section>
    }
}

#[component]
fn LifecycleTab(
    data: WorkItemDetail,
    contract: Contract,
    serving: control_plane_domain::Serving,
    instance: Signal<String>,
    work_item_id: String,
    next_actions: Vec<NextAction>,
    is_terminal: bool,
) -> impl IntoView {
    let stages = data.stages;
    let reviews = data.reviews;
    let release = data.release;

    view! {
        <div class="split">
            <div class="stack">
                <Panel title="Workflow".to_string()>
                    <div class="stage-rail">
                        {stages.into_iter().map(|stage| view! { <StageRow stage=stage/> }).collect_view()}
                    </div>
                </Panel>

                <ContractPanel contract=contract/>

                <ReviewMatrix reviews=reviews.clone()/>

                <ReleaseEvidence release=release.clone()/>

                <ServingPanel serving=serving instance=instance.get() work_item_id=work_item_id/>
            </div>

            <div class="stack">
                <Panel title="What the factory allows next".to_string()>
                    <p class="tiny faint">
                        "This is the factory's own transition list. Contract, rejection and merge decisions are made from the action row at the top of this page; starting or stopping a serve is owned by the release page. This list does not perform any of them."
                    </p>
                    {if next_actions.is_empty() {
                        view! { <EmptyBlock title="No action offered" detail="The control plane does not expose a transition the factory has not authorised."/> }.into_any()
                    } else {
                        view! {
                            <div class="stack">
                                {next_actions.into_iter().map(|action| view! {
                                    <div class="action-row">
                                        <div class="row-between">
                                            <strong>{action.label}</strong>
                                            <StatusIndicator label=role_label(&action.required_role).to_string() tone=if action.available { "active" } else { "neutral" } raw=action.required_role.clone()/>
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
                    <StatusIndicator label=stage_state_label(&stage.state).to_string() tone=tone raw=stage.state.clone()/>
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
fn ContractPanel(contract: Contract) -> impl IntoView {
    let scenarios = contract.scenarios.clone();
    let unresolved = contract.unresolved_questions.clone();
    let awaiting = contract.awaiting_approval;
    view! {
        <Panel title="Frozen acceptance contract".to_string()>
            <div class="stack">
                <div class="row wrap">
                    <StatusIndicator
                        label=if contract.is_frozen { "Frozen".to_string() } else if contract.awaiting_approval { "Awaiting approval".to_string() } else { "Open".to_string() }
                        tone=if contract.is_frozen { "success" } else if contract.awaiting_approval { "waiting" } else { "neutral" }
                    />
                    <StatusIndicator label=if contract.executable { "Executable HTTP scenarios".to_string() } else { "Not executable".to_string() } tone=if contract.executable { "active" } else { "danger" }/>
                </div>
                <div class="grid grid-2">
                    <DigestLine label="Proposal digest".to_string() value=contract.proposal_digest.clone()/>
                    <DigestLine label="Frozen digest".to_string() value=contract.frozen_digest.clone()/>
                    <DigestLine label="Accepted contract digest".to_string() value=contract.accepted_contract_digest.clone()/>
                    <DigestLine label="Requirement digest".to_string() value=contract.requirement_digest.clone()/>
                </div>
                <ContractScenarios scenarios=scenarios/>
                {(!unresolved.is_empty()).then(|| view! {
                    <Notice kind="warn">
                        <strong>"Unresolved proposal questions. "</strong>
                        <span>"The factory refuses to freeze an ambiguous proposal; revise the requirement or resolve the observable outcome."</span>
                        <ul>{unresolved.into_iter().map(|question| view! { <li>{question}</li> }).collect_view()}</ul>
                    </Notice>
                })}
                {contract.rejection_reason.clone().map(|reason| view! { <Notice kind="danger"><strong>"Rejected: "</strong>{reason}</Notice> })}
                <div class="stack">
                    {(!contract.executable && contract.is_frozen).then(|| view! {
                    <Notice kind="danger"><strong>"Non-executable frozen contract. "</strong><span>"Requirement coverage and post-merge verification cannot execute this proposal. Submit a revised requirement with an explicit acceptance contract."</span></Notice>
                })}
                {(!contract.executable && !contract.is_frozen).then(|| view! {
                    <Notice kind="warn"><strong>"Not executable. "</strong><span>"This proposal has no executable HTTP scenarios. The control plane will refuse approval because the review oracle could not run; add an acceptance contract and resubmit."</span></Notice>
                })}
                {awaiting.then(|| view! {
                    <p class="small">
                        <a href="#work-actions">"Approve or reject this exact proposal from the action row at the top of the page."</a>
                    </p>
                })}
                </div>
            </div>
        </Panel>
    }
}

/// Scenarios are a definition list, not a four-column table: at 320px a table either clips or
/// wraps every path into a character-per-line column. The list keeps the ID and kind together and
/// gives the path and expectation their own rows.
#[component]
fn ContractScenarios(scenarios: Vec<ContractScenario>) -> impl IntoView {
    if scenarios.is_empty() {
        return view! {
            <p class="small form-error">"No scenario is bound to this proposal."</p>
        }
        .into_any();
    }
    view! {
        <section class="scenario-block" aria-labelledby="contract-scenarios-title">
            <h3 id="contract-scenarios-title">
                {format!("Declared scenarios ({})", scenarios.len())}
            </h3>
            <ul class="scenario-list">
                {scenarios
                    .into_iter()
                    .map(|scenario| view! {
                        <li class="scenario">
                            <div class="scenario-head">
                                <code class="mono">{scenario.id}</code>
                                <span class="scenario-kind">{scenario.kind}</span>
                            </div>
                            <dl class="metadata-list compact">
                                <div>
                                    <dt>"Path"</dt>
                                    <dd class="mono wrap">{scenario.path}</dd>
                                </div>
                                <div>
                                    <dt>"Expectation"</dt>
                                    <dd>{scenario.expectation}</dd>
                                </div>
                            </dl>
                        </li>
                    })
                    .collect_view()}
            </ul>
        </section>
    }
    .into_any()
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
                                        <div class="row-between"><strong>{review_kind_label(kind)}</strong><StatusIndicator label="Not run".to_string() tone="danger"/></div>
                                        <div class="tiny faint">"A check that did not run is never implicitly passing."</div>
                                    </div>
                                }.into_any(),
                                Some(execution) => view! {
                                    <div class=format!("review-cell {}", if execution.passed { "review-pass" } else { "review-fail" })>
                                        <div class="row-between"><strong>{review_kind_label(kind)}</strong><StatusIndicator label=outcome_label(&execution.outcome).to_string() tone=if execution.passed { "success" } else { "danger" } raw=execution.outcome.clone()/></div>
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
                            <StatusIndicator label=if release.release_recorded { "Release recorded".to_string() } else { "Candidate".to_string() } tone=if release.release_recorded { "success" } else { "waiting" }/>
                            {release.verification.as_ref().map(|verification| view! {
                                <StatusIndicator label=if verification.verified { "Post-merge verification passed".to_string() } else { "Post-merge verification failed".to_string() } tone=if verification.verified { "success" } else { "danger" }/>
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
    instance: String,
    work_item_id: String,
) -> impl IntoView {
    let active = serving.active;
    let release_href = format!("/i/{instance}/releases/{work_item_id}");
    let has_release = serving
        .merged_sha
        .as_deref()
        .is_some_and(|value| !value.trim().is_empty())
        || serving.recorded_at_utc.is_some()
        || active;
    view! {
        <Panel title="Supervised serving".to_string()>
            <div class="stack">
                <div class="row wrap">
                    <StatusIndicator label=serving_view(&serving).label.to_string() tone=serving_view(&serving).tone raw=serving_view(&serving).code.to_string()/>
                    {active.then(|| view! { <span class="tiny faint">"Process "{serving.process_id.unwrap_or_default()}" · restarts "{serving.restart_count.unwrap_or_default()}</span> })}
                </div>
                <InfoRow label="Merged revision".to_string()><span class="mono">{short_sha(serving.merged_sha.as_deref())}</span></InfoRow>
                <InfoRow label="Last observation".to_string()>{serving.recorded_at_utc.map(|time| view! { <span>{time_ago(Some(&time.to_rfc3339()))}</span> }.into_any()).unwrap_or_else(|| view! { <span class="faint">"None"</span> }.into_any())}</InfoRow>
                {serving.detail.clone().map(|detail| view! { <div class="tiny faint">{detail}</div> })}
                {active.then(|| serving.url.clone()).flatten().map(|url| view! {
                    <a class="btn" href=url.clone() target="_blank" rel="noopener noreferrer">"Open verified preview"</a>
                })}
                <p class="small muted">
                    "Starting or stopping a supervised serve is owned by the release page, which binds the release identity and its verification evidence. This page reports the current serving state and does not repeat the mutation."
                </p>
                {has_release.then(|| view! {
                    <a class="btn" href=release_href.clone()>
                        {if active { "Stop or inspect serve on the release page" } else { "Start serve on the release page" }}
                    </a>
                })}
                <p class="tiny faint">
                    "A verified release is not automatically a running application. The preview link appears only while the factory owns a reachable supervised process."
                </p>
            </div>
        </Panel>
    }
}

fn severity_rank(severity: &str) -> u8 {
    match severity {
        "critical" => 2,
        "warning" => 1,
        _ => 0,
    }
}

/// One line for every condition the adapter raised. The details stay in the decision dialog and
/// the lifecycle panel; this line answers "does this item need me" without repeating the cards.
#[component]
fn AttentionLine(items: Vec<Attention>, decision_waiting: bool) -> impl IntoView {
    let count = items.len();
    let severity = items
        .iter()
        .map(|item| item.severity.as_str())
        .max_by_key(|severity| severity_rank(severity))
        .unwrap_or("info")
        .to_string();
    let titles = items
        .iter()
        .map(|item| item.title.clone())
        .collect::<Vec<_>>()
        .join(" · ");
    view! {
        <p class="attention-line" role="status">
            <StatusIndicator label=severity_label(&severity).to_string() tone=severity_tone(&severity) raw=severity.clone()/>
            <span class="attention-line-text">
                <strong>{count}</strong>
                {if count == 1 { " condition needs attention: " } else { " conditions need attention: " }}
                {titles}
            </span>
            {decision_waiting.then(|| view! {
                <a class="attention-line-link" href="#work-actions">"Decide at the top of the page"</a>
            })}
        </p>
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
                                            <StatusIndicator label=outcome_label(&attempt.outcome).to_string() tone=if attempt.outcome == "succeeded" { "success" } else { "danger" } raw=attempt.outcome.clone()/>
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
                        Some(Ok(sessions)) => {
                            if sessions.is_empty() {
                                view! { <EmptyBlock title="No model session trace" detail="This run has no durable model session trace. The implementation attempt journal above is still authoritative for execution outcome; deterministic runs do not fabricate model tokens or tool activity."/> }.into_any()
                            } else {
                                view! {
                                    <div class="stack">
                                        {sessions.into_iter().map(|session| view! { <SessionView session=session/> }).collect_view()}
                                    </div>
                                }.into_any()
                            }
                        }
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
                <StatusIndicator label=outcome_label(&session.outcome).to_string() tone=if session.outcome == "completed" { "success" } else { "danger" } raw=session.outcome.clone()/>
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
                                    <StatusIndicator label=if invocation.failed { "Failed".to_string() } else { "Succeeded".to_string() } tone=if invocation.failed { "danger" } else { "success" }/>
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

/// The review's conclusion in operator words and the tone that supports it. Used by both the
/// Evidence tab and the merge dialog so the verdict cannot read differently in two places.
fn review_verdict(review: &ReviewHistory) -> (String, &'static str) {
    match review.outcome.as_str() {
        "accepted" => ("Review accepted".to_string(), "success"),
        "rejected" => ("Review rejected".to_string(), "danger"),
        _ => (outcome_label(&review.outcome).to_string(), "neutral"),
    }
}

fn review_checks(review: &ReviewHistory) -> Vec<EvidenceCheck> {
    review
        .executions
        .iter()
        .map(|execution| {
            let detail = if execution.detail.trim().is_empty() {
                execution.check_id.clone()
            } else {
                format!("{} · {}", execution.check_id, execution.detail)
            };
            EvidenceCheck {
                label: review_kind_label(&execution.kind).to_string(),
                outcome: outcome_label(&execution.outcome).to_string(),
                tone: if execution.passed { "success" } else { "danger" },
                detail: Some(detail),
            }
        })
        .collect()
}

fn review_identity(review: &ReviewHistory) -> Vec<(String, String)> {
    fn value(value: &Option<String>) -> String {
        value
            .clone()
            .filter(|value| !value.trim().is_empty())
            .unwrap_or_else(|| "Not recorded".to_string())
    }
    vec![
        ("Review digest".to_string(), value(&review.review_digest)),
        ("Reviewed head".to_string(), value(&review.head_sha)),
        ("Reviewed base".to_string(), value(&review.base_sha)),
        ("Contract digest".to_string(), value(&review.contract_digest)),
        ("Evidence digest".to_string(), value(&review.evidence_digest)),
    ]
}

#[component]
fn EvidenceTab(data: WorkItemDetail) -> impl IntoView {
    let latest_review = data
        .reviews
        .iter()
        .rev()
        .find(|review| review.outcome == "accepted")
        .or_else(|| data.reviews.last())
        .cloned();
    view! {
        <div class="stack">
            <Panel title="Latest review verdict".to_string()>
                {match latest_review {
                    None => view! {
                        <EmptyBlock title="No review verdict yet" detail="The four mandatory checks and their verdict appear here after the first review."/>
                    }.into_any(),
                    Some(review) => {
                        let (verdict, tone) = review_verdict(&review);
                        view! {
                            <EvidenceSummary
                                verdict=verdict
                                tone=tone
                                identity=review_identity(&review)
                                checks=review_checks(&review)
                                raw=review.checks_summary.clone()
                            />
                        }.into_any()
                    }
                }}
            </Panel>

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

            <Panel title="Review chronology".to_string()>
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
                    <StatusIndicator label=outcome_label(&review.outcome).to_string() tone=if review.outcome == "accepted" { "success" } else { "danger" } raw=review.outcome.clone()/>
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
                <StatusIndicator label=outcome_label(&execution.outcome).to_string() tone=if execution.passed { "success" } else { "danger" } raw=execution.outcome.clone()/>
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
                Some(Err(error)) => {
                    if error.code == "diff-not-available" {
                        view! { <EmptyBlock title="No reviewed revision yet" detail="The diff becomes available after an implementation attempt produces a committed head revision."/> }.into_any()
                    } else {
                        view! { {error_view(error)} }.into_any()
                    }
                }
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
    let patch = diff.patch.clone();
    let base_sha = diff.base_sha.clone();
    let head_sha = diff.head_sha.clone();
    let files = diff.files.clone();
    let truncated = diff.truncated;
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
        <Panel title="Reviewed diff".to_string()>
            <DiffViewer
                base_sha=base_sha
                head_sha=head_sha
                files=files
                truncated=truncated
                selected=selected
            >
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
            </DiffViewer>
        </Panel>
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
                                    <span class="event-type">{event.r#type.clone()}</span>
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

#[component]
fn ActionDialog(
    dialog: RwSignal<Option<Dialog>>,
    instance: Signal<String>,
    work_item_id: Signal<String>,
    work_title: String,
    contract: Contract,
    requirement: Option<control_plane_domain::Requirement>,
    accepted_review: Option<ReviewHistory>,
) -> impl IntoView {
    let context = AppContext::provide();
    let busy = RwSignal::new(false);
    let error = RwSignal::new(None::<String>);
    let confirmed = RwSignal::new(false);
    let typed = RwSignal::new(String::new());
    let reason = RwSignal::new(String::new());

    // Closing is refused while a governed mutation is in flight: the dialog must not disappear
    // before the factory answers, and a user's typed evidence must not be discarded silently.
    let close = Callback::new(move |_| {
        if busy.get() {
            return;
        }
        dialog.set(None);
        error.set(None);
        confirmed.set(false);
        typed.set(String::new());
        reason.set(String::new());
    });

    let submit = Callback::new(move |_| {
        if busy.get() {
            return;
        }
        let Some(current) = dialog.get() else {
            return;
        };
        let instance = instance.get();
        let work_item_id = work_item_id.get();
        let csrf = context.csrf.get();
        let confirmed_value = confirmed.get();
        let typed_value = typed.get();
        let reason_value = reason.get();
        busy.set(true);
        error.set(None);
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
                            confirmation: typed_value,
                        },
                        &api::new_idempotency_key(),
                    )
                    .await
                }
            };

            busy.set(false);
            match result {
                Ok(_) => {
                    context.notify_success(
                        "Command accepted by the factory adapter. The factory owns the next lifecycle step.",
                    );
                    dialog.set(None);
                    error.set(None);
                    confirmed.set(false);
                    typed.set(String::new());
                    reason.set(String::new());
                    context.touch();
                }
                Err(failure) => {
                    // Keep the dialog, its evidence and the user's typed digest; explain the failure.
                    error.set(Some(format!(
                        "{} ({})",
                        failure.message,
                        if failure.code.is_empty() {
                            "no code".to_string()
                        } else {
                            failure.code.clone()
                        }
                    )));
                }
            }
        });
    });

    let failure_notice = move || {
        error.get().map(|message| {
            view! {
                <Notice kind="danger">
                    <strong>"The factory refused or could not complete this decision. "</strong>
                    <span>{message}</span>
                </Notice>
            }
        })
    };

    view! {
        {move || dialog.get().map(|current| {
            let (title, description, body, can_submit, confirm_label) = match &current {
                Dialog::ApproveContract { digest } => {
                    let executable = contract.executable;
                    let proposal_digest = digest.clone();
                    let requirement_digest = contract.requirement_digest.clone();
                    let scenarios = contract.scenarios.clone();
                    let unresolved = contract.unresolved_questions.clone();
                    let requirement = requirement.clone();
                    let title = format!("Approve contract proposal for {work_title}");
                    build_contract_authorization(
                        &title,
                        executable,
                        requirement,
                        scenarios,
                        unresolved,
                        proposal_digest,
                        requirement_digest,
                        confirmed,
                    )
                }
                Dialog::ApproveMerge { digest } => {
                    let expected = digest.clone();
                    let review = accepted_review.clone();
                    let title = format!("Authorize merge for {work_title}");
                    build_merge_authorization(&title, review, expected, confirmed, typed)
                }
                Dialog::RejectContract { digest } => {
                    let proposal_digest = digest.clone();
                    let requirement_digest = contract.requirement_digest.clone();
                    let scenarios = contract.scenarios.clone();
                    let title = format!("Reject contract proposal for {work_title}");
                    let body = view! {
                        <div class="stack decision">
                            {failure_notice.clone()}
                            <Notice kind="warning">
                                <strong>"A rejection is durable. "</strong>
                                <span>"The factory records it against this exact proposal digest and refuses a later approval of the same digest. To proceed, submit a revised requirement."</span>
                            </Notice>
                            <EvidenceSummary
                                verdict="Proposal awaiting a rejection decision".to_string()
                                tone="waiting"
                                identity=vec![
                                    ("Proposal digest".to_string(), proposal_digest.clone()),
                                    (
                                        "Requirement digest".to_string(),
                                        requirement_digest.clone().unwrap_or_else(|| "Not recorded".to_string()),
                                    ),
                                ]
                                checks=scenario_evidence(&scenarios)
                                items_label=Some("scenario".to_string())
                            />
                            <label class="field" for="reject-reason">"Reason (optional)"
                                <textarea
                                    class="textarea"
                                    id="reject-reason"
                                    rows="3"
                                    prop:value=move || reason.get()
                                    on:input=move |event| reason.set(event_target_value(&event))
                                ></textarea>
                            </label>
                            <Confirmation
                                assertion="I understand this rejection is durable and applies to the exact proposal digest above, not to the requirement in general.".to_string()
                                checked=confirmed
                            />
                        </div>
                    }.into_any();
                    (
                        title,
                        Some("This records a durable rejection against the exact digest above.".to_string()),
                        body,
                        Signal::derive(move || confirmed.get()),
                        "Record rejection",
                    )
                }
            };

            let footer = view! {
                <button class="btn" type="button" disabled=move || busy.get() on:click=move |_| close.run(())>
                    "Cancel"
                </button>
                <button
                    class="btn btn-primary"
                    type="button"
                    disabled=move || busy.get() || !can_submit.get()
                    on:click=move |_| submit.run(())
                >
                    {move || if busy.get() { "Submitting…".to_string() } else { confirm_label.to_string() }}
                </button>
            }.into_any();

            view! {
                <Modal
                    title=title
                    description=description
                    on_close=close
                    dismissible=true
                    wide=true
                    footer=footer
                >
                    {body}
                </Modal>
            }
        })}
    }
}

fn scenario_evidence(scenarios: &[ContractScenario]) -> Vec<EvidenceCheck> {
    if scenarios.is_empty() {
        return vec![EvidenceCheck {
            label: "No scenario is bound".to_string(),
            outcome: "Cannot be approved".to_string(),
            tone: "danger",
            detail: Some(
                "The factory cannot execute a proposal without an executable scenario."
                    .to_string(),
            ),
        }];
    }
    scenarios
        .iter()
        .map(|scenario| EvidenceCheck {
            label: scenario.id.clone(),
            outcome: "Declared".to_string(),
            tone: "neutral",
            detail: Some(format!(
                "{} {} — {}",
                scenario.kind, scenario.path, scenario.expectation
            )),
        })
        .collect()
}

fn can_submit_contract(executable: bool, confirmed: RwSignal<bool>) -> Signal<bool> {
    Signal::derive(move || executable && confirmed.get())
}

fn build_contract_authorization(
    title: &str,
    executable: bool,
    requirement: Option<control_plane_domain::Requirement>,
    scenarios: Vec<ContractScenario>,
    unresolved: Vec<String>,
    proposal_digest: String,
    requirement_digest: Option<String>,
    confirmed: RwSignal<bool>,
) -> (String, Option<String>, AnyView, Signal<bool>, &'static str) {
    let scenario_count = scenarios.len();
    let evidence_checks = scenario_evidence(&scenarios);
    let supported_class = requirement
        .as_ref()
        .map(|requirement| requirement.supported_class.clone())
        .unwrap_or_else(|| "Not returned".to_string());
    let body = view! {
        <div class="stack decision">
            {(!executable).then(|| view! {
                <Notice kind="danger">
                    <strong>"This proposal cannot be approved. "</strong>
                    <span>"It has no executable HTTP scenarios, so requirement coverage and post-merge verification cannot run. The approval action is disabled; submit a revised requirement with an explicit acceptance contract."</span>
                </Notice>
            })}
            <EvidenceSummary
                verdict=if executable { "Proposal ready for a freezing decision".to_string() } else { "Proposal cannot be executed".to_string() }
                tone=if executable { "waiting" } else { "danger" }
                identity=vec![
                    ("Proposal digest".to_string(), proposal_digest.clone()),
                    (
                        "Requirement digest".to_string(),
                        requirement_digest.clone().unwrap_or_else(|| "Not recorded".to_string()),
                    ),
                    ("Supported class".to_string(), supported_class),
                ]
                checks=evidence_checks
                items_label=Some("scenario".to_string())
            />
            <section class="decision-section">
                <h3>"What you are freezing"</h3>
                {match requirement {
                    None => view! { <p class="small form-error">"The adapter did not return the requirement this proposal was derived from. Approval stays blocked until the proposal contents can be shown."</p> }.into_any(),
                    Some(requirement) => view! {
                        <div class="stack">
                            <dl class="metadata-list">
                                <div><dt>"Title"</dt><dd>{requirement.title.clone()}</dd></div>
                            </dl>
                            <div>
                                <div class="label">"Requirement text"</div>
                                <pre class="requirement-text">{requirement.text.clone()}</pre>
                            </div>
                            {(!requirement.expectations.is_empty()).then(|| view! {
                                <div>
                                    <div class="label">"Acceptance expectations"</div>
                                    <ul class="decision-list">
                                        {requirement.expectations.clone().into_iter().map(|expectation| view! { <li>{expectation}</li> }).collect_view()}
                                    </ul>
                                </div>
                            })}
                        </div>
                    }.into_any(),
                }}
            </section>
            <section class="decision-section">
                <h3>"Unresolved questions"</h3>
                {if unresolved.is_empty() {
                    view! { <p class="small muted">"The adapter recorded no unresolved question for this proposal."</p> }.into_any()
                } else {
                    view! {
                        <ul class="decision-list warning-list">
                            {unresolved.into_iter().map(|question| view! { <li>{question}</li> }).collect_view()}
                        </ul>
                    }.into_any()
                }}
            </section>
            <section class="decision-section">
                <h3>"Effect and resulting state"</h3>
                <ul class="decision-list">
                    <li>"The contract becomes immutable evidence bound to the digest below; the factory refuses any later approval for a different digest."</li>
                    <li>"The work item becomes eligible for dispatch. Approval alone does not advance the factory state, start implementation, merge or serve."</li>
                    <li>"Independent acceptance is evaluator-owned and is never implied by this approval."</li>
                </ul>
            </section>
            <DigestCopy label="Exact proposal digest being approved".to_string() value=Some(proposal_digest.clone())/>
            <Confirmation
                assertion=format!(
                    "I reviewed the requirement text, the {} declared scenario(s), the unresolved questions and the effect shown above, and I am freezing the exact proposal digest shown.",
                    scenario_count
                )
                checked=confirmed
            />
        </div>
    }.into_any();

    (
        title.to_string(),
        Some("Every fact below belongs to the proposal this digest identifies.".to_string()),
        body,
        can_submit_contract(executable, confirmed),
        "Freeze this exact contract",
    )
}

fn build_merge_authorization(
    title: &str,
    review: Option<ReviewHistory>,
    expected_digest: String,
    confirmed: RwSignal<bool>,
    typed: RwSignal<String>,
) -> (String, Option<String>, AnyView, Signal<bool>, &'static str) {
    let can_submit = Signal::derive({
        let expected = expected_digest.clone();
        move || confirmed.get() && typed_confirmation_matches(&expected, &typed.get())
    });
    let body = view! {
        <div class="stack decision">
            {match review {
                None => view! {
                    <Notice kind="danger">
                        <strong>"No accepted review evidence is loaded. "</strong>
                        <span>"The merge cannot be authorized from this screen until the accepted review, its four checks and its exact digest are available."</span>
                    </Notice>
                }.into_any(),
                Some(review) => {
                    let (verdict, tone) = review_verdict(&review);
                    let recorded = time_ago(review.recorded_at_utc.as_deref());
                    let check_count = review.executions.len();
                    view! {
                        <div class="stack">
                            <EvidenceSummary
                                verdict=verdict
                                tone=tone
                                identity=review_identity(&review)
                                checks=review_checks(&review)
                                raw=review.checks_summary.clone()
                            />
                            <p class="tiny faint">
                                "Recorded "
                                {recorded}
                                " · "
                                {check_count}
                                " check(s) executed."
                            </p>
                            {review.reason.clone().map(|reason| view! {
                                <section class="decision-section">
                                    <h3>"Reviewer note"</h3>
                                    <p class="small muted">{reason}</p>
                                </section>
                            })}
                            {review.executions.is_empty().then(|| view! {
                                <Notice kind="danger">
                                    <strong>"No check execution is recorded. "</strong>
                                    <span>"This is not sufficient evidence to authorize a merge."</span>
                                </Notice>
                            })}
                        </div>
                    }.into_any()
                }
            }}
            <section class="decision-section">
                <h3>"Binding conditions"</h3>
                <ul class="decision-list">
                    <li>"The adapter revalidates this exact review digest and its evidence before it merges; any change is refused with a typed conflict."</li>
                    <li>"The merge binds the reviewed head, its base and the contract digest shown above."</li>
                    <li>"Post-merge verification runs on the merged revision afterwards; a merge alone never proves the tree works."</li>
                </ul>
            </section>
            <section class="decision-section">
                <h3>"Consequence of authorization"</h3>
                <ul class="decision-list">
                    <li>"The factory merges the reviewed head into the target branch and records the merge."</li>
                    <li>"It does not certify independent acceptance, and it does not start the application or serve a preview."</li>
                </ul>
            </section>
            <DigestCopy label="Exact review digest being authorized".to_string() value=Some(expected_digest.clone())/>
            <Confirmation
                assertion="I reviewed the accepted verdict, its checks and the exact identities shown above, and I authorize merging this reviewed head.".to_string()
                checked=confirmed
                typed=Some(TypedConfirmation {
                    label: "Type the exact review digest above to confirm".to_string(),
                    expected: expected_digest.clone(),
                    value: typed,
                })
            />
        </div>
    }.into_any();

    (
        title.to_string(),
        Some("This authorizes a merge; it does not certify independent acceptance.".to_string()),
        body,
        can_submit,
        "Authorize merge",
    )
}