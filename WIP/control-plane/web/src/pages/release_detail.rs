use leptos::prelude::*;
use leptos::task::spawn_local;
use leptos_router::hooks::use_params_map;

use control_plane_domain::{short_digest, short_sha};

use crate::api;
use crate::components::ui::{
    Chip, DigestLine, EmptyBlock, InfoRow, LoadingBlock, Notice, Panel, error_view, time_ago,
    timestamp,
};
use crate::context::AppContext;

#[component]
pub fn ReleaseDetailPage() -> impl IntoView {
    let context = AppContext::provide();
    let params = use_params_map();
    let instance = Signal::derive(move || params.get().get("instance").unwrap_or_default());
    let work_item_id = Signal::derive(move || params.get().get("work_item_id").unwrap_or_default());
    let refresh = context.refresh;

    let release = LocalResource::new(move || {
        let instance = instance.get();
        let work_item_id = work_item_id.get();
        let _ = refresh.get();
        async move {
            if !api::is_client() || instance.is_empty() || work_item_id.is_empty() {
                None
            } else {
                Some(api::release(&instance, &work_item_id).await)
            }
        }
    });
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

    let start_serve = {
        let context = context;
        let instance = instance;
        let work_item_id = work_item_id;
        move |_| {
            let instance = instance.get();
            let work_item_id = work_item_id.get();
            let csrf = context.csrf.get();
            spawn_local(async move {
                match api::start_serve(&instance, &csrf, &work_item_id, 60).await {
                    Ok(_) => {
                        context.notify_success("Supervised serve window accepted. The preview appears when the factory proves a reachable process.");
                        context.touch();
                    }
                    Err(error) => context.notify_error(error),
                }
            });
        }
    };
    let stop_serve = {
        let context = context;
        let instance = instance;
        let work_item_id = work_item_id;
        move |_| {
            let instance = instance.get();
            let work_item_id = work_item_id.get();
            let csrf = context.csrf.get();
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
    };

    view! {
        <section class="page">
            <div class="row wrap">
                <a class="tiny" href=format!("/i/{}/releases", instance.get())>"← Releases"</a>
                <a class="tiny" href=format!("/i/{}/work/{}", instance.get(), work_item_id.get())>"Work detail"</a>
            </div>
            <Suspense fallback=move || view! { <LoadingBlock label="Loading release identity and evidence…"/> }>
                {move || match release.get().flatten() {
                    None => view! { <LoadingBlock label="Loading release identity and evidence…"/> }.into_any(),
                    Some(Err(error)) => view! {
                        if error.code == "release-not-available" {
                            view! { <EmptyBlock title="No release yet" detail="This work item has no accepted review or recorded release. Inspect its lifecycle to see what remains."/> }.into_any()
                        } else {
                            view! { {error_view(error)} }.into_any()
                        }
                    }.into_any(),
                    Some(Ok(release)) => {
                        let title = detail.get().flatten().and_then(|result| result.ok()).map(|detail| detail.summary.title).unwrap_or_else(|| release.work_item_id.clone());
                        let verification = release.verification.clone();
                        let serving = release.serving.clone();
                        let active = serving.active;
                        let verified = verification.as_ref().map(|verification| verification.verified).unwrap_or(false);
                        let start_serve = start_serve.clone();
                        let stop_serve = stop_serve.clone();
                        view! {
                            <header class="page-header">
                                <div>
                                    <h1 class="page-title">{title}</h1>
                                    <div class="row wrap">
                                        <Chip label=format!("STATE {}", release.work_item_id) tone="neutral"/>
                                        <Chip label=if verified { "VERIFIED RELEASE".to_string() } else { "UNVERIFIED".to_string() } tone=if verified { "success" } else { "danger" }/>
                                        <Chip label=if active { "SERVING".to_string() } else { "NOT SERVING".to_string() } tone=if active { "success" } else { "neutral" }/>
                                    </div>
                                </div>
                                <div class="page-actions">
                                    {if active {
                                        view! { <button class="btn btn-danger" disabled=move || !context.can_write() on:click=stop_serve.clone()>"Stop serve"</button> }.into_any()
                                    } else {
                                        view! { <button class="btn btn-primary" disabled=move || !context.can_write() on:click=start_serve.clone()>"Start 60s serve"</button> }.into_any()
                                    }}
                                </div>
                            </header>

                            <div class="release-ladder">
                                <div class="release-step">
                                    <div class="label">"Reviewed candidate"</div>
                                    <div class="mono">{short_sha(release.approved_head_sha.as_deref())}</div>
                                    <div class="tiny faint">"A head revision accepted by the four review checks. Not merged."</div>
                                </div>
                                <div class="release-arrow">"→"</div>
                                <div class="release-step">
                                    <div class="label">"Merged revision"</div>
                                    <div class="mono">{short_sha(release.merged_sha.as_deref())}</div>
                                    <div class="tiny faint">"Git changed. This alone does not prove the merged tree works."</div>
                                </div>
                                <div class="release-arrow">"→"</div>
                                <div class="release-step">
                                    <div class="label">"Verified release"</div>
                                    <div class="mono">{short_digest(verification.as_ref().map(|verification| verification.record_digest.as_str()))}</div>
                                    <div class="tiny faint">"Build, existing regressions and the frozen contract passed on the merged revision."</div>
                                </div>
                                <div class="release-arrow">"→"</div>
                                <div class="release-step">
                                    <div class="label">"Serving application"</div>
                                    <div class="mono">{if active { format!("pid {}", serving.process_id.unwrap_or_default()) } else { "not running".to_string() }}</div>
                                    <div class="tiny faint">"Only an active supervised process can be opened."</div>
                                </div>
                            </div>

                            <div class="split">
                                <div class="stack">
                                    <Panel title="Release verification".to_string()>
                                        {match verification {
                                            None => view! { <EmptyBlock title="No post-merge verification" detail="This candidate has not recorded a merged-revision verification."/> }.into_any(),
                                            Some(verification) => view! {
                                                <div class="stack">
                                                    <div class="row wrap">
                                                        <Chip label=verification.outcome.to_uppercase() tone=if verification.verified { "success" } else { "danger" }/>
                                                        <span class="tiny mono">{verification.verifier_version.clone()}</span>
                                                        <span class="tiny faint">{time_ago(Some(&verification.verified_at_utc.to_rfc3339()))}</span>
                                                    </div>
                                                    <p class="small muted">{verification.detail.clone()}</p>
                                                    <div class="grid grid-2">
                                                        <DigestLine label="Merged revision".to_string() value=Some(verification.merged_sha.clone())/>
                                                        <DigestLine label="Merged tree".to_string() value=verification.merged_tree_sha.clone()/>
                                                        <DigestLine label="Build evidence".to_string() value=Some(verification.build_evidence_digest.clone())/>
                                                        <DigestLine label="Regression evidence".to_string() value=Some(verification.regression_evidence_digest.clone())/>
                                                        <DigestLine label="Contract evidence".to_string() value=Some(verification.contract_evidence_digest.clone())/>
                                                        <DigestLine label="Record digest".to_string() value=Some(verification.record_digest.clone())/>
                                                    </div>
                                                    <div class="tiny mono">{verification.scenario_summary.clone()}</div>
                                                    <InfoRow label="Verified at".to_string()>{timestamp(Some(&verification.verified_at_utc.to_rfc3339()))}</InfoRow>
                                                </div>
                                            }.into_any(),
                                        }}
                                    </Panel>

                                    <Panel title="What the factory actually did".to_string()>
                                        <div class="stack">
                                            <div class="tiny faint">"This page reads the durable ReleaseCompleted event and its release-verification record. It does not infer a running application from a merge."</div>
                                            <div class="grid grid-2">
                                                <DigestLine label="Merged SHA".to_string() value=release.merged_sha.clone()/>
                                                <DigestLine label="Contract digest".to_string() value=release.contract_digest.clone()/>
                                                <DigestLine label="Operation".to_string() value=release.operation_id.clone()/>
                                                <DigestLine label="Reconciliation".to_string() value=release.mutation_mode.clone()/>
                                            </div>
                                        </div>
                                    </Panel>
                                </div>

                                <div class="stack">
                                    <Panel title="Supervised process".to_string()>
                                        <div class="stack">
                                            <div class="row-between"><span class="muted">"Status"</span><Chip label=serving.status.clone().unwrap_or_else(|| "not-served".to_string()).to_uppercase() tone=if active { "success" } else { "neutral" }/></div>
                                            <InfoRow label="Process".to_string()>{serving.process_id.map(|id| id.to_string()).unwrap_or_else(|| "—".to_string())}</InfoRow>
                                            <InfoRow label="Restarts".to_string()>{serving.restart_count.unwrap_or_default().to_string()}</InfoRow>
                                            <InfoRow label="Last probe".to_string()>{serving.recorded_at_utc.map(|time| time_ago(Some(&time.to_rfc3339()))).unwrap_or_else(|| "None".to_string())}</InfoRow>
                                            {serving.detail.clone().map(|detail| view! { <div class="tiny faint">{detail}</div> })}
                                            {active.then(|| serving.url.clone().map(|url| view! {
                                                <a class="btn btn-primary" href=url target="_blank" rel="noopener noreferrer">"Open verified preview"</a>
                                            }))}
                                            {(!active).then(|| view! {
                                                <Notice kind="accent">"No preview link is shown because no reachable verified serve window is active."</Notice>
                                            })}
                                        </div>
                                    </Panel>

                                    <Panel title="Boundaries".to_string()>
                                        <ul class="small muted">
                                            <li>"Merge authorization is bound to the exact review digest."</li>
                                            <li>"Post-merge verification is bound to the merged revision, base and contract."</li>
                                            <li>"Independent acceptance is evaluator-owned and is never inferred here."</li>
                                            <li>"Restart, stop and rollback remain subject to factory ownership and compensation policy."</li>
                                        </ul>
                                    </Panel>
                                </div>
                            </div>
                        }.into_any()
                    }
                }}
            </Suspense>
        </section>
    }
}
