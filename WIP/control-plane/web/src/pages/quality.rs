use leptos::prelude::*;
use leptos::task::spawn_local;
use leptos_router::hooks::use_params_map;

use control_plane_domain::{ApprovalItem, WorkReview, outcome_label, short_digest};

use crate::api;
use crate::components::status::StatusIndicator;
use crate::components::ui::{
    DigestCopy, DigestLine, EmptyBlock, LoadingBlock, Notice, Panel, error_view, time_ago,
};
use crate::components::Confirmation;
use crate::context::AppContext;

#[component]
pub fn QualityPage() -> impl IntoView {
    let context = AppContext::provide();
    let params = use_params_map();
    let instance = Signal::derive(move || params.get().get("instance").unwrap_or_default());
    let refresh = context.refresh;

    let approvals = LocalResource::new(move || {
        let instance = instance.get();
        let _ = refresh.get();
        async move {
            if !api::is_client() || instance.is_empty() {
                None
            } else {
                Some(api::approvals(&instance).await)
            }
        }
    });
    let reviews = LocalResource::new(move || {
        let instance = instance.get();
        let _ = refresh.get();
        async move {
            if !api::is_client() || instance.is_empty() {
                None
            } else {
                Some(api::reviews(&instance).await)
            }
        }
    });

    view! {
        <section class="page">
            <header class="page-header">
                <div>
                    <h1 class="page-title">Quality & approvals</h1>
                    <p class="page-subtitle">
                        "Engineering evidence, exact-digest authorizations and the separation between contract approval, merge approval and independent acceptance."
                    </p>
                </div>
                <div class="page-actions">
                    <button class="btn" on:click=move |_| context.touch()>"Refresh"</button>
                </div>
            </header>

            <Transition fallback=move || view! { <LoadingBlock label="Loading approvals and engineering evidence…"/> }>
                {move || match approvals.get().flatten() {
                    None => view! { <LoadingBlock label="Loading approvals and engineering evidence…"/> }.into_any(),
                    Some(Err(error)) => view! { {error_view(error)} }.into_any(),
                    Some(Ok(inbox)) => view! {
                        <div class="metric-grid">
                            <div class="metric"><div class="metric-label">"Contract approvals"</div><div class="metric-value">{inbox.contract_count}</div></div>
                            <div class="metric"><div class="metric-label">"Merge authorizations"</div><div class="metric-value">{inbox.merge_count}</div></div>
                            <div class="metric"><div class="metric-label">"Template policies"</div><div class="metric-value">{inbox.template_count}</div></div>
                        </div>

                        <div class="stack">
                            <h2 class="section-title">"Approval inbox"</h2>
                            {if inbox.items.is_empty() {
                                view! { <EmptyBlock title="No pending approvals" detail="Contract proposals, merge authorizations and template policies appear here with their exact digest and effect."/> }.into_any()
                            } else {
                                view! { <div class="stack">{inbox.items.into_iter().map(|item| view! { <ApprovalCard item=item instance=instance.get()/> }).collect_view()}</div> }.into_any()
                            }}
                        </div>
                    }.into_any(),
                }}
            </Transition>

            <Transition fallback=move || view! { <LoadingBlock label="Loading review evidence…"/> }>
                {move || match reviews.get().flatten() {
                    None => view! { <LoadingBlock label="Loading review evidence…"/> }.into_any(),
                    Some(Err(error)) => view! { {error_view(error)} }.into_any(),
                    Some(Ok(reviews)) => view! {
                        <Panel title="Review evidence matrix".to_string()>
                            {if reviews.is_empty() {
                                view! { <EmptyBlock title="No review evidence" detail="The factory records the four checks only after an implementation reaches review."/> }.into_any()
                            } else {
                                view! { <ReviewTable reviews=reviews instance=instance.get()/> }.into_any()
                            }}
                        </Panel>
                    }.into_any(),
                }}
            </Transition>

            <Notice kind="accent">
                <strong>"Acceptance boundary. "</strong>
                <span>"Engineering status comes from merged-revision build, regression and contract evidence. Independent acceptance belongs to the evaluator and its candidate-bound evidence; this screen never infers it from local tests."</span>
            </Notice>
        </section>
    }
}

