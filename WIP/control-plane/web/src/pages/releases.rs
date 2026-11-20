use leptos::prelude::*;
use leptos_router::hooks::use_params_map;

use control_plane_domain::{serving_status_label, serving_status_tone, short_digest, state_label, state_tone};

use crate::api;
use crate::components::status::StatusIndicator;
use crate::components::ui::{EmptyBlock, LoadingBlock, Panel, error_view};
use crate::context::AppContext;

#[component]
pub fn ReleasesPage() -> impl IntoView {
    let context = AppContext::provide();
    let params = use_params_map();
    let instance = Signal::derive(move || params.get().get("instance").unwrap_or_default());
    let refresh = context.refresh;

    let releases = LocalResource::new(move || {
        let instance = instance.get();
        let _ = refresh.get();
        async move {
            if !api::is_client() || instance.is_empty() {
                None
            } else {
                Some(api::releases(&instance).await)
            }
        }
    });

    view! {
        <section class="page">
            <header class="page-header">
                <div>
                    <h1 class="page-title">Artifacts & releases</h1>
                    <p class="page-subtitle">
                        "Reviewed candidate, merged revision, verified release and supervised process are distinct facts. A merge never implies that an application is running."
                    </p>
                </div>
                <div class="page-actions">
                    <button class="btn" on:click=move |_| context.touch()>"Refresh"</button>
                </div>
            </header>

            <Suspense fallback=move || view! { <LoadingBlock label="Loading release evidence…"/> }>
                {move || match releases.get().flatten() {
                    None => view! { <LoadingBlock label="Loading release evidence…"/> }.into_any(),
                    Some(Err(error)) => view! { {error_view(error)} }.into_any(),
                    Some(Ok(list)) => view! {
                        <Panel title=format!("Releases ({})", list.total)>
                            {if list.items.is_empty() {
                                view! { <EmptyBlock title="No reviewed or released work" detail="A work item appears here once an independent review accepts a revision."/> }.into_any()
                            } else {
                                view! {
                                    <div class="table-wrap">
                                        <table class="data">
                                            <thead><tr>
                                                <th>"Work item"</th>
                                                <th>"Factory state"</th>
                                                <th>"Merged revision"</th>
                                                <th>"Post-merge verification"</th>
                                                <th>"Serving"</th>
                                                <th>"Merged"</th>
                                            </tr></thead>
                                            <tbody>
                                                {list.items.into_iter().map(|release| {
                                                    let href = format!("/i/{}/releases/{}", instance.get(), release.work_item_id);
                                                    let state = release.state.clone();
                                                    let state_label = state_label(&state).to_string();
                                                    let state_tone = state_tone(&state);
                                                    let work_id = release.work_item_id.clone();
                                                    let verified = release.verified;
                                                    let serving = release.serving_status.clone();
                                                    let serving_tone = serving_status_tone(Some(serving.as_str()));
                                                    let merged = short_digest(release.merged_sha.as_deref());
                                                    let merged_at = release.merged_at_utc.as_deref().map(|value| crate::components::ui::timestamp(Some(value))).unwrap_or_else(|| "—".to_string());
                                                    view! {
                                                        <tr>
                                                            <td><a href=href>{release.title}</a><div class="tiny faint mono">{work_id}</div></td>
                                                            <td><StatusIndicator label=state_label.clone() tone=state_tone raw=state_label.clone()/></td>
                                                            <td class="mono tiny">{merged}</td>
                                                            <td><StatusIndicator label=if verified { "Release verified".to_string() } else { "Not verified".to_string() } tone=if verified { "success" } else { "waiting" }/></td>
                                                            <td>
                                                                <div class="row">
                                                                    <StatusIndicator label=serving_status_label(Some(serving.as_str())).to_string() tone=serving_tone raw=serving.clone()/>
                                                                    {release.serving_url.map(|url| view! { <a class="tiny" href=url target="_blank" rel="noopener noreferrer">"open"</a> })}
                                                                </div>
                                                            </td>
                                                            <td class="tiny">{merged_at}</td>
                                                        </tr>
                                                    }
                                                }).collect_view()}
                                            </tbody>
                                        </table>
                                    </div>
                                }.into_any()
                            }}
                        </Panel>
                    }.into_any(),
                }}
            </Suspense>
        </section>
    }
}
