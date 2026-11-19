use leptos::prelude::*;
use leptos::task::spawn_local;
use leptos_router::NavigateOptions;
use leptos_router::hooks::{use_location, use_navigate, use_params_map};
use percent_encoding::{AsciiSet, CONTROLS, percent_decode_str, utf8_percent_encode};
use serde::{Deserialize, Serialize};

use control_plane_domain::{
    Page, Preview, RequirementDraft, SubmitRequest, WorkItemSummary,
    indicator_label, indicator_tone, render_markdown, short_digest, state_label, state_tone,
};

use crate::api;
use crate::components::feedback::EmptyState;
use crate::components::layout::FilterBar;
use crate::components::status::{EntityIdentity, StatusIndicator};
use crate::components::table::{Column, DataTable, Sort, SortDirection, TableState};
use crate::components::ui::{EmptyBlock, LoadingBlock, error_view, time_ago};
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

/// A named presentation of the queue. It never carries authority: loading one only restores the
/// search, filters, sort and view the operator saved. Named views live in this browser because
/// they are presentation state, while the URL carries the filters so a link stays reproducible.
#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
struct SavedView {
    #[serde(default)]
    name: String,
    #[serde(default)]
    state: String,
    #[serde(default)]
    indicator: String,
    #[serde(default)]
    search: String,
    #[serde(default)]
    sort: String,
    #[serde(default)]
    view: String,
}

impl SavedView {
    /// A view stored by an older build (or edited by hand) is repaired instead of feeding the
    /// adapter an ordering or state it cannot produce.
    fn normalized(mut self) -> Self {
        if !SORT_OPTIONS.iter().any(|(value, _)| self.sort == *value) {
            self.sort = "updated-desc".to_string();
        }
        if !known_states().iter().any(|value| self.state == *value) {
            self.state.clear();
        }
        if !ATTENTION_INDICATORS
            .iter()
            .any(|value| self.indicator == *value)
        {
            self.indicator.clear();
        }
        self
    }

    fn matches(
        &self,
        queue_view: QueueView,
        search: &str,
        state: &str,
        indicator: &str,
        sort: &str,
    ) -> bool {
        self.view == queue_view.as_str()
            && self.search == search
            && self.state == state
            && self.indicator == indicator
            && self.sort == sort
    }
}

/// The addressable part of the queue: every filter that changes which rows or what order the
/// operator sees. The URL is the source of truth for these, so Back/Forward and a pasted link
/// reproduce the same list.
#[derive(Debug, Clone, PartialEq, Eq)]
struct QueueQuery {
    view: QueueView,
    search: String,
    state: String,
    attention: String,
    sort: String,
}

impl Default for QueueQuery {
    fn default() -> Self {
        Self {
            view: QueueView::Table,
            search: String::new(),
            state: String::new(),
            attention: String::new(),
            sort: "updated-desc".to_string(),
        }
    }
}

/// Characters that may not appear literally in a query value. `-`, `_` and `.` stay readable.
const QUERY_VALUE: &AsciiSet = &CONTROLS
    .add(b' ')
    .add(b'"')
    .add(b'#')
    .add(b'$')
    .add(b'%')
    .add(b'&')
    .add(b'+')
    .add(b',')
    .add(b'/')
    .add(b':')
    .add(b';')
    .add(b'<')
    .add(b'=')
    .add(b'>')
    .add(b'?')
    .add(b'@')
    .add(b'[')
    .add(b']')
    .add(b'^')
    .add(b'`')
    .add(b'{')
    .add(b'|')
    .add(b'}');

fn encode_query_value(value: &str) -> String {
    utf8_percent_encode(value, QUERY_VALUE).to_string()
}

fn decode_query_value(value: &str) -> String {
    percent_decode_str(&value.replace('+', " "))
        .decode_utf8_lossy()
        .into_owned()
}

/// Parses the queue parameters. Unknown states, attention codes and orderings are dropped rather
/// than forwarded to the adapter: a hand-edited URL must not invent a filter the factory cannot
/// mean.
fn parse_queue_query(raw: &str) -> QueueQuery {
    let mut query = QueueQuery::default();
    for pair in raw.trim_start_matches('?').split('&') {
        let Some((key, value)) = pair.split_once('=') else {
            continue;
        };
        let value = decode_query_value(value);
        match key {
            "view" => query.view = QueueView::from_str(&value),
            "q" => query.search = value,
            "state" => {
                if known_states().iter().any(|known| value == *known) {
                    query.state = value;
                }
            }
            "attention" => {
                if ATTENTION_INDICATORS
                    .iter()
                    .any(|known| value == *known)
                {
                    query.attention = value;
                }
            }
            "sort" => {
                if SORT_OPTIONS.iter().any(|(known, _)| value == *known) {
                    query.sort = value;
                }
            }
            _ => {}
        }
    }
    query
}

/// Serialises only the non-default values, so the bare queue URL stays short and stable.
fn queue_query_string(
    view: QueueView,
    search: &str,
    state: &str,
    attention: &str,
    sort: &str,
) -> String {
    let mut parts: Vec<String> = Vec::new();
    if view != QueueView::Table {
        parts.push(format!("view={}", encode_query_value(view.as_str())));
    }
    if !search.trim().is_empty() {
        parts.push(format!("q={}", encode_query_value(search)));
    }
    if !state.is_empty() {
        parts.push(format!("state={}", encode_query_value(state)));
    }
    if !attention.is_empty() {
        parts.push(format!("attention={}", encode_query_value(attention)));
    }
    if sort != "updated-desc" {
        parts.push(format!("sort={}", encode_query_value(sort)));
    }
    parts.join("&")
}