#[component]
fn ApprovalCard(item: ApprovalItem, instance: String) -> impl IntoView {
    let context = AppContext::provide();
    let busy = RwSignal::new(false);
    let confirmed = RwSignal::new(false);
    let typed = RwSignal::new(String::new());
    let reason = RwSignal::new(String::new());
    let stale = item.stale;
    let digest = item.digest.clone();
    let work_item_id = item.work_item_id.clone();
    let kind = item.kind.clone();
    let authorize_href = format!(
        "/i/{instance}/work/{work_item_id}{}",
        if kind == "merge" { "?authorize=merge" } else { "" }
    );
    let template_identity = item
        .source_revision
        .clone()
        .unwrap_or_else(|| item.title.clone());

    let approve = {
        let instance = instance.clone();
        let digest = digest.clone();
        let work_item_id = work_item_id.clone();
        let kind = kind.clone();
        let template_identity = template_identity.clone();
        move |_| {
            let instance = instance.clone();
            let csrf = context.csrf.get();
            let key = api::new_idempotency_key();
            let digest = digest.clone();
            let work_item_id = work_item_id.clone();
            let kind = kind.clone();
            let template_identity = template_identity.clone();
            let confirmed_value = confirmed.get();
            let typed_value = typed.get();
            if kind == "merge" && typed_value.trim() != digest.trim() {
                context.notify_error(api::UiError::new(
                    0,
                    "confirmation-mismatch",
                    "Type the exact review digest before authorizing the merge.",
                ));
                return;
            }
            if kind != "merge" && !confirmed_value {
                context.notify_error(api::UiError::new(
                    0,
                    "confirmation-required",
                    "Confirm that you inspected the exact object and digest.",
                ));
                return;
            }

            busy.set(true);
            spawn_local(async move {
                let result = match kind.as_str() {
                    "contract" => api::approve_contract(
                        &instance,
                        &csrf,
                        &work_item_id,
                        &control_plane_domain::ContractApproveRequest {
                            proposal_digest: digest.clone(),
                            confirmed: true,
                        },
                        &key,
                    )
                    .await
                    .map(|_| ()),
                    "merge" => api::approve_merge(
                        &instance,
                        &csrf,
                        &work_item_id,
                        &control_plane_domain::MergeApprovalRequest {
                            review_digest: digest.clone(),
                            confirmation: typed_value,
                        },
                        &key,
                    )
                    .await
                    .map(|_| ()),
                    "template" => {
                        let (id, version) = template_identity
                            .split_once('@')
                            .and_then(|(id, version)| {
                                version
                                    .parse::<i32>()
                                    .ok()
                                    .map(|version| (id.to_string(), version))
                            })
                            .unwrap_or_else(|| (template_identity.clone(), 1));
                        api::approve_template(&instance, &csrf, &id, version, &digest, confirmed.get())
                            .await
                            .map(|_| ())
                    }
                    _ => {
                        busy.set(false);
                        context.notify_error(api::UiError::new(
                            0,
                            "unsupported-approval",
                            "This approval kind is not supported by this control-plane build.",
                        ));
                        return;
                    }
                };

                busy.set(false);
                match result {
                    Ok(_) => {
                        context.notify_success(
                            "Authorization recorded. The factory now owns the next lifecycle step.",
                        );
                        context.touch();
                    }
                    Err(error) => context.notify_error(error),
                }
            });
        }
    };

    let reject = {
        let instance = instance.clone();
        let digest = digest.clone();
        let work_item_id = work_item_id.clone();
        move |_: leptos::ev::MouseEvent| {
            let instance = instance.clone();
            let csrf = context.csrf.get();
            let digest = digest.clone();
            let work_item_id = work_item_id.clone();
            let reason_value = reason.get();
            busy.set(true);
            spawn_local(async move {
                let result = api::reject_contract(
                    &instance,
                    &csrf,
                    &work_item_id,
                    &control_plane_domain::ContractRejectRequest {
                        proposal_digest: digest,
                        reason: (!reason_value.trim().is_empty()).then_some(reason_value),
                    },
                    &api::new_idempotency_key(),
                )
                .await;
                busy.set(false);
                match result {
                    Ok(_) => {
                        context
                            .notify_success("Contract proposal rejected; the decision is durable.");
                        context.touch();
                    }
                    Err(error) => context.notify_error(error),
                }
            });
        }
    };

    view! {
        <article class=format!("approval-card {}", if stale { "approval-stale" } else { "" })>
            <div class="row-between">
                <div class="row wrap">
                    <StatusIndicator label=match item.kind.as_str() { "merge" => "Merge authorization", "contract" => "Contract approval", "template" => "Template policy", _ => "Approval" }.to_string() tone=match item.kind.as_str() { "merge" => "danger", "contract" => "waiting", _ => "info" } raw=item.kind.clone()/>
                    <strong>{item.title}</strong>
                    {(!item.work_item_id.is_empty()).then(|| { let id = item.work_item_id.clone(); let id_for_href = id.clone(); view! { <a class="tiny mono" href=format!("/i/{instance}/work/{id_for_href}")>{id}</a> } })}
                </div>
                <span class="tiny faint">{time_ago(item.created_at_utc.as_deref())}</span>
            </div>
            <p class="small muted">{item.summary}</p>
            <DigestCopy label="Exact object digest".to_string() value=Some(item.digest.clone())/>
            {item.source_revision.clone().map(|revision| view! { <DigestLine label="Source revision".to_string() value=Some(revision)/> })}
            <div class="approval-details">
                <div>
                    <div class="label">"Binding conditions"</div>
                    <ul class="tiny">{item.binding_conditions.iter().map(|condition| view! { <li>{condition.clone()}</li> }).collect_view()}</ul>
                </div>
                <div>
                    <div class="label">"Effect of approval"</div>
                    <ul class="tiny">{item.effect_of_approval.iter().map(|effect| view! { <li>{effect.clone()}</li> }).collect_view()}</ul>
                </div>
            </div>
            {stale.then(|| view! {
                <Notice kind="danger"><strong>"Stale authorization. "</strong><span>{item.stale_reason.clone().unwrap_or_else(|| "The object changed since this approval was prepared.".to_string())}</span></Notice>
            })}
            {(!stale).then(move || view! {
                <div class="stack">
                    {if kind == "template" {
                        view! {
                            <div class="stack">
                                <Confirmation assertion="I inspected the immutable template manifest digest and its parameter schema.".to_string() checked=confirmed/>
                                <div class="row">
                                    <button class="btn btn-primary" disabled=move || busy.get() || !confirmed.get() on:click=approve.clone()>"Approve template policy"</button>
                                </div>
                            </div>
                        }.into_any()
                    } else {
                        view! {
                            <div class="stack">
                                <p class="small muted">
                                    "Contract and merge authorization happen on the work item, where the exact object, its evidence, the binding conditions and the consequence are shown together. This inbox never asks you to confirm a digest it has not displayed in full."
                                </p>
                                <a class="btn" href=authorize_href.clone()>"Review and authorize on the work item"</a>
                            </div>
                        }.into_any()
                    }}
                </div>
            })}
        </article>
    }
}

