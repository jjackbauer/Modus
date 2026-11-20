use leptos::prelude::*;
use leptos::task::spawn_local;
use leptos_router::hooks::use_params_map;

use control_plane_domain::{
    Release, Serving, ServingView, WorkItemDetail, outcome_label, serving_view, short_digest,
    short_sha, state_label, state_tone,
};

use crate::api;
use crate::components::modal::Modal;
use crate::components::status::StatusIndicator;
use crate::components::ui::{
    DigestLine, EmptyBlock, InfoRow, LoadingBlock, Notice, Panel, error_view, time_ago, timestamp,
};
use crate::components::Confirmation;
use crate::context::AppContext;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum ServeIntent {
    Start,
    Stop,
}

#[component]
pub fn ReleaseDetailPage() -> impl IntoView {
    let context = AppContext::provide();
    let params = use_params_map();
    let instance = Signal::derive(move || params.get().get("instance").unwrap_or_default());
    let work_item_id = Signal::derive(move || params.get().get("work_item_id").unwrap_or_default());
    let refresh = context.refresh;
    let confirm = RwSignal::new(None::<ServeIntent>);
    let release_refresh_error = RwSignal::new(None::<api::UiError>);
    let release_last_good = RwSignal::new(None::<(String, Release)>);
    let detail_refresh_error = RwSignal::new(None::<api::UiError>);
    let detail_last_good = RwSignal::new(None::<(String, WorkItemDetail)>);

    let release = LocalResource::new(move || {
        let instance = instance.get();
        let work_item_id = work_item_id.get();
        let request_key = work_item_id.clone();
        let _ = refresh.get();
        let error_slot = release_refresh_error;
        let previous_slot = release_last_good;
        async move {
            if !api::is_client() || instance.is_empty() || work_item_id.is_empty() {
                None
            } else {
                match api::release(&instance, &work_item_id).await {
                    Ok(release) => {
                        error_slot.set(None);
                        previous_slot.set(Some((request_key.clone(), release.clone())));
                        Some(Ok(release))
                    }
                    Err(error) => {
                        error_slot.set(Some(error.clone()));
                        match previous_slot.get_untracked() {
                            Some((key, previous)) if key == request_key => Some(Ok(previous)),
                            _ => Some(Err(error)),
                        }
                    }
                }
            }
        }
    });
    let detail = LocalResource::new(move || {
        let instance = instance.get();
        let work_item_id = work_item_id.get();
        let request_key = work_item_id.clone();
        let _ = refresh.get();
        let error_slot = detail_refresh_error;
        let previous_slot = detail_last_good;
        async move {
            if !api::is_client() || instance.is_empty() || work_item_id.is_empty() {
                None
            } else {
                match api::work_item(&instance, &work_item_id).await {
                    Ok(detail) => {
                        error_slot.set(None);
                        previous_slot.set(Some((request_key.clone(), detail.clone())));
                        Some(Ok(detail))
                    }
                    Err(error) => {
                        error_slot.set(Some(error.clone()));
                        match previous_slot.get_untracked() {
                            Some((key, previous)) if key == request_key => Some(Ok(previous)),
                            _ => Some(Err(error)),
                        }
                    }
                }
            }
        }
    });

    view! {
        <section class="page">
            <div class="row wrap">
                <a class="tiny" href=format!("/i/{}/work/{}", instance.get(), work_item_id.get())>"Work detail"</a>
            </div>
            <Transition fallback=move || view! { <LoadingBlock label="Loading release identity and evidence…"/> }>
                {move || match release.get().flatten() {
                    None => view! { <LoadingBlock label="Loading release identity and evidence…"/> }.into_any(),
                    Some(Err(error)) => {
                        if error.code == "release-not-available" {
                            view! { <EmptyBlock title="No release yet" detail="This work item has no accepted review or recorded release. Inspect its lifecycle to see what remains."/> }.into_any()
                        } else {
                            view! { {error_view(error)} }.into_any()
                        }
                    }
                    Some(Ok(release)) => {
                        let work = detail.get().flatten().and_then(|result| result.ok());
                        let title = work
                            .as_ref()
                            .map(|detail| detail.summary.title.clone())
                            .unwrap_or_else(|| release.work_item_id.clone());
                        let state = work
                            .as_ref()
                            .map(|detail| detail.summary.state.clone())
                            .unwrap_or_default();
                        let verification = release.verification.clone();
                        let serving = release.serving.clone();
                        let view_model = serving_view(&serving);
                        let active = serving.active;
                        let verified = verification
                            .as_ref()
                            .map(|verification| verification.verified)
                            .unwrap_or(false);
                        let serving_for_dialog = serving.clone();
                        let instance_value = instance.get();
                        let work_item_value = work_item_id.get();

                        view! {
                            {move || {
                                let error = release_refresh_error.get().or_else(|| detail_refresh_error.get());
                                error.map(|error| view! {
                                    <Notice kind="danger">
                                        <div role="alert" class="row wrap">
                                            <strong>"The last refresh failed. "</strong>
                                            <span>{error.message}</span>
                                            <span class="tiny mono">{format!("{} · {}", error.status, error.code)}</span>
                                            <button class="btn btn-sm" on:click=move |_| context.touch()>"Retry refresh"</button>
                                        </div>
                                    </Notice>
                                })
                            }}
                            <header class="page-header">
                                <div>
                                    <h1 class="page-title">{title}</h1>
                                    <div class="row wrap">
                                        <StatusIndicator label=state_label(&state).to_string() tone=state_tone(&state) raw=state.clone()/>
                                        <StatusIndicator
                                            label=if verified { "Release verified".to_string() } else { "Not verified".to_string() }
                                            tone=if verified { "success" } else { "waiting" }
                                        />
                                        <StatusIndicator label=view_model.label.to_string() tone=view_model.tone raw=serving.status.clone().unwrap_or_default()/>
                                        <span class="tiny faint mono">{release.work_item_id.clone()}</span>
                                    </div>
                                </div>
                                <div class="page-actions">
                                    {if active {
                                        view! {
                                            <button
                                                class="btn btn-danger"
                                                type="button"
                                                disabled=move || !context.can_write()
                                                on:click=move |_| confirm.set(Some(ServeIntent::Stop))
                                            >
                                                "Stop serve"
                                            </button>
                                        }.into_any()
                                    } else {
                                        view! {
                                            <button
                                                class="btn btn-primary"
                                                type="button"
                                                disabled=move || !context.can_write()
                                                on:click=move |_| confirm.set(Some(ServeIntent::Start))
                                            >
                                                "Start supervised serve"
                                            </button>
                                        }.into_any()
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
                                                        <StatusIndicator label=outcome_label(&verification.outcome).to_string() tone=if verification.verified { "success" } else { "danger" } raw=verification.outcome.clone()/>
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
                                            <div class="row-between">
                                                <span class="muted">"Status"</span>
                                                <StatusIndicator label=view_model.label.to_string() tone=view_model.tone raw=serving.status.clone().unwrap_or_default()/>
                                            </div>
                                            {view_model.detail.clone().map(|detail| view! { <Notice kind="warning">{detail}</Notice> })}
                                            <InfoRow label="Process".to_string()>{serving.process_id.map(|id| id.to_string()).unwrap_or_else(|| "—".to_string())}</InfoRow>
                                            <InfoRow label="Restarts".to_string()>{serving.restart_count.unwrap_or_default().to_string()}</InfoRow>
                                            <InfoRow label="Last probe".to_string()>{serving.recorded_at_utc.map(|time| time_ago(Some(&time.to_rfc3339()))).unwrap_or_else(|| "None".to_string())}</InfoRow>
                                            {serving.detail.clone().map(|detail| view! { <div class="tiny faint">{detail}</div> })}
                                            {active.then(|| serving.url.clone().map(|url| view! {
                                                <a class="btn btn-primary" href=url target="_blank" rel="noopener noreferrer">"Open verified preview"</a>
                                            }))}
                                            {(!active).then(|| view! {
                                                <Notice kind="accent">"No preview link is shown because no reachable verified serve window is active. A historical journal record is not a running process."</Notice>
                                            })}
                                        </div>
                                    </Panel>

                                    <Panel title="Boundaries".to_string()>
                                        <ul class="small muted">
                                            <li>"Merge authorization is bound to the exact review digest."</li>
                                            <li>"Post-merge verification is bound to the merged revision, base and contract."</li>
                                            <li>"Independent acceptance is evaluator-owned and is never inferred here."</li>
                                            <li>"Start and stop are governed mutations on the factory-owned process; both go through the same confirmation."</li>
                                        </ul>
                                    </Panel>
                                </div>
                            </div>

                            {move || confirm.get().map(|intent| view! {
                                <ServeConfirm
                                    intent=intent
                                    serving=serving_for_dialog.clone()
                                    instance=instance_value.clone()
                                    work_item_id=work_item_value.clone()
                                    on_close=Callback::new(move |_| confirm.set(None))
                                />
                            })}
                        }.into_any()
                    }
                }}
            </Transition>
        </section>
    }
}

/// The same governed confirmation the work detail uses: the operator sees what the factory will do
/// and the process it owns before the mutation is sent. Nothing is optimistic.
#[component]
fn ServeConfirm(
    intent: ServeIntent,
    serving: Serving,
    instance: String,
    work_item_id: String,
    on_close: Callback<()>,
) -> impl IntoView {
    let context = AppContext::provide();
    let busy = RwSignal::new(false);
    let error = RwSignal::new(None::<String>);
    let duration = RwSignal::new(60i32);
    let confirmed = RwSignal::new(false);

    let close = Callback::new(move |_| {
        if busy.get() {
            return;
        }
        on_close.run(());
    });

    let submit = Callback::new(move |_| {
        if busy.get() {
            return;
        }
        let instance = instance.clone();
        let work_item_id = work_item_id.clone();
        let csrf = context.csrf.get();
        let seconds = duration.get();
        busy.set(true);
        error.set(None);
        spawn_local(async move {
            let result = match intent {
                ServeIntent::Start => {
                    api::start_serve(&instance, &csrf, &work_item_id, seconds).await
                }
                ServeIntent::Stop => api::stop_serve(&instance, &csrf, &work_item_id).await,
            };
            busy.set(false);
            match result {
                Ok(_) => {
                    context.notify_success(match intent {
                        ServeIntent::Start => {
                            "Supervised serve window accepted. The preview appears only after the factory proves a reachable process."
                        }
                        ServeIntent::Stop => "Serve cancellation requested.",
                    });
                    on_close.run(());
                    context.touch();
                }
                Err(failure) => {
                    let message = if failure.code.is_empty() || failure.message.contains(&failure.code) {
                        failure.message
                    } else {
                        format!("{} ({})", failure.message, failure.code)
                    };
                    error.set(Some(message));
                }
            }
        });
    });

    let title = match intent {
        ServeIntent::Start => "Start supervised serve",
        ServeIntent::Stop => "Stop supervised serve",
    };
    let description = match intent {
        ServeIntent::Start => {
            "The factory materialises the exact released revision and supervises the process for a bounded window."
        }
        ServeIntent::Stop => "The factory stops the process it owns. The release evidence stays recorded.",
    };
    let confirm_label = match intent {
        ServeIntent::Start => "Start supervised serve",
        ServeIntent::Stop => "Stop the serve window",
    };
    let can_submit = Signal::derive(move || {
        confirmed.get()
            && match intent {
                ServeIntent::Start => (10..=300).contains(&duration.get()),
                ServeIntent::Stop => true,
            }
    });
    let assertion = match intent {
        ServeIntent::Start => {
            "I understand the factory starts and owns this process, publishes a preview only after verification, and ends the window automatically."
        }
        ServeIntent::Stop => {
            "I understand the published preview stops responding immediately; release evidence and serving history stay recorded."
        }
    };
    let process = serving.process_id.unwrap_or_default();
    let url = serving.url.clone();
    let active = serving.active;
    let view_model: ServingView = serving_view(&serving);

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
            title=title.to_string()
            description=Some(description.to_string())
            on_close=close
            dismissible=true
            footer=footer
        >
            <div class="stack decision">
                {move || error.get().map(|message| view! {
                    <Notice kind="danger">
                        <strong>"The factory refused or could not complete this command. "</strong>
                        <span>{message}</span>
                    </Notice>
                })}
                {match intent {
                    ServeIntent::Start => view! {
                        <div class="stack">
                            <p class="small muted">
                                "The factory proves process identity, runs the frozen contract against the running process and publishes the URL only after it passes."
                            </p>
                            <label class="field">"Serve window (seconds, 10-300)"
                                <input
                                    class="input"
                                    type="number"
                                    min="10"
                                    max="300"
                                    prop:value=move || duration.get().to_string()
                                    on:input=move |event| duration.set(event_target_value(&event).parse().unwrap_or(60))
                                />
                            </label>
                            <ul class="decision-list">
                                <li>"The window ends automatically; the factory owns the process lifecycle."</li>
                                <li>"A serve window never implies independent acceptance."</li>
                            </ul>
                        </div>
                    }.into_any(),
                    ServeIntent::Stop => view! {
                        <div class="stack">
                            <Notice kind="warning">
                                <strong>"The published preview stops responding immediately. "</strong>
                                <span>"Release verification and serving history remain recorded."</span>
                            </Notice>
                            <dl class="metadata-list">
                                <div><dt>"Current status"</dt><dd>{view_model.label}</dd></div>
                                <div><dt>"Tracked process"</dt><dd class="mono">{if active && process > 0 { process.to_string() } else { "no process tracked by this adapter".to_string() }}</dd></div>
                                {url.map(|url| view! { <div><dt>"Published URL"</dt><dd class="mono wrap">{url}</dd></div> })}
                            </dl>
                        </div>
                    }.into_any(),
                }}
                <Confirmation assertion=assertion.to_string() checked=confirmed/>
            </div>
        </Modal>
    }
}
