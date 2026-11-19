use leptos::prelude::*;
use leptos_router::hooks::use_params_map;

use control_plane_domain::{Pipeline, format_duration_seconds, short_digest, state_label};

use crate::api;
use crate::components::ui::{
    Chip, EmptyBlock, LoadingBlock, MetricCard, Notice, Panel, error_view,
};
use crate::context::AppContext;

#[component]
pub fn PipelinePage() -> impl IntoView {
    let context = AppContext::provide();
    let params = use_params_map();
    let instance = Signal::derive(move || params.get().get("instance").unwrap_or_default());
    let at = RwSignal::new(String::new());
    let refresh = context.refresh;

    let pipeline = LocalResource::new(move || {
        let instance = instance.get();
        let at_value = at.get();
        let _ = refresh.get();
        async move {
            if !api::is_client() || instance.is_empty() {
                None
            } else {
                let at_param = (!at_value.is_empty()).then(|| format!("{at_value}:00Z"));
                Some(api::pipeline(&instance, at_param.as_deref()).await)
            }
        }
    });

    view! {
        <section class="page">
            <header class="page-header">
                <div>
                    <h1 class="page-title">Production pipeline</h1>
                    <p class="page-subtitle">
                        "Where every work item is, which stages are busy, what is waiting, and where failures cluster. Queue and duration measures are derived from durable events, not animated."
                    </p>
                </div>
                <div class="page-actions">
                    <input
                        class="input"
                        type="datetime-local"
                        aria-label="Reconstruct the pipeline at a past instant"
                        prop:value=move || at.get()
                        on:change=move |event| at.set(event_target_value(&event))
                    />
                    <button class="btn" disabled=move || at.get().is_empty() on:click=move |_| at.set(String::new())>"Live"</button>
                    <button class="btn" on:click=move |_| context.touch()>"Refresh"</button>
                </div>
            </header>

            <Suspense fallback=move || view! { <LoadingBlock label="Reconstructing pipeline state from durable events…"/> }>
                {move || match pipeline.get().flatten() {
                    None => view! { <LoadingBlock label="Reconstructing pipeline state from durable events…"/> }.into_any(),
                    Some(Err(error)) => view! { {error_view(error)} }.into_any(),
                    Some(Ok(pipeline)) => view! { <PipelineBody pipeline=pipeline/> }.into_any(),
                }}
            </Suspense>
        </section>
    }
}

#[component]
fn PipelineBody(pipeline: Pipeline) -> impl IntoView {
    let is_replay = pipeline.is_replay;
    let stages = pipeline.stages;
    let items = pipeline.items;
    let generated = pipeline.generated_at_utc;
    let note = pipeline.playback_note;
    let at = pipeline.at_utc;
    let queue_depth: i32 = stages.iter().map(|stage| stage.queue_depth).sum();
    let in_flight: i32 = stages.iter().map(|stage| stage.in_flight).sum();
    let needs_attention: i32 = stages.iter().map(|stage| stage.failed).sum();

    view! {
        <div class="stack">
            {is_replay.then(|| view! {
                <Notice kind="warn">
                    <strong>"Historical playback. "</strong>
                    <span>{note.clone().unwrap_or_default()}</span>
                    {at.clone().map(|at| view! { <span class="mono tiny">"Reconstructed at "{at}</span> })}
                </Notice>
            })}

            <div class="pipeline-stages">
                {stages.clone().into_iter().map(|stage| {
                    let total = stage.queue_depth + stage.in_flight + stage.waiting_for_authorization + stage.failed;
                    view! {
                        <div class="pipeline-stage">
                            <div class="row-between">
                                <strong>{stage.label.clone()}</strong>
                                <span class="tiny faint">{total}</span>
                            </div>
                            <div class="pipeline-metrics">
                                <span><span class="faint">"Queue "</span>{stage.queue_depth}</span>
                                <span><span class="faint">"In flight "</span>{stage.in_flight}</span>
                                <span><span class="faint">"Waiting auth "</span>{stage.waiting_for_authorization}</span>
                                <span><span class="faint">"Blocked/failed "</span>{stage.failed}</span>
                            </div>
                            <div class="tiny faint">
                                {stage.average_processing_seconds.map(|value| format!("processing {}", format_duration_seconds(value))).unwrap_or_else(|| "processing not measured".to_string())}
                                " · "
                                {stage.average_waiting_seconds.map(|value| format!("waiting {}", format_duration_seconds(value))).unwrap_or_else(|| "waiting not measured".to_string())}
                            </div>
                            <div class="tiny faint">{stage.completed_in_window}" completed item(s) in this stage"</div>
                        </div>
                    }
                }).collect_view()}
            </div>

            <div class="metric-grid">
                <MetricCard label="Work items shown".to_string() value=items.len().to_string() unit=None basis=None tone="" measured=true/>
                <MetricCard label="Queue depth".to_string() value=queue_depth.to_string() unit=None basis=Some("Sum of stage queue depths".to_string()) tone="waiting" measured=true/>
                <MetricCard label="In flight".to_string() value=in_flight.to_string() unit=None basis=Some("Active stages".to_string()) tone="active" measured=true/>
                <MetricCard label="Needs attention".to_string() value=needs_attention.to_string() unit=None basis=Some("Blocked or failed".to_string()) tone="danger" measured=true/>
            </div>

            <div class="pipeline-board">
                {stages.into_iter().map(|stage| {
                    let stage_items: Vec<_> = items.iter().filter(|item| item.stage == stage.id).cloned().collect();
                    let head_tone = if stage.failed > 0 { "danger" } else { "neutral" };
                    view! {
                        <div class="pipeline-column">
                            <div class="pipeline-column-head">
                                <div>
                                    <strong>{stage.label.clone()}</strong>
                                    <div class="tiny faint">{stage.id.clone()}</div>
                                </div>
                                <Chip label=stage_items.len().to_string() tone=head_tone/>
                            </div>
                            <div class="pipeline-column-body">
                                {if stage_items.is_empty() {
                                    view! { <div class="tiny faint">"No work at this stage."</div> }.into_any()
                                } else {
                                    stage_items.into_iter().map(|item| view! {
                                        <div class="pipeline-card">
                                            <div class="row-between">
                                                <span class="tiny mono faint">{short_digest(Some(&item.work_item_id))}</span>
                                                <Chip label=state_label(&item.state).to_string() tone=if item.needs_attention { "danger" } else { "neutral" }/>
                                            </div>
                                            <strong>{item.title.clone()}</strong>
                                            <div class="tiny faint">
                                                {format!("waiting {}", format_duration_seconds(item.waiting_seconds as f64))}
                                            </div>
                                            {item.last_failure.map(|failure| view! { <div class="tiny form-error">{failure}</div> })}
                                        </div>
                                    }).collect_view().into_any()
                                }}
                            </div>
                        </div>
                    }
                }).collect_view()}
            </div>

            <div class="row-between">
                <span class="tiny faint">"Generated "{generated.to_rfc3339()}</span>
                <span class="tiny faint">"Flow metrics are descriptive. No forecast is inferred from this history."</span>
            </div>
        </div>
    }
}