#[component]
fn ReviewTable(reviews: Vec<WorkReview>, instance: String) -> impl IntoView {
    view! {
        <div class="table-wrap">
            <table class="data">
                <thead>
                    <tr>
                        <th>"Work item"</th>
                        <th>"Verdict"</th>
                        <th>"Validation"</th>
                        <th>"Coverage"</th>
                        <th>"Architecture"</th>
                        <th>"Security"</th>
                        <th>"Reviewed SHA"</th>
                        <th>"Recorded"</th>
                    </tr>
                </thead>
                <tbody>
                    {reviews.into_iter().map(|review| {
                        let href = format!("/i/{instance}/work/{}", review.work_item_id);
                        let title = review.title.clone();
                        let verdict = review.verdict.clone();
                        let verdict_tone = if review.verdict == "accepted" { "success" } else { "danger" };
                        let sha = short_digest(review.reviewed_sha.as_deref());
                        let recorded = time_ago(review.recorded_at_utc.as_deref());
                        let validation = check_cell(&review, "Validation");
                        let coverage = check_cell(&review, "RequirementCoverage");
                        let architecture = check_cell(&review, "ArchitecturalConformance");
                        let security = check_cell(&review, "Security");
                        view! {
                            <tr>
                                <td><a href=href>{title}</a><div class="tiny faint mono">{review.work_item_id.clone()}</div></td>
                                <td><StatusIndicator label=outcome_label(&verdict).to_string() tone=verdict_tone raw=verdict.clone()/></td>
                                <td>{validation}</td>
                                <td>{coverage}</td>
                                <td>{architecture}</td>
                                <td>{security}</td>
                                <td class="mono tiny">{sha}</td>
                                <td class="tiny">{recorded}</td>
                            </tr>
                        }
                    }).collect_view()}
                </tbody>
            </table>
        </div>
    }
}

fn check_cell(review: &WorkReview, kind: &str) -> AnyView {
    match review.executions.iter().find(|execution| execution.kind == kind) {
        Some(execution) => view! {
            <StatusIndicator label=if execution.passed { "Passed".to_string() } else { "Failed".to_string() } tone=if execution.passed { "success" } else { "danger" }/>
        }.into_any(),
        None => view! { <StatusIndicator label="Not measured".to_string() tone="danger"/> }.into_any(),
    }
}
