use leptos::prelude::*;
use leptos_router::hooks::use_params_map;

use control_plane_domain::{Analytics, Metric, SeriesPoint, format_duration_seconds};

use crate::api;
use crate::components::ui::{LoadingBlock, MiniBar, Notice, Panel, error_view};
use crate::context::AppContext;

#[component]
pub fn AnalyticsPage() -> impl IntoView {
    let context = AppContext::provide();
    let params = use_params_map();
    let instance = Signal::derive(move || params.get().get("instance").unwrap_or_default());
    let from = RwSignal::new(String::new());
    let to = RwSignal::new(String::new());
    let refresh = context.refresh;

    let analytics = LocalResource::new(move || {
        let instance = instance.get();
        let from_value = from.get();
        let to_value = to.get();
        let _ = refresh.get();
        async move {
            if !api::is_client() || instance.is_empty() {
                None
            } else {
                let from = (!from_value.is_empty()).then(|| format!("{from_value}T00:00:00Z"));
                let to = (!to_value.is_empty()).then(|| format!("{to_value}T23:59:59Z"));
                Some(api::analytics(&instance, from.as_deref(), to.as_deref()).await)
            }
        }
    });

    view! {
        <section class="page">
            <header class="page-header">
                <div>
                    <h1 class="page-title">Performance & analytics</h1>
                    <p class="page-subtitle">
                        "Measured throughput, duration, failure and token telemetry. Every number links to a definition; missing telemetry is shown as Not measured, never zero."
                    </p>
                </div>
                <div class="page-actions">
                    <input class="input" type="date" aria-label="From date" prop:value=move || from.get() on:change=move |event| from.set(event_target_value(&event))/>
                    <input class="input" type="date" aria-label="To date" prop:value=move || to.get() on:change=move |event| to.set(event_target_value(&event))/>
                    <button class="btn" on:click=move |_| context.touch()>"Refresh"</button>
                </div>
            </header>

            <Transition fallback=move || view! { <LoadingBlock label="Loading measured factory analytics…"/> }>
                {move || match analytics.get().flatten() {
                    None => view! { <LoadingBlock label="Loading measured factory analytics…"/> }.into_any(),
                    Some(Err(error)) => view! { {error_view(error)} }.into_any(),
                    Some(Ok(analytics)) => view! { <AnalyticsBody analytics=analytics/> }.into_any(),
                }}
            </Transition>
        </section>
    }
}

#[component]
fn AnalyticsBody(analytics: Analytics) -> impl IntoView {
    let window = analytics.window.clone();
    let measured = analytics
        .metrics
        .iter()
        .filter(|metric| metric.status == "measured")
        .count();
    let not_measured = analytics.not_measured.clone();
    let metrics = analytics.metrics.clone();
    let definitions = analytics.definitions.clone();
    let throughput = analytics.throughput_by_day.clone();
    let failures = analytics.failure_categories.clone();
    let stages = analytics.stage_durations.clone();
    let tokens = analytics.token_usage_by_model.clone();
    let lead = analytics.lead_time_by_state.clone();

    view! {
        <div class="stack">
            <div class="row-between">
                <span class="tiny faint">
                    {format!("Window {} → {}", window.from_utc, window.to_utc)}
                </span>
                <span class="tiny faint">{format!("{measured} measured metric(s)")}</span>
            </div>

            {(!not_measured.is_empty()).then(|| view! {
                <Notice kind="warn">
                    <strong>"Some telemetry is not measured. "</strong>
                    <span>"These values are unavailable, not zero: "{not_measured.join(", ")}</span>
                </Notice>
            })}

            <div class="metric-grid">
                {metrics.into_iter().map(|metric| view! { <ReferenceMetric metric=metric/> }).collect_view()}
            </div>

            <div class="split">
                <div class="stack">
                    <Panel title="Throughput by day".to_string()>
                        {series_bars(throughput, "No release events in this window.")}
                    </Panel>
                    <Panel title="Failure categories".to_string()>
                        {series_bars(failures, "No stage failures recorded in this window.")}
                    </Panel>
                    <Panel title="Stage durations (median seconds)".to_string()>
                        {series_bars(stages, "Not measured: no completed stage pair in this window.")}
                    </Panel>
                </div>
                <div class="stack">
                    <Panel title="Tokens by model".to_string()>
                        {if tokens.is_empty() {
                            view! { <div class="tiny faint">"Not measured: no provider usage exists in durable traces. Token volume is not inferred from activity."</div> }.into_any()
                        } else {
                            view! {
                                <div class="stack">
                                    {tokens.into_iter().map(|point| view! {
                                        <MiniBar label=point.label.clone() value=point.value max=1000.0 display=format!("{:.0}", point.value)/>
                                    }).collect_view()}
                                    <div class="tiny faint">"Sums provider-reported input and output tokens. Cached tokens are not treated as unique context."</div>
                                </div>
                            }.into_any()
                        }}
                    </Panel>
                    <Panel title="Lead time by final state (median seconds)".to_string()>
                        {series_bars(lead, "No completed lead times in this window.")}
                    </Panel>
                    <Panel title="Definitions".to_string()>
                        <details class="evidence">
                            <summary>"Show metric definitions"</summary>
                            <div class="stack" style="margin-top:8px">
                                {definitions.into_iter().map(|definition| view! {
                                    <div class="definition">
                                        <div class="row-between"><code class="mono">{definition.key}</code><span class="tiny faint">{definition.unit}</span></div>
                                        <div class="tiny muted">{definition.definition}</div>
                                        {definition.notes.map(|notes| view! { <div class="tiny faint">{notes}</div> })}
                                    </div>
                                }).collect_view()}
                            </div>
                        </details>
                    </Panel>
                </div>
            </div>
        </div>
    }
}

#[component]
fn ReferenceMetric(metric: Metric) -> impl IntoView {
    let measured = metric.status == "measured";
    let value = metric
        .value
        .map(|value| {
            if metric.unit == "seconds" {
                format_duration_seconds(value)
            } else if metric.unit == "ratio" {
                format!("{:.1}%", value * 100.0)
            } else if metric.unit == "items/day" {
                format!("{value:.2}")
            } else {
                format!("{value:.0}")
            }
        })
        .unwrap_or_else(|| "Not measured".to_string());
    view! {
        <div class=format!("metric {}", if measured { "" } else { "metric-not-measured" })>
            <div class="metric-label"><span>{metric.label.clone()}</span></div>
            <div class="metric-value">{value}<span class="metric-unit">{metric.unit.clone()}</span></div>
            <div class="metric-basis">{metric.basis.clone()}</div>
        </div>
    }
}

fn series_bars(points: Vec<SeriesPoint>, empty: &'static str) -> impl IntoView {
    if points.is_empty() {
        return view! { <div class="tiny faint">{empty}</div> }.into_any();
    }

    let max = points
        .iter()
        .map(|point| point.value)
        .fold(0.0_f64, f64::max);
    view! {
        <div class="stack">
            {points.into_iter().map(|point| view! {
                <MiniBar label=point.label.clone() value=point.value max=max display=format!("{:.1}", point.value)/>
            }).collect_view()}
        </div>
    }.into_any()
}