#[component]
pub fn WorkQueuePage() -> impl IntoView {
    let context = AppContext::provide();
    let params = use_params_map();
    let location = use_location();
    let navigate = use_navigate();
    let instance = Signal::derive(move || params.get().get("instance").unwrap_or_default());

    // The URL is read before the controls are created, so a linked or reloaded queue starts on the
    // same rows without a second request.
    let initial = parse_queue_query(&location.search.get_untracked());
    let view = RwSignal::new(initial.view);
    let state = RwSignal::new(initial.state);
    let indicator = RwSignal::new(initial.attention);
    let search = RwSignal::new(initial.search);
    let sort = RwSignal::new(initial.sort);
    let show_intake = RwSignal::new(false);
    let saved = RwSignal::new(Vec::<SavedView>::new());
    let saved_name = RwSignal::new(String::new());
    let active_view = RwSignal::new(Option::<String>::None);
    let refresh = context.refresh;
    let filters_active = Signal::derive(move || {
        !search.get().trim().is_empty() || !state.get().is_empty() || !indicator.get().is_empty()
    });
    let clear_filters = Callback::new(move |_| {
        search.set(String::new());
        state.set(String::new());
        indicator.set(String::new());
    });
    // A header click reuses the adapter's own orderings. There is no client-side resort: the
    // visible ordering is always one the adapter can produce.
    let on_sort = Callback::new(move |key: &'static str| {
        let next = match key {
            "title" => "title",
            "state" => "state",
            _ => "updated-desc",
        };
        sort.set(next.to_string());
    });

    // Browser Back/Forward or a pasted link changes the URL; mirror it into the controls so the
    // visible filters always describe the rows on screen. Nothing here writes the URL.
    {
        let location = location.clone();
        Effect::new(move |_| {
            let parsed = parse_queue_query(&location.search.get());
            if parsed.search != search.get_untracked() {
                search.set(parsed.search);
            }
            if parsed.state != state.get_untracked() {
                state.set(parsed.state);
            }
            if parsed.attention != indicator.get_untracked() {
                indicator.set(parsed.attention);
            }
            if parsed.sort != sort.get_untracked() {
                sort.set(parsed.sort);
            }
            if parsed.view != view.get_untracked() {
                view.set(parsed.view);
            }
        });
    }

    // Filter changes replace the current history entry: typing a search must not fill Back with
    // one entry per keystroke. The URL parser above then sees the normalized value it wrote.
    {
        let location = location.clone();
        let navigate = navigate.clone();
        Effect::new(move |_| {
            let query = queue_query_string(
                view.get(),
                &search.get(),
                &state.get(),
                &indicator.get(),
                &sort.get(),
            );
            if query == location.search.get_untracked() {
                return;
            }
            let path = location.pathname.get_untracked();
            let url = if query.is_empty() {
                path
            } else {
                format!("{path}?{query}")
            };
            navigate(
                &url,
                NavigateOptions {
                    replace: true,
                    scroll: false,
                    ..Default::default()
                },
            );
        });
    }

    #[cfg(target_arch = "wasm32")]
    {
        Effect::new(move |_| {
            if let Some(storage) =
                web_sys::window().and_then(|window| window.local_storage().ok().flatten())
            {
                if let Ok(Some(raw)) = storage.get_item("cp_work_views") {
                    if let Ok(views) = serde_json::from_str::<Vec<SavedView>>(&raw) {
                        let views = views
                            .into_iter()
                            .map(SavedView::normalized)
                            .collect::<Vec<_>>();
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
        let saved_view = saved_view.normalized();
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

    let warn = move |message: &str| {
        context.toast.set(Some(crate::context::Toast {
            kind: "warn".to_string(),
            message: message.to_string(),
            detail: None,
        }));
    };

    // Some(saved view name, whether the current controls still match it). This is the visible
    // active-view indicator; it compares every filter, not only the name.
    let active_view_state = Signal::derive(move || {
        let name = active_view.get()?;
        let saved_view = saved
            .get()
            .into_iter()
            .find(|candidate| candidate.name == name)?;
        Some(saved_view.matches(
            view.get(),
            &search.get(),
            &state.get(),
            &indicator.get(),
            &sort.get(),
        ))
    });

    let save_view = move || {
        let name = saved_name.get().trim().to_string();
        if name.is_empty() {
            warn("Name the view before saving it.");
            return;
        }

        let saved_view = SavedView {
            name: name.clone(),
            state: state.get(),
            indicator: indicator.get(),
            search: search.get(),
            sort: sort.get(),
            view: view.get().as_str().to_string(),
        };
        let mut views = saved.get();
        views.retain(|candidate| candidate.name != saved_view.name);
        views.push(saved_view);
        views.truncate(12);
        saved.set(views.clone());
        persist_saved_views(&views);
        active_view.set(Some(name));
        saved_name.set(String::new());
        context.notify_success("Saved view stored in this browser.");
    };

    let rename_view = move || {
        let Some(active) = active_view.get() else {
            warn("Select a saved view before renaming it.");
            return;
        };
        let name = saved_name.get().trim().to_string();
        if name.is_empty() {
            warn("Type the new name in the view name field, then choose Rename.");
            return;
        }
        if name == active {
            return;
        }
        let mut views = saved.get();
        if views.iter().any(|candidate| candidate.name == name) {
            warn("Another saved view already uses that name.");
            return;
        }
        let Some(entry) = views
            .iter_mut()
            .find(|candidate| candidate.name == active)
        else {
            active_view.set(None);
            warn("That saved view is no longer in this browser.");
            return;
        };
        entry.name = name.clone();
        saved.set(views.clone());
        persist_saved_views(&views);
        active_view.set(Some(name));
        saved_name.set(String::new());
        context.notify_success("Saved view renamed.");
    };

    let delete_view = move || {
        let Some(active) = active_view.get() else {
            warn("Select a saved view before deleting it.");
            return;
        };
        let mut views = saved.get();
        views.retain(|candidate| candidate.name != active);
        saved.set(views.clone());
        persist_saved_views(&views);
        active_view.set(None);
        context.notify_success("Saved view deleted. The current filters are unchanged.");
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

            <FilterBar
                label="Work queue filters".to_string()
                leading=view! {
                    <div class="field search">
                        <label class="field-label" for="queue-search">"Search work items"</label>
                        <input
                            class="input queue-search"
                            id="queue-search"
                            type="search"
                            placeholder="ID, title or digest"
                            prop:value=move || search.get()
                            on:input=move |event| search.set(event_target_value(&event))
                        />
                    </div>
                }.into_any()
                fields=view! {
                    <div class="field">
                        <label class="field-label" for="queue-state">"Factory state"</label>
                        <select
                            class="select"
                            id="queue-state"
                            on:change=move |event| state.set(event_target_value(&event))
                        >
                            <option value="">"All factory states"</option>
                            {known_states().into_iter().map(|value| view! {
                                <option value=value selected=move || state.get() == value>{state_label(value)}</option>
                            }).collect_view()}
                        </select>
                    </div>
                    <div class="field">
                        <label class="field-label" for="queue-indicator">"Attention"</label>
                        <select
                            class="select"
                            id="queue-indicator"
                            on:change=move |event| indicator.set(event_target_value(&event))
                        >
                            <option value="">"All attention indicators"</option>
                            {ATTENTION_INDICATORS.into_iter().map(|value| view! {
                                <option value=value selected=move || indicator.get() == value>{indicator_label(Some(value))}</option>
                            }).collect_view()}
                        </select>
                    </div>
                    <div class="field">
                        <label class="field-label" for="queue-sort">"Sort"</label>
                        <select
                            class="select"
                            id="queue-sort"
                            on:change=move |event| sort.set(event_target_value(&event))
                        >
                            {SORT_OPTIONS.into_iter().map(|(value, label)| view! {
                                <option value=value selected=move || sort.get() == value>{label}</option>
                            }).collect_view()}
                        </select>
                    </div>
                }.into_any()
                active=filters_active
                on_clear=clear_filters
                secondary=Some(view! {
                    <div class="segmented" role="group" aria-label="Work queue view">
                        <button type="button" class:active=move || view.get() == QueueView::Table aria-pressed=move || (view.get() == QueueView::Table).to_string() on:click=move |_| view.set(QueueView::Table)>"Table"</button>
                        <button type="button" class:active=move || view.get() == QueueView::Kanban aria-pressed=move || (view.get() == QueueView::Kanban).to_string() on:click=move |_| view.set(QueueView::Kanban)>"Board"</button>
                        <button type="button" class:active=move || view.get() == QueueView::Prioritized aria-pressed=move || (view.get() == QueueView::Prioritized).to_string() on:click=move |_| view.set(QueueView::Prioritized)>"Priority"</button>
                    </div>
                    <div class="saved-views" role="group" aria-label="Saved views">
                        <label class="field-label" for="queue-saved-view">"Saved view"</label>
                        <select
                            class="select"
                            id="queue-saved-view"
                            prop:value=move || active_view.get().unwrap_or_default()
                            on:change=move |event| {
                                let name = event_target_value(&event);
                                if name.is_empty() {
                                    active_view.set(None);
                                    return;
                                }
                                if let Some(saved_view) = saved
                                    .get()
                                    .into_iter()
                                    .find(|candidate| candidate.name == name)
                                {
                                    apply_view(saved_view);
                                    active_view.set(Some(name));
                                }
                            }
                        >
                            <option value="">"No saved view"</option>
                            {move || saved.get().into_iter().map(|saved_view| {
                                let name = saved_view.name.clone();
                                let value = name.clone();
                                view! { <option value=value>{name}</option> }
                            }).collect_view()}
                        </select>
                        <input
                            class="input"
                            aria-label="View name"
                            placeholder="View name"
                            prop:value=move || saved_name.get()
                            on:input=move |event| saved_name.set(event_target_value(&event))
                        />
                        <button class="btn btn-sm" type="button" on:click=move |_| save_view()>"Save view"</button>
                        <button
                            class="btn btn-sm btn-quiet"
                            type="button"
                            disabled=move || active_view.get().is_none()
                            title=move || if active_view.get().is_some() { "Rename the active saved view to the name in the field" } else { "Select a saved view first" }
                            on:click=move |_| rename_view()
                        >"Rename"</button>
                        <button
                            class="btn btn-sm btn-quiet"
                            type="button"
                            disabled=move || active_view.get().is_none()
                            title=move || if active_view.get().is_some() { "Delete the active saved view from this browser" } else { "Select a saved view first" }
                            on:click=move |_| delete_view()
                        >"Delete"</button>
                        <span class="saved-view-state" role="status" aria-live="polite">
                            {move || match active_view_state.get() {
                                Some(true) => view! {
                                    <span>"Saved view “"{active_view.get().unwrap_or_default()}"” is active."</span>
                                }.into_any(),
                                Some(false) => view! {
                                    <span class="saved-view-dirty">"“"{active_view.get().unwrap_or_default()}"” has unsaved changes."</span>
                                }.into_any(),
                                None => view! {
                                    <span class="faint">"No saved view active."</span>
                                }.into_any(),
                            }}
                        </span>
                        <span class="tiny faint saved-view-note">
                            "Named views are stored in this browser only. The filters themselves are in the URL, so a copied link reproduces the list."
                        </span>
                    </div>
                }.into_any())
            />
            <Suspense fallback=move || view! { <LoadingBlock label="Loading durable work items…"/> }>
                {move || match work.get().flatten() {
                    None => view! { <LoadingBlock label="Loading durable work items…"/> }.into_any(),
                    Some(Err(error)) => view! { {error_view(error)} }.into_any(),
                    Some(Ok(page)) => view! {
                        <QueueSummary page=page.clone()/>
                        {match view.get() {
                            QueueView::Table => view! {
                                <WorkTable
                                    page=page.clone()
                                    instance=instance
                                    sort=sort
                                    on_sort=on_sort
                                    filters_active=filters_active
                                    on_clear=clear_filters
                                />
                            }.into_any(),
                            QueueView::Kanban => view! { <WorkKanban page=page.clone() instance=instance/> }.into_any(),
                            QueueView::Prioritized => view! { <WorkPriority page=page.clone() instance=instance/> }.into_any(),
                        }}
                    }.into_any(),
                }}
            </Suspense>
        </section>
    }
}

/// Attention indicators an operator can filter by. These are derived markers, not factory states.
const ATTENTION_INDICATORS: [&str; 9] = [
    "waiting-contract-approval",
    "waiting-merge-approval",
    "active-implementation",
    "running-verification",
    "needs-rework",
    "blocked",
    "terminal-failure",
    "released",
    "released-unverified",
];

/// The orderings the adapter can actually produce, named in operator words.
const SORT_OPTIONS: [(&str, &str); 5] = [
    ("updated-desc", "Recently updated first"),
    ("created-desc", "Newest requirement first"),
    ("created-asc", "Oldest requirement first"),
    ("state", "Factory state A to Z"),
    ("title", "Title A to Z"),
];

/// What the selected ordering means, in words, next to the table it orders.
fn sort_definition(sort: &str) -> &'static str {
    match sort {
        "created-desc" => {
            "Ordered by requirement creation time, newest first. Updated time is not the key."
        }
        "created-asc" => {
            "Ordered by requirement creation time, oldest first. Updated time is not the key."
        }
        "state" => {
            "Ordered by factory state A to Z, then by most recently updated first inside a state."
        }
        "title" => "Ordered by title A to Z, ignoring case.",
        _ => "Ordered by most recently updated first, using the recorded ISO timestamps.",
    }
}

/// Maps the adapter ordering onto the column whose values it actually sorts by, so `aria-sort`
/// never claims an ordering the table is not showing.
fn table_sort(sort: &str) -> Option<Sort> {
    match sort {
        "title" => Some(Sort {
            key: "title",
            direction: SortDirection::Ascending,
        }),
        "state" => Some(Sort {
            key: "state",
            direction: SortDirection::Ascending,
        }),
        "updated-desc" => Some(Sort {
            key: "updated",
            direction: SortDirection::Descending,
        }),
        _ => None,
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

/// Advisory next step for a row. It is derived from the factory's authoritative flags and state;
/// it never replaces the governed action on the work item itself.
fn next_step_label(item: &WorkItemSummary) -> &'static str {
    if item.awaiting_contract_approval {
        return "Freeze or reject the contract";
    }
    if item.awaiting_merge_approval {
        return "Authorize merge";
    }
    match item.state.as_str() {
        "ReadyForImplementation" => "Start dispatch",
        "NeedsRework" => "Dispatch a rework attempt",
        "Released" | "OperationallyValidated" => "Start supervised serve",
        "InImplementation" | "InReview" | "DeliveryValidation" => "Monitor execution",
        "Blocked" | "Failed" | "Cancelled" | "Closed" => "Inspect evidence",
        _ => "Inspect evidence",
    }
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
        <div class="queue-summary" aria-label="Queue summary">
            <span><strong>{page.items.len()}</strong> " shown"</span>
            <span><strong>{awaiting}</strong> " awaiting authorization"</span>
            <span><strong>{active}</strong> " active execution"</span>
            <span><strong>{failed}</strong> " terminal failures"</span>
        </div>
        <details class="definitions">
            <summary>"How these counts are defined"</summary>
            <p class="small muted">
                "Shown counts the rows after filters. Awaiting authorization counts rows with a contract or merge approval pending. Active execution counts In implementation and In review. Terminal failures counts the factory state Failed only; a failed verification on an In-review item is an attention indicator, not a terminal failure. Overview uses the adapter's failed_work_items definition, so the two counts can differ; each label names its source."
            </p>
        </details>
    }
}

#[component]
fn WorkTable(
    page: Page<WorkItemSummary>,
    instance: Signal<String>,
    #[prop(into)] sort: Signal<String>,
    on_sort: Callback<&'static str>,
    filters_active: Signal<bool>,
    on_clear: Callback<()>,
) -> impl IntoView {
    let items = page.items;
    let row_count = items.len();
    let state = if row_count == 0 {
        TableState::Empty
    } else {
        TableState::Populated
    };
    let current_sort = sort.get_untracked();
    let sort_state = table_sort(&current_sort);
    let caption = format!("Work items. {}", sort_definition(&current_sort));
    let mobile = items.clone();
    view! {
        <div class="queue-table-wrap">
            <DataTable
                caption=caption
                columns=vec![
                    Column::sortable("Work item", "title"),
                    Column::sortable("State", "state"),
                    Column::text("Attention"),
                    Column::text("Priority"),
                    Column::text("Next step"),
                    Column::sortable("Updated", "updated"),
                    Column::numeric("Events", None),
                    Column::text("Last failure"),
                ]
                rows=row_count
                state=state
                sort=sort_state
                on_sort=Some(on_sort)
                empty=Some(view! {
                    <EmptyState
                        title="No work matches these filters"
                        detail="Nothing is hidden: the count is zero for the current search, state and attention filters."
                        action=Some(view! {
                            <button
                                class="btn btn-sm"
                                type="button"
                                disabled=move || !filters_active.get()
                                on:click=move |_| on_clear.run(())
                            >"Clear filters"</button>
                        }.into_any())
                    />
                }.into_any())
            >
                {items.into_iter().map(|item| {
                    let href = format!("/i/{}/work/{}", instance.get(), item.work_item_id);
                    let title = item.title.clone();
                    let work_item_id = item.work_item_id.clone();
                    let state = item.state.clone();
                    let state_text = state_label(&state).to_string();
                    let state_tone_value = state_tone(&state);
                    let indicator = item.derived_indicator.clone();
                    let indicator_text = indicator_label(indicator.as_deref()).to_string();
                    let indicator_tone_value = indicator_tone(indicator.as_deref());
                    let show_indicator = indicator.is_some() && indicator_text != state_text;
                    let priority = declared_priority_label(item.planning.priority.as_deref());
                    let event_count = item.event_count;
                    let updated = item.updated_at_utc.clone();
                    let updated_title = timestamp(updated.as_deref());
                    let updated_text = time_ago(updated.as_deref());
                    let last_failure = item.last_failure.clone().unwrap_or_else(|| "—".to_string());
                    let next_step = next_step_label(&item);
                    let href_for_next = href.clone();
                    view! {
                        <tr>
                            <td>
                                <a href=href><strong>{title}</strong></a>
                                <EntityIdentity label="Work item" value=work_item_id/>
                            </td>
                            <td><StatusIndicator label=state_text.clone() tone=state_tone_value raw=state.clone()/></td>
                            <td>
                                {if show_indicator {
                                    view! {
                                        <StatusIndicator
                                            label=indicator_text.clone()
                                            tone=indicator_tone_value
                                            raw=indicator.clone().unwrap_or_default()
                                        />
                                    }.into_any()
                                } else {
                                    view! { <span class="faint small">"None recorded"</span> }.into_any()
                                }}
                            </td>
                            <td>{priority}</td>
                            <td>
                                <a
                                    class="queue-next"
                                    href=href_for_next
                                    title="Opens the work item, where the governed action is available"
                                >
                                    {next_step}" →"
                                </a>
                            </td>
                            <td title=updated_title>{updated_text}</td>
                            <td class="numeric">{event_count}</td>
                            <td class="tiny">{last_failure}</td>
                        </tr>
                    }
                }).collect_view()}
            </DataTable>
        </div>
        {(!mobile.is_empty()).then(|| view! {
            <div class="queue-mobile-list" aria-label="Work items">
                {mobile.into_iter().map(|item| {
                    let href = format!("/i/{}/work/{}", instance.get(), item.work_item_id);
                    let state_text = state_label(&item.state).to_string();
                    let indicator_text = item
                        .derived_indicator
                        .as_ref()
                        .map(|value| indicator_label(Some(value)).to_string())
                        .filter(|text| *text != state_text);
                    let priority = declared_priority_card_label(item.planning.priority.as_deref());
                    let updated = time_ago(item.updated_at_utc.as_deref());
                    let last_failure = item
                        .last_failure
                        .clone()
                        .filter(|failure| failure != "—");
                    let next_step = next_step_label(&item);
                    view! {
                        <a class="queue-mobile-card" href=href>
                            <span class="queue-mobile-title">{item.title.clone()}</span>
                            <span class="queue-mobile-id mono">{item.work_item_id.clone()}</span>
                            <span class="queue-mobile-facts">
                                <span>{state_text}</span>
                                {indicator_text.map(|text| view! { <span class="queue-mobile-attention">{text}</span> })}
                                <span>{priority}</span>
                                <span>{format!("{} events · {}", item.event_count, updated)}</span>
                            </span>
                            {last_failure.map(|failure| view! { <span class="queue-mobile-failure mono">{failure}</span> })}
                            <span class="queue-mobile-next">{next_step}" →"</span>
                        </a>
                    }
                }).collect_view()}
            </div>
        })}
    }
}
/// The board's scroll region is addressed by id because the DOM node is the only thing that knows
/// how far the board has scrolled. One column plus its gap is the arrow step.
const KANBAN_ID: &str = "queue-kanban";
const KANBAN_COLUMN_STEP: f64 = 296.0;

fn scroll_kanban(direction: i32) {
    #[cfg(target_arch = "wasm32")]
    {
        if let Some(document) = web_sys::window().and_then(|window| window.document()) {
            if let Some(element) = document.get_element_by_id(KANBAN_ID) {
                let left = element.scroll_left() as f64 + f64::from(direction) * KANBAN_COLUMN_STEP;
                element.set_scroll_left(left.max(0.0) as i32);
            }
        }
    }
    #[cfg(not(target_arch = "wasm32"))]
    let _ = (direction, KANBAN_COLUMN_STEP);
}

#[cfg(target_arch = "wasm32")]
fn persist_saved_views(views: &[SavedView]) {
    if let Some(storage) =
        web_sys::window().and_then(|window| window.local_storage().ok().flatten())
    {
        if let Ok(raw) = serde_json::to_string(views) {
            let _ = storage.set_item("cp_work_views", &raw);
        }
    }
}

#[cfg(not(target_arch = "wasm32"))]
fn persist_saved_views(_views: &[SavedView]) {}

/// Actionable urgency is derived only from authoritative flags and state. It is not a factory
/// state, it is not operator-owned, and the priority view states this definition next to the list.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord)]
enum PriorityUrgency {
    DecisionWaiting,
    Stopped,
    Executing,
    Quiet,
}

impl PriorityUrgency {
    fn classify(item: &WorkItemSummary) -> Self {
        if item.awaiting_contract_approval || item.awaiting_merge_approval {
            Self::DecisionWaiting
        } else if matches!(item.state.as_str(), "Blocked" | "Failed" | "NeedsRework") {
            Self::Stopped
        } else if matches!(
            item.state.as_str(),
            "InImplementation" | "InReview" | "DeliveryValidation"
        ) {
            Self::Executing
        } else {
            Self::Quiet
        }
    }

    fn label(self) -> &'static str {
        match self {
            Self::DecisionWaiting => "Decision waiting",
            Self::Stopped => "Stopped, needs a decision",
            Self::Executing => "Executing now",
            Self::Quiet => "No immediate action",
        }
    }
}

fn declared_priority_label(priority: Option<&str>) -> String {
    match priority.map(str::trim).filter(|value| !value.is_empty()) {
        None => "Not declared".to_string(),
        Some(value) if value.eq_ignore_ascii_case("urgent") => "Urgent".to_string(),
        Some(value) if value.eq_ignore_ascii_case("high") => "High".to_string(),
        Some(value) if value.eq_ignore_ascii_case("normal") => "Normal".to_string(),
        Some(value) if value.eq_ignore_ascii_case("low") => "Low".to_string(),
        Some(value) => value.to_string(),
    }
}

fn declared_priority_rank(priority: Option<&str>) -> u8 {
    match priority.map(str::trim).filter(|value| !value.is_empty()) {
        Some(value) if value.eq_ignore_ascii_case("urgent") => 0,
        Some(value) if value.eq_ignore_ascii_case("high") => 1,
        Some(value) if value.eq_ignore_ascii_case("normal") => 2,
        Some(value) if value.eq_ignore_ascii_case("low") => 3,
        _ => 4,
    }
}

/// A short form for a card or list cell: "High priority", or an explicit absence instead of a
/// blank or a raw enum value.
fn declared_priority_card_label(priority: Option<&str>) -> String {
    match priority.map(str::trim).filter(|value| !value.is_empty()) {
        Some(value) => format!("{} priority", declared_priority_label(Some(value))),
        None => "No declared priority".to_string(),
    }
}

/// The definition displayed next to the priority list. It states the order the rows below are
/// actually in, and why this view is not the table's adapter ordering.
fn priority_definition() -> &'static str {
    "Rows are ordered by actionable urgency first, then by the declared planning priority, then by the most recent update. Actionable urgency comes from authoritative factory flags and state: work waiting for a contract or merge decision comes first, then work the factory stopped (Blocked, Failed, Needs rework), then work the factory is executing, then work with no immediate action. Declared priority is the operator-owned planning value (Urgent, High, Normal, Low or not declared) and never creates execution authority. The Table view instead displays one of the adapter's own orderings, selected by the sort control; this view never forwards its ordering to the adapter."
}

fn priority_order(items: Vec<WorkItemSummary>) -> Vec<WorkItemSummary> {
    let mut items = items;
    items.sort_by(|left, right| {
        PriorityUrgency::classify(left)
            .cmp(&PriorityUrgency::classify(right))
            .then_with(|| {
                declared_priority_rank(left.planning.priority.as_deref())
                    .cmp(&declared_priority_rank(right.planning.priority.as_deref()))
            })
            .then_with(|| right.updated_at_utc.cmp(&left.updated_at_utc))
            .then_with(|| left.work_item_id.cmp(&right.work_item_id))
    });
    items
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
        <div class="kanban-toolbar">
            <p class="kanban-hint" id="kanban-hint">
                "Seven stages, each 280 pixels wide. The board scrolls sideways: use Earlier/Later, the scrollbar, or focus the board and use the arrow keys. Empty stages stay short instead of showing a blank well."
            </p>
            <div class="kanban-nav" role="group" aria-label="Scroll the work board">
                <button class="btn btn-sm" type="button" on:click=move |_| scroll_kanban(-1)>
                    "← Earlier stages"
                </button>
                <button class="btn btn-sm" type="button" on:click=move |_| scroll_kanban(1)>
                    "Later stages →"
                </button>
            </div>
        </div>
        <div
            class="kanban-scroll"
            id=KANBAN_ID
            tabindex="0"
            role="region"
            aria-label="Work board with seven lifecycle stages"
            aria-describedby="kanban-hint"
        >
            <div class="kanban">
                {columns.into_iter().map(|(label, states)| {
                    let cards: Vec<WorkItemSummary> = page
                        .items
                        .iter()
                        .filter(|item| states.contains(&item.state.as_str()))
                        .cloned()
                        .collect();
                    let count = cards.len();
                    let empty = cards.is_empty();
                    view! {
                        <div class="kanban-column">
                            <div class="kanban-head">
                                <span>{label}</span>
                                <span class="tiny faint">{count}</span>
                            </div>
                            <div class="kanban-body">
                                {empty.then(|| view! {
                                    <p class="kanban-empty">"No work in this stage."</p>
                                })}
                                {cards.into_iter().map(|item| {
                                    let href = format!("/i/{}/work/{}", instance.get(), item.work_item_id);
                                    let title = item.title.clone();
                                    let work_item_id = item.work_item_id.clone();
                                    let state = item.state.clone();
                                    let state_text = state_label(&state).to_string();
                                    let tone = state_tone(&state);
                                    let indicator = indicator_label(item.derived_indicator.as_deref()).to_string();
                                    let show_indicator = item.derived_indicator.is_some() && indicator != state_text;
                                    let priority = declared_priority_card_label(item.planning.priority.as_deref());
                                    view! {
                                        <a class="kanban-card" href=href>
                                            <span class="kanban-card-title">{title}</span>
                                            <span class="kanban-card-id mono">{work_item_id}</span>
                                            <span class="kanban-card-facts">
                                                <StatusIndicator label=state_text.clone() tone=tone/>
                                                {show_indicator.then(|| view! {
                                                    <span class="kanban-card-attention">{indicator}</span>
                                                })}
                                            </span>
                                            <span class="kanban-card-priority">{priority}</span>
                                        </a>
                                    }
                                }).collect_view()}
                            </div>
                        </div>
                    }
                }).collect_view()}
            </div>
        </div>
        <p class="tiny faint kanban-note">
            "The board groups work by authoritative lifecycle state, not by priority or age. Each card names the state and its declared planning priority; the Priority view explains the ordering used there."
        </p>
    }
}

#[component]
fn WorkPriority(page: Page<WorkItemSummary>, instance: Signal<String>) -> impl IntoView {
    let items = priority_order(page.items);
    view! {
        <section class="priority-definition" aria-labelledby="priority-definition-title">
            <h2 class="section-title" id="priority-definition-title">"How the priority view orders work"</h2>
            <p>{priority_definition()}</p>
            <p class="tiny faint">
                "Computed in this browser from the rows the adapter returned for the current filters, with no extra request and no invented ordering."
            </p>
        </section>
        <div class="stack" aria-label="Work items in priority order">
            {if items.is_empty() {
                view! { <EmptyBlock title="No work in the queue" detail="Submit a requirement to create the first work item, or clear the filters."/> }.into_any()
            } else {
                items.into_iter().map(|item| {
                    let href = format!("/i/{}/work/{}", instance.get(), item.work_item_id);
                    let title = item.title.clone();
                    let state = item.state.clone();
                    let state_text = state_label(&state).to_string();
                    let tone = state_tone(&state);
                    let urgency = PriorityUrgency::classify(&item);
                    let priority = declared_priority_label(item.planning.priority.as_deref());
                    let updated = time_ago(item.updated_at_utc.as_deref());
                    let next_step = next_step_label(&item);
                    view! {
                        <a class="priority-row" href=href>
                            <div class="priority-main">
                                <div class="priority-title-line">
                                    <strong>{title}</strong>
                                    <StatusIndicator label=state_text tone=tone/>
                                </div>
                                <div class="priority-facts">
                                    <span><span class="faint">"Declared priority: "</span>{priority}</span>
                                    <span><span class="faint">"Actionable urgency: "</span>{urgency.label()}</span>
                                    <span><span class="faint">"Updated "</span>{updated}</span>
                                </div>
                                <div class="priority-next">{next_step}" →"</div>
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

#[cfg(test)]
mod tests {
    use super::*;

    fn summary(
        work_item_id: &str,
        state: &str,
        priority: Option<&str>,
        awaiting_contract: bool,
        awaiting_merge: bool,
        updated: Option<&str>,
    ) -> WorkItemSummary {
        let mut planning = control_plane_domain::Planning::default();
        planning.priority = priority.map(str::to_string);
        WorkItemSummary {
            work_item_id: work_item_id.to_string(),
            state: state.to_string(),
            version: 1,
            title: format!("Work {work_item_id}"),
            repository: "r".to_string(),
            requirement_digest: "d".to_string(),
            proposal_digest: None,
            frozen_contract_digest: None,
            review_digest: None,
            last_failure: None,
            event_count: 0,
            created_at_utc: None,
            updated_at_utc: updated.map(str::to_string),
            awaiting_contract_approval: awaiting_contract,
            awaiting_merge_approval: awaiting_merge,
            derived_indicator: None,
            planning,
        }
    }

    #[test]
    fn every_adapter_ordering_has_a_written_definition() {
        for (value, _) in SORT_OPTIONS {
            let definition = sort_definition(value);
            assert!(
                definition.len() > 20,
                "ordering {value} needs a sentence an operator can read"
            );
            assert!(
                definition.contains("Ordered") || definition.contains("ordered"),
                "ordering {value} must state what it orders by"
            );
        }
        assert_ne!(sort_definition("created-asc"), sort_definition("created-desc"));
        assert!(sort_definition("state").contains("state"));
    }

    #[test]
    fn aria_sort_names_only_the_columns_the_ordering_uses() {
        assert_eq!(table_sort("updated-desc").map(|sort| sort.key), Some("updated"));
        assert_eq!(table_sort("title").map(|sort| sort.key), Some("title"));
        assert_eq!(table_sort("state").map(|sort| sort.key), Some("state"));
        assert_eq!(
            table_sort("created-asc"),
            None,
            "a creation ordering must not claim the Updated column is sorted"
        );
        assert_eq!(table_sort("created-desc"), None);
    }

    #[test]
    fn the_next_step_never_promises_a_state_the_factory_did_not_reach() {
        let mut item = summary(
            "WORK-test",
            "ReadyForDelivery",
            None,
            false,
            true,
            None,
        );
        assert_eq!(next_step_label(&item), "Authorize merge");
        item.awaiting_merge_approval = false;
        item.awaiting_contract_approval = true;
        assert_eq!(next_step_label(&item), "Freeze or reject the contract");
        item.awaiting_contract_approval = false;
        item.state = "Failed".to_string();
        assert_eq!(next_step_label(&item), "Inspect evidence");
    }

    #[test]
    fn the_queue_url_round_trips_every_filter_and_the_view() {
        let search = "api & health / 100%";
        let url = queue_query_string(
            QueueView::Kanban,
            search,
            "InReview",
            "waiting-merge-approval",
            "title",
        );
        let parsed = parse_queue_query(&url);
        assert_eq!(parsed.view, QueueView::Kanban);
        assert_eq!(parsed.search, search);
        assert_eq!(parsed.state, "InReview");
        assert_eq!(parsed.attention, "waiting-merge-approval");
        assert_eq!(parsed.sort, "title");
    }

    #[test]
    fn the_queue_url_omits_defaults_so_the_bare_queue_stays_short() {
        assert_eq!(
            queue_query_string(QueueView::Table, "", "", "", "updated-desc"),
            ""
        );
        assert_eq!(
            queue_query_string(QueueView::Prioritized, "", "", "", "updated-desc"),
            "view=priority"
        );
    }

    #[test]
    fn a_hand_edited_url_cannot_invent_a_filter_the_adapter_cannot_mean() {
        let parsed = parse_queue_query(
            "state=NotAState&attention=not-real&sort=sideways&view=grid&q=kept",
        );
        assert_eq!(parsed.view, QueueView::Table);
        assert_eq!(parsed.state, "");
        assert_eq!(parsed.attention, "");
        assert_eq!(parsed.sort, "updated-desc");
        assert_eq!(parsed.search, "kept");
    }

    #[test]
    fn a_saved_view_is_active_only_when_every_filter_matches() {
        let saved = SavedView {
            name: "Mine".to_string(),
            state: "InReview".to_string(),
            indicator: "waiting-merge-approval".to_string(),
            search: "api".to_string(),
            sort: "title".to_string(),
            view: "kanban".to_string(),
        };
        assert!(saved.matches(
            QueueView::Kanban,
            "api",
            "InReview",
            "waiting-merge-approval",
            "title"
        ));
        assert!(!saved.matches(
            QueueView::Kanban,
            "api2",
            "InReview",
            "waiting-merge-approval",
            "title"
        ));
        assert!(!saved.matches(
            QueueView::Table,
            "api",
            "InReview",
            "waiting-merge-approval",
            "title"
        ));
        assert!(!saved.matches(
            QueueView::Kanban,
            "api",
            "InReview",
            "waiting-merge-approval",
            "updated-desc"
        ));
    }

    #[test]
    fn a_stored_view_with_unusable_values_is_repaired_instead_of_sent_to_the_adapter() {
        let repaired = SavedView {
            sort: "sideways".to_string(),
            state: "NotAState".to_string(),
            indicator: "not-real".to_string(),
            view: "grid".to_string(),
            ..Default::default()
        }
        .normalized();
        assert_eq!(repaired.sort, "updated-desc");
        assert!(repaired.state.is_empty());
        assert!(repaired.indicator.is_empty());
        assert_eq!(QueueView::from_str(&repaired.view), QueueView::Table);
    }

    #[test]
    fn the_priority_view_uses_urgency_then_the_declared_priority_then_the_update_time() {
        let ordered = priority_order(vec![
            summary(
                "WORK-quiet-normal",
                "ReadyForImplementation",
                Some("normal"),
                false,
                false,
                Some("2026-11-19T12:00:00Z"),
            ),
            summary(
                "WORK-decision-low",
                "Proposed",
                Some("low"),
                true,
                false,
                Some("2026-11-19T09:00:00Z"),
            ),
            summary(
                "WORK-executing-high",
                "InImplementation",
                Some("high"),
                false,
                false,
                Some("2026-11-19T11:00:00Z"),
            ),
            summary(
                "WORK-stopped-normal",
                "NeedsRework",
                Some("normal"),
                false,
                false,
                Some("2026-11-19T10:00:00Z"),
            ),
            summary(
                "WORK-quiet-urgent",
                "ReadyForImplementation",
                Some("urgent"),
                false,
                false,
                Some("2026-11-19T08:00:00Z"),
            ),
        ]);
        let ids: Vec<&str> = ordered
            .iter()
            .map(|item| item.work_item_id.as_str())
            .collect();
        assert_eq!(
            ids,
            vec![
                "WORK-decision-low",
                "WORK-stopped-normal",
                "WORK-executing-high",
                "WORK-quiet-urgent",
                "WORK-quiet-normal",
            ],
            "urgency first, then declared priority"
        );
    }

    #[test]
    fn the_priority_definition_states_both_inputs_and_the_distinction_from_the_table() {
        let definition = priority_definition();
        assert!(definition.contains("declared planning priority"));
        assert!(definition.contains("authoritative factory flags"));
        assert!(definition.to_ascii_lowercase().contains("table"));
        assert_eq!(declared_priority_label(Some("urgent")), "Urgent");
        assert_eq!(declared_priority_label(Some("HIGH")), "High");
        assert_eq!(declared_priority_label(None), "Not declared");
        assert_eq!(declared_priority_rank(Some("urgent")), 0);
        assert_eq!(declared_priority_rank(Some("low")), 3);
        assert_eq!(declared_priority_rank(None), 4);
    }
}