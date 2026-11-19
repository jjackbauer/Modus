use leptos::prelude::*;
use leptos::task::spawn_local;
use leptos_router::hooks::use_params_map;

use control_plane_domain::Planning;

use crate::api;
use crate::context::AppContext;

#[component]
pub fn PlanningEditor(planning: Planning, version: i32, work_item_id: String) -> impl IntoView {
    let context = AppContext::provide();
    let params = use_params_map();
    let priority = RwSignal::new(
        planning
            .priority
            .clone()
            .unwrap_or_else(|| "normal".to_string()),
    );
    let deadline = RwSignal::new(planning.deadline_utc.clone().unwrap_or_default());
    let tags = RwSignal::new(planning.tags.join(", "));
    let links = RwSignal::new(planning.links.join("\n"));
    let notes = RwSignal::new(planning.notes.clone().unwrap_or_default());
    let busy = RwSignal::new(false);
    let message = RwSignal::new(None::<String>);

    let save = move |_| {
        let instance = params.get().get("instance").unwrap_or_default();
        let csrf = context.csrf.get();
        let request = control_plane_domain::PlanningUpdateRequest {
            priority: Some(priority.get()),
            deadline_utc: (!deadline.get().is_empty()).then(|| deadline.get()),
            tags: Some(
                tags.get()
                    .split(',')
                    .map(str::trim)
                    .filter(|value| !value.is_empty())
                    .map(str::to_string)
                    .collect(),
            ),
            links: Some(
                links
                    .get()
                    .lines()
                    .map(str::trim)
                    .filter(|value| !value.is_empty())
                    .map(str::to_string)
                    .collect(),
            ),
            notes: (!notes.get().is_empty()).then(|| notes.get()),
            expected_version: Some(version),
        };
        busy.set(true);
        message.set(None);
        let work_item_id = work_item_id.clone();
        spawn_local(async move {
            let result = api::update_planning(
                &instance,
                &csrf,
                &work_item_id,
                &request,
                &api::new_idempotency_key(),
            )
            .await;
            busy.set(false);
            match result {
                Ok(_) => {
                    context.notify_success("Planning metadata updated (control-plane only).");
                    message.set(Some(
                        "Saved. This is planning metadata, not factory authority.".to_string(),
                    ));
                    context.touch();
                }
                Err(error) => message.set(Some(error.message)),
            }
        });
    };

    view! {
        <div class="stack">
            <div class="form-grid">
                <label class="field">"Priority"
                    <select class="select" on:change=move |event| priority.set(event_target_value(&event))>
                        <option value="normal">"Normal"</option>
                        <option value="high">"High"</option>
                        <option value="urgent">"Urgent"</option>
                        <option value="low">"Low"</option>
                    </select>
                </label>
                <label class="field">"Deadline"
                    <input class="input" type="date" prop:value=move || deadline.get() on:input=move |event| deadline.set(event_target_value(&event))/>
                </label>
                <label class="field">"Tags"
                    <input class="input" prop:value=move || tags.get() on:input=move |event| tags.set(event_target_value(&event))/>
                </label>
                <label class="field">"Links"
                    <input class="input" prop:value=move || links.get() on:input=move |event| links.set(event_target_value(&event))/>
                </label>
                <label class="field span-2">"Notes"
                    <textarea class="textarea small-area" prop:value=move || notes.get() on:input=move |event| notes.set(event_target_value(&event))></textarea>
                </label>
            </div>
            {move || message.get().map(|message| view! { <div class="tiny muted">{message}</div> })}
            <button class="btn btn-sm" disabled=move || busy.get() || !context.can_write() on:click=save>
                {move || if busy.get() { "Saving…" } else { "Save planning metadata" }}
            </button>
            <div class="tiny faint">
                "Planning metadata never becomes factory scheduling or execution authority. Expected factory version: "
                {version}
            </div>
        </div>
    }
}
