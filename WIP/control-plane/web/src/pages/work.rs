use leptos::prelude::*;
use leptos::task::spawn_local;
use leptos_router::NavigateOptions;
use leptos_router::hooks::{use_navigate, use_params_map};
use serde::{Deserialize, Serialize};

use control_plane_domain::{
    Page, Preview, RequirementDraft, SubmitRequest, WorkItemSummary, format_relative,
    indicator_label, indicator_tone, render_markdown, short_digest, state_label, state_tone,
};

use crate::api;
use crate::components::ui::{
    Chip, EmptyBlock, LoadingBlock, MetricCard, Notice, Panel, PanelActions, error_view, time_ago,
};
use crate::context::AppContext;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum QueueView {
    Table,
    Kanban,
    Prioritized,
}

impl QueueView {
    fn as_str(self) -> &'static str {
        match self {
            Self::Table => "table",
            Self::Kanban => "kanban",
            Self::Prioritized => "priority",
        }
    }

    fn from_str(value: &str) -> Self {
        match value {
            "kanban" => Self::Kanban,
            "priority" => Self::Prioritized,
            _ => Self::Table,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
struct SavedView {
    name: String,
    state: String,
    indicator: String,
    search: String,
    sort: String,
    view: String,
}

#[component]
pub fn WorkQueuePage() -> impl IntoView {
    let context = AppContext::provide();
    let params = use_params_map();
    let instance = Signal::derive(move || params.get().get("instance").unwrap_or_default());
    let navigate = use_navigate();

    let view = RwSignal::new(QueueView::Table);
    let state = RwSignal::new(String::new());
    let indicator = RwSignal::new(String::new());
    let search = RwSignal::new(String::new());
    let sort = RwSignal::new("updated-desc".to_string());
    let show_intake = RwSignal::new(false);
    let saved = RwSignal::new(Vec::<SavedView>::new());
    let saved_name = RwSignal::new(String::new());
    let refresh = context.refresh;

    #[cfg(target_arch = "wasm32")]
    {
        Effect::new(move |_| {
            if let Some(storage) =
                web_sys::window().and_then(|window| window.local_storage().ok().flatten())
            {
                if let Ok(Some(raw)) = storage.get_item("cp_work_views") {
                    if let Ok(views) = serde_json::from_str::<Vec<SavedView>>(&raw) {
                        saved.set(views);
                    }
                }
            }
        });
    }

    let work = LocalResource::new(move || {
        let instance = instance.get();
        let state = state.get();
        let indicator = indicator.get();
        let search = search.get();
        let sort = sort.get();
        let _ = refresh.get();
        async move {
            if !api::is_client() || instance.is_empty() {
                None
            } else {
                Some(
                    api::work_items(
                        &instance,
                        Some(state.as_str()),
                        Some(search.as_str()),
                        Some(indicator.as_str()),
                        Some(sort.as_str()),
                        200,
                        0,
                    )
                    .await,
                )
            }
        }
    });

    let apply_view = move |saved_view: SavedView| {
        state.set(saved_view.state);
        indicator.set(saved_view.indicator);
        search.set(saved_view.search);
        sort.set(saved_view.sort);
        view.set(QueueView::from_str(&saved_view.view));
    };

    let on_created = Callback::new(move |work_item_id: String| {
        show_intake.set(false);
        let url = format!("/i/{}/work/{work_item_id}", instance.get());
        navigate(&url, NavigateOptions::default());
    });

    let reload_saved = move || {
        let name = saved_name.get().trim().to_string();
        if name.is_empty() {
            context.toast.set(Some(crate::context::Toast {
                kind: "warn".to_string(),
                message: "Name the view before saving it.".to_string(),
                detail: None,
            }));
            return;
        }

        let view = SavedView {
            name,
            state: state.get(),
            indicator: indicator.get(),
            search: search.get(),
            sort: sort.get(),
            view: view.get().as_str().to_string(),
        };
        let mut views = saved.get();
        views.retain(|candidate| candidate.name != view.name);
        views.push(view);
        views.truncate(12);
        saved.set(views.clone());
        #[cfg(target_arch = "wasm32")]
        if let Some(storage) =
            web_sys::window().and_then(|window| window.local_storage().ok().flatten())
        {
            if let Ok(raw) = serde_json::to_string(&views) {
                let _ = storage.set_item("cp_work_views", &raw);
            }
        }
        saved_name.set(String::new());
    };

    view! {
        <section class="page">
            <header class="page-header">
                <div>
                    <h1 class="page-title">Work queue</h1>
                    <p class="page-subtitle">
                        "Describe work, submit it to the factory, and see the authoritative state rather than a local task board."
                    </p>
                </div>
                <div class="page-actions">
                    <button class="btn" on:click=move |_| context.touch()>"Refresh"</button>
                    <button class="btn btn-primary" on:click=move |_| show_intake.update(|value| *value = !*value)>
                        {move || if show_intake.get() { "Close intake" } else { "New work item" }}
                    </button>
                </div>
            </header>

            {move || show_intake.get().then(|| view! {
                <WorkIntakeForm instance=instance.get() on_created=on_created/>
            })}

            <div class="queue-toolbar">
                <div class="segmented" role="group" aria-label="Work queue view">
                    <button class:active=move || view.get() == QueueView::Table on:click=move |_| view.set(QueueView::Table)>"Table"</button>
                    <button class:active=move || view.get() == QueueView::Kanban on:click=move |_| view.set(QueueView::Kanban)>"Kanban"</button>
                    <button class:active=move || view.get() == QueueView::Prioritized on:click=move |_| view.set(QueueView::Prioritized)>"Priority"</button>
                </div>
                <input
                    class="input queue-search"
                    type="search"
                    placeholder="Search id, title or digest"
                    prop:value=move || search.get()
                    on:input=move |event| search.set(event_target_value(&event))
                />
                <select class="select" aria-label="Filter by factory state" on:change=move |event| state.set(event_target_value(&event))>
                    <option value="">"All factory states"</option>
                    {known_states().into_iter().map(|state| view! { <option value=state>{state_label(state)}</option> }).collect_view()}
                </select>
                <select class="select" aria-label="Filter by derived indicator" on:change=move |event| indicator.set(event_target_value(&event))>
                    <option value="">"All indicators"</option>
                    <option value="waiting-contract-approval">"Waiting for contract approval"</option>
                    <option value="waiting-merge-approval">"Waiting for merge approval"</option>
                    <option value="active-implementation">"Active implementation"</option>
                    <option value="running-verification">"Running verification"</option>
                    <option value="needs-rework">"Needs rework"</option>
                    <option value="blocked">"Blocked"</option>
                    <option value="terminal-failure">"Terminal failure"</option>
                    <option value="released">"Released"</option>
                </select>
                <select class="select" aria-label="Sort work items" on:change=move |event| sort.set(event_target_value(&event))>
                    <option value="updated-desc">"Recently updated"</option>
                    <option value="created-desc">"Newest first"</option>
                    <option value="created-asc">"Oldest first"</option>
                    <option value="state">"Factory state"</option>
                    <option value="title">"Title"</option>
                </select>
                <div class="saved-views">
                    <select class="select" aria-label="Saved views" on:change=move |event| {
                        let name = event_target_value(&event);
                        if let Some(view) = saved.get().into_iter().find(|view| view.name == name) {
                            apply_view(view);
                        }
                    }>
                        <option value="">"Saved views"</option>
                        {move || saved.get().into_iter().map(|view| { let name = view.name.clone(); { let value_name = name.clone(); view! { <option value=value_name>{name}</option> } } }).collect_view()}
                    </select>
                    <input class="input" placeholder="Name this view" prop:value=move || saved_name.get() on:input=move |event| saved_name.set(event_target_value(&event))/>
                    <button class="btn btn-sm" on:click=move |_| reload_saved()>"Save"</button>
                </div>
            </div>

            <Suspense fallback=move || view! { <LoadingBlock label="Loading durable work items…"/> }>
                {move || match work.get().flatten() {
                    None => view! { <LoadingBlock label="Loading durable work items…"/> }.into_any(),
                    Some(Err(error)) => view! { {error_view(error)} }.into_any(),
                    Some(Ok(page)) => view! {
                        <QueueSummary page=page.clone()/>
                        {match view.get() {
                            QueueView::Table => view! { <WorkTable page=page.clone() instance=instance/> }.into_any(),
                            QueueView::Kanban => view! { <WorkKanban page=page.clone() instance=instance/> }.into_any(),
                            QueueView::Prioritized => view! { <WorkPriority page=page.clone() instance=instance/> }.into_any(),
                        }}
                    }.into_any(),
                }}
            </Suspense>
        </section>
    }
}

fn known_states() -> [&'static str; 16] {
    [
        "Proposed",
        "RequirementsAligned",
        "ArchitectureRefined",
        "ReadyForImplementation",
        "InImplementation",
        "InReview",
        "ReadyForDelivery",
        "DeliveryValidation",
        "ReleaseCandidate",
        "Released",
        "OperationallyValidated",
        "Blocked",
        "NeedsRework",
        "Failed",
        "Cancelled",
        "Closed",
    ]
}

#[component]
fn QueueSummary(page: Page<WorkItemSummary>) -> impl IntoView {
    let awaiting = page
        .items
        .iter()
        .filter(|item| item.awaiting_contract_approval || item.awaiting_merge_approval)
        .count();
    let failed = page
        .items
        .iter()
        .filter(|item| item.state == "Failed")
        .count();
    let active = page
        .items
        .iter()
        .filter(|item| matches!(item.state.as_str(), "InImplementation" | "InReview"))
        .count();
    view! {
        <div class="metric-grid">
            <MetricCard label="Shown".to_string() value=page.items.len().to_string() unit=None basis=None tone="" measured=true/>
            <MetricCard label="Awaiting authorization".to_string() value=awaiting.to_string() unit=None basis=None tone="waiting" measured=true/>
            <MetricCard label="Active execution".to_string() value=active.to_string() unit=None basis=None tone="active" measured=true/>
            <MetricCard label="Terminal failures".to_string() value=failed.to_string() unit=None basis=None tone="danger" measured=true/>
        </div>
    }
}

#[component]
fn WorkTable(page: Page<WorkItemSummary>, instance: Signal<String>) -> impl IntoView {
    if page.items.is_empty() {
        return view! {
            <EmptyBlock title="No work matches these filters" detail="Clear filters, or submit a requirement from the intake form."/>
        }
        .into_any();
    }

    view! {
        <div class="table-wrap">
            <table class="data">
                <thead>
                    <tr>
                        <th>"Work item"</th>
                        <th>"State"</th>
                        <th>"Derived indicator"</th>
                        <th>"Priority"</th>
                        <th>"Events"</th>
                        <th>"Updated"</th>
                        <th>"Last failure"</th>
                    </tr>
                </thead>
                <tbody>
                    {page.items.into_iter().map(|item| {
                        let href = format!("/i/{}/work/{}", instance.get(), item.work_item_id);
                        let title = item.title.clone();
                        let work_item_id = item.work_item_id.clone();
                        let state = item.state.clone();
                        let state_label = state_label(&state).to_string();
                        let state_tone = state_tone(&state);
                        let indicator = item.derived_indicator.clone();
                        let indicator_label = indicator_label(indicator.as_deref()).to_string();
                        let indicator_tone = indicator_tone(indicator.as_deref());
                        let priority = item.planning.priority.clone().unwrap_or_else(|| "—".to_string());
                        let event_count = item.event_count;
                        let updated = item.updated_at_utc.clone();
                        let updated_title = timestamp(updated.as_deref());
                        let updated_text = time_ago(updated.as_deref());
                        let last_failure = item.last_failure.clone().unwrap_or_else(|| "—".to_string());
                        view! {
                            <tr>
                                <td>
                                    <a href=href><strong>{title}</strong></a>
                                    <div class="tiny faint mono">{work_item_id}</div>
                                </td>
                                <td><Chip label=state_label tone=state_tone/></td>
                                <td><Chip label=indicator_label tone=indicator_tone/></td>
                                <td>{priority}</td>
                                <td class="num">{event_count}</td>
                                <td title=updated_title>{updated_text}</td>
                                <td class="tiny">{last_failure}</td>
                            </tr>
                        }
                    }).collect_view()}
                </tbody>
            </table>
        </div>
    }
    .into_any()
}

#[component]
fn WorkKanban(page: Page<WorkItemSummary>, instance: Signal<String>) -> impl IntoView {
    let columns: [(&'static str, &[&'static str]); 7] = [
        (
            "Intake",
            &[
                "Proposed",
                "RequirementsAligned",
                "ArchitectureRefined",
                "ReadyForImplementation",
            ],
        ),
        ("Implementation", &["InImplementation", "NeedsRework"]),
        ("Review", &["InReview"]),
        ("Delivery", &["ReadyForDelivery", "DeliveryValidation"]),
        ("Release", &["ReleaseCandidate"]),
        ("Released", &["Released", "OperationallyValidated"]),
        ("Attention", &["Blocked", "Failed"]),
    ];

    view! {
        <div class="kanban">
            {columns.into_iter().map(|(label, states)| {
                let cards: Vec<WorkItemSummary> = page.items.iter().filter(|item| states.contains(&item.state.as_str())).cloned().collect();
                view! {
                    <div class="kanban-column">
                        <div class="kanban-head"><span>{label}</span><span class="tiny faint">{cards.len()}</span></div>
                        <div class="kanban-body">
                            {cards.into_iter().map(|item| {
                                let href = format!("/i/{}/work/{}", instance.get(), item.work_item_id);
                                let work_item_id = short_digest(Some(&item.work_item_id));
                                let title = item.title.clone();
                                let state = item.state.clone();
                                let state_label = state_label(&state).to_string();
                                let state_tone = state_tone(&state);
                                let indicator = indicator_label(item.derived_indicator.as_deref()).to_string();
                                view! {
                                    <a class="kanban-card" href=href>
                                        <div class="row-between">
                                            <span class="tiny mono faint">{work_item_id}</span>
                                            <Chip label=state_label tone=state_tone/>
                                        </div>
                                        <strong>{title}</strong>
                                        <div class="tiny faint">{indicator}</div>
                                    </a>
                                }
                            }).collect_view()}
                        </div>
                    </div>
                }
            }).collect_view()}
        </div>
    }
}

#[component]
fn WorkPriority(page: Page<WorkItemSummary>, instance: Signal<String>) -> impl IntoView {
    let mut items = page.items;
    items.sort_by_key(|item| {
        let attention = if item.awaiting_contract_approval || item.awaiting_merge_approval {
            0
        } else if matches!(item.state.as_str(), "Failed" | "Blocked" | "NeedsRework") {
            1
        } else if matches!(item.state.as_str(), "InImplementation" | "InReview") {
            2
        } else {
            3
        };
        let priority = match item.planning.priority.as_deref() {
            Some("urgent") => 0,
            Some("high") => 1,
            Some("normal") => 2,
            _ => 3,
        };
        (
            attention,
            priority,
            std::cmp::Reverse(item.updated_at_utc.clone()),
        )
    });

    view! {
        <div class="stack">
            {if items.is_empty() {
                view! { <EmptyBlock title="No work in the queue" detail="Submit a requirement to create the first work item."/> }.into_any()
            } else {
                items.into_iter().map(|item| {
                    let href = format!("/i/{}/work/{}", instance.get(), item.work_item_id);
                    let title = item.title.clone();
                    let state = item.state.clone();
                    let state_label = state_label(&state).to_string();
                    let state_tone = state_tone(&state);
                    let indicator = indicator_label(item.derived_indicator.as_deref()).to_string();
                    let priority = item.planning.priority.clone().unwrap_or_else(|| "no planning priority".to_string());
                    let updated = time_ago(item.updated_at_utc.as_deref());
                    view! {
                        <a class="priority-row" href=href>
                            <div class="priority-rank">{indicator}</div>
                            <div class="priority-main">
                                <div class="row-between">
                                    <strong>{title}</strong>
                                    <Chip label=state_label tone=state_tone/>
                                </div>
                                <div class="tiny faint">
                                    {priority}
                                    " · "
                                    {updated}
                                </div>
                            </div>
                        </a>
                    }
                }).collect_view().into_any()
            }}
        </div>
    }
}

#[component]
fn WorkIntakeForm(instance: String, on_created: Callback<String>) -> impl IntoView {
    let context = AppContext::provide();
    let preview_instance = instance.clone();
    let submit_instance = instance.clone();
    let title = RwSignal::new(String::new());
    let description = RwSignal::new(String::new());
    let functional = RwSignal::new(String::new());
    let expectations = RwSignal::new(String::new());
    let constraints = RwSignal::new(String::new());
    let non_goals = RwSignal::new(String::new());
    let contract_json = RwSignal::new(String::new());
    let template_reference = RwSignal::new(String::new());
    let template_parameters = RwSignal::new(String::new());
    let selected_template = RwSignal::new(String::new());
    let template_instance = instance.clone();
    let templates = LocalResource::new(move || {
        let instance = template_instance.clone();
        async move {
            if !api::is_client() || instance.is_empty() {
                None
            } else {
                Some(api::templates(&instance).await)
            }
        }
    });
    let priority = RwSignal::new("normal".to_string());
    let deadline = RwSignal::new(String::new());
    let tags = RwSignal::new(String::new());
    let links = RwSignal::new(String::new());
    let notes = RwSignal::new(String::new());
    let confirmed = RwSignal::new(false);
    let preview = RwSignal::new(None::<Preview>);
    let error = RwSignal::new(None::<api::UiError>);
    let busy = RwSignal::new(false);
    let idempotency_key = RwSignal::new(api::new_idempotency_key());

    let build_draft = move || RequirementDraft {
        title: title.get(),
        description: description.get(),
        functional_requirements: lines(&functional.get()),
        acceptance_expectations: lines(&expectations.get()),
        constraints: lines(&constraints.get()),
        non_goals: lines(&non_goals.get()),
        supported_class: control_plane_domain::SUPPORTED_CLASS.to_string(),
        template_reference: non_empty(&template_reference.get()),
        template_parameters_json: non_empty(&template_parameters.get()),
        acceptance_contract_json: non_empty(&contract_json.get()),
        priority: Some(priority.get()),
        deadline_utc: non_empty(&deadline.get()),
        tags: split_csv(&tags.get()),
        links: split_lines(&links.get()),
        notes: non_empty(&notes.get()),
    };

    let on_preview = move |_| {
        let draft = build_draft.clone()();
        let request = control_plane_domain::PreviewRequest {
            requirement: draft.to_markdown(),
            title: None,
            acceptance_contract_json: draft.acceptance_contract_json.clone(),
        };
        let instance = preview_instance.clone();
        let csrf = context.csrf.get();
        error.set(None);
        busy.set(true);
        spawn_local(async move {
            let result = api::preview(&instance, &csrf, &request).await;
            match result {
                Ok(value) => preview.set(Some(value)),
                Err(issue) => error.set(Some(issue)),
            }
            busy.set(false);
        });
    };

    let on_submit = move |_| {
        let draft = build_draft.clone()();
        let local_errors = draft.validation_errors();
        if !local_errors.is_empty() {
            error.set(Some(api::UiError::new(
                0,
                "local-validation",
                local_errors.join(" "),
            )));
            return;
        }
        let Some(preview_value) = preview.get() else {
            error.set(Some(api::UiError::new(
                0,
                "preview-required",
                "Validate the requirement with the factory before submitting.",
            )));
            return;
        };
        if !preview_value.valid {
            error.set(Some(api::UiError::new(
                0,
                "preview-invalid",
                "The factory parser still reports errors.",
            )));
            return;
        }
        if !confirmed.get() {
            error.set(Some(api::UiError::new(
                0,
                "confirmation-required",
                "Confirm that this is the exact requirement you intend to submit.",
            )));
            return;
        }

        let request = SubmitRequest {
            requirement: draft.to_markdown(),
            title: None,
            acceptance_contract_json: draft.acceptance_contract_json.clone(),
            planning: Some(draft.planning_input()),
            explicitly_confirmed: Some(true),
        };
        let instance = submit_instance.clone();
        let csrf = context.csrf.get();
        let key = idempotency_key.get();
        busy.set(true);
        error.set(None);
        spawn_local(async move {
            match api::submit(&instance, &csrf, &request, &key).await {
                Ok(response) => {
                    context.notify_success(format!("Work item {} created.", response.work_item_id));
                    idempotency_key.set(api::new_idempotency_key());
                    busy.set(false);
                    on_created.run(response.work_item_id);
                }
                Err(issue) => {
                    error.set(Some(issue));
                    busy.set(false);
                }
            }
        });
    };

    let build_markdown = build_draft.clone();
    let markdown = move || build_markdown().to_markdown();
    let rendered = move || render_markdown(&markdown());
    view! {
        <section class="panel intake-panel">
            <header class="panel-header">
                <h2 class="panel-title">"New work item"</h2>
                <span class="tiny faint">"The factory parser is authoritative; planning metadata is control-plane only."</span>
            </header>
            <div class="panel-body">
                <div class="form-grid">
                    <label class="field span-2">"Title"
                        <input class="input" prop:value=move || title.get() on:input=move |event| { title.set(event_target_value(&event)); preview.set(None); }/>
                    </label>
                    <label class="field span-2">"Description"
                        <textarea class="textarea" placeholder="What should exist, for whom, and why?" prop:value=move || description.get() on:input=move |event| description.set(event_target_value(&event))></textarea>
                    </label>
                    <label class="field">"Functional requirements (one per line)"
                        <textarea class="textarea small-area" placeholder="Numbered internally; these do not become acceptance bullets." prop:value=move || functional.get() on:input=move |event| functional.set(event_target_value(&event))></textarea>
                    </label>
                    <label class="field">"Observable acceptance expectations (one per line)"
                        <textarea class="textarea small-area" placeholder="GET /health returns HTTP 200 with status ok" prop:value=move || expectations.get() on:input=move |event| expectations.set(event_target_value(&event))></textarea>
                    </label>
                    <label class="field">"Constraints (one per line)"
                        <textarea class="textarea small-area" prop:value=move || constraints.get() on:input=move |event| constraints.set(event_target_value(&event))></textarea>
                    </label>
                    <label class="field">"Non-goals (one per line)"
                        <textarea class="textarea small-area" prop:value=move || non_goals.get() on:input=move |event| non_goals.set(event_target_value(&event))></textarea>
                    </label>
                </div>

                <details class="intake-advanced">
                    <summary>"Acceptance contract, template and planning metadata"</summary>
                    <div class="form-grid" style="margin-top:12px">
                        <label class="field span-2">"Acceptance contract JSON (optional)"
                            <textarea class="textarea code-area" placeholder=r#"{"schemaVersion":1,"supportedClass":"aspnetcore-http-net10","scenarios":[...]}"# prop:value=move || contract_json.get() on:input=move |event| { contract_json.set(event_target_value(&event)); preview.set(None); }></textarea>
                        </label>
                        <div class="form-grid span-2">
                            <label class="field">"Approved template"
                                <select class="select" on:change=move |event| {
                                    let value = event_target_value(&event);
                                    selected_template.set(value.clone());
                                    template_reference.set(value);
                                }>
                                    <option value="">"Custom requirement (no template)"</option>
                                    {move || match templates.get().flatten() {
                                        Some(Ok(list)) => list.into_iter().filter(|template| template.approved && !template.revoked).map(|template| {
                                            let value = format!("{}@{}", template.template_id, template.template_version);
                                            view! { <option value=value>{template.template_id}" v"{template.template_version}</option> }
                                        }).collect_view().into_any(),
                                        _ => view! { <option value="" disabled>"Loading approved templates…"</option> }.into_any(),
                                    }}
                                </select>
                            </label>
                            <label class="field">"Template reference"
                                <input class="input" placeholder="template-id@version" prop:value=move || template_reference.get() on:input=move |event| template_reference.set(event_target_value(&event))/>
                            </label>
                        </div>
                        <label class="field">"Template parameters JSON (optional)"
                            <input class="input" placeholder=r#"{"path":"/health"}"# prop:value=move || template_parameters.get() on:input=move |event| template_parameters.set(event_target_value(&event))/>
                        </label>
                        <label class="field">"Priority (planning only)"
                            <select class="select" on:change=move |event| priority.set(event_target_value(&event))>
                                <option value="normal">"Normal"</option>
                                <option value="high">"High"</option>
                                <option value="urgent">"Urgent"</option>
                                <option value="low">"Low"</option>
                            </select>
                        </label>
                        <label class="field">"Deadline (planning only)"
                            <input class="input" type="date" prop:value=move || deadline.get() on:input=move |event| deadline.set(event_target_value(&event))/>
                        </label>
                        <label class="field">"Tags (comma separated, planning only)"
                            <input class="input" prop:value=move || tags.get() on:input=move |event| tags.set(event_target_value(&event))/>
                        </label>
                        <label class="field">"Related links (one per line, planning only)"
                            <input class="input" prop:value=move || links.get() on:input=move |event| links.set(event_target_value(&event))/>
                        </label>
                        <label class="field span-2">"Operator notes (planning only)"
                            <textarea class="textarea small-area" prop:value=move || notes.get() on:input=move |event| notes.set(event_target_value(&event))></textarea>
                        </label>
                    </div>
                    <p class="form-hint">
                        "Priority, deadline, tags, links and notes are stored by the control plane and are never converted into factory scheduling or execution authority."
                    </p>
                </details>

                <div class="row wrap" style="margin-top:16px">
                    <button class="btn" disabled=move || busy.get() on:click=on_preview>"Validate with factory parser"</button>
                    <label class="check">
                        <input type="checkbox" prop:checked=move || confirmed.get() on:change=move |event| confirmed.set(event_target_checked(&event))/>
                        <span>"I have inspected the generated requirement and confirm it is the exact document to submit."</span>
                    </label>
                    <span class="spacer"></span>
                    <button class="btn btn-primary" disabled=move || busy.get() || !confirmed.get() || preview.get().map(|value| !value.valid).unwrap_or(true) on:click=on_submit>
                        {move || if busy.get() { "Submitting…" } else { "Submit to factory" }}
                    </button>
                </div>

                {move || error.get().map(|issue| view! { <div class="form-error" role="alert">{issue.message}</div> })}
                {move || preview.get().map(|value| view! {
                    <div class="preview-result">
                        <div class="row-between">
                            <strong>{if value.valid { "Factory parser accepted this document" } else { "Factory parser rejected this document" }}</strong>
                            <span class="tiny mono">{short_digest(value.requirement_digest.as_deref())}</span>
                        </div>
                        {(!value.errors.is_empty()).then(|| view! {
                            <ul class="form-errors">{value.errors.iter().map(|error| view! { <li>{error.clone()}</li> }).collect_view()}</ul>
                        })}
                        {(!value.contract_scenarios.is_empty()).then(|| view! {
                            <div class="tiny faint">"Contract scenarios: "{value.contract_scenarios.iter().map(|scenario| scenario.id.clone()).collect::<Vec<_>>().join(", ")}</div>
                        })}
                    </div>
                })}
                <div class="row" style="margin-top:8px">
                    <span class="tiny faint">{move || format!("{} planning error(s)", build_draft().validation_errors().len())}</span>
                </div>

                <div class="intake-preview-grid">
                    <div class="stack">
                        <div class="label">"Generated requirement (exact bytes sent after confirmation)"</div>
                        <pre class="requirement-draft">{markdown}</pre>
                    </div>
                    <div class="stack">
                        <div class="label">"Rendered preview"</div>
                        <div class="md panel" style="padding:16px" inner_html=rendered></div>
                    </div>
                </div>
            </div>
        </section>
    }
}

fn lines(value: &str) -> Vec<String> {
    value
        .lines()
        .map(str::trim)
        .filter(|line| !line.is_empty())
        .map(str::to_string)
        .collect()
}

fn split_lines(value: &str) -> Vec<String> {
    lines(value)
}

fn split_csv(value: &str) -> Vec<String> {
    value
        .split(',')
        .map(str::trim)
        .filter(|value| !value.is_empty())
        .map(str::to_string)
        .collect()
}

fn non_empty(value: &str) -> Option<String> {
    (!value.trim().is_empty()).then(|| value.trim().to_string())
}

fn timestamp(value: Option<&str>) -> String {
    let Some(value) = value else {
        return "Not recorded".to_string();
    };
    chrono::DateTime::parse_from_rfc3339(value)
        .map(|value| {
            value
                .with_timezone(&chrono::Utc)
                .format("%Y-%m-%d %H:%M:%S UTC")
                .to_string()
        })
        .unwrap_or_else(|_| value.to_string())
}
