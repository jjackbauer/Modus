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
use crate::components::Confirmation;
use crate::components::feedback::{EmptyState, FieldError, InlineValidation};
use crate::components::layout::FilterBar;
use crate::components::status::{EntityIdentity, StatusIndicator};
use crate::components::table::{Column, DataTable, Sort, SortDirection, TableState};
use crate::components::ui::{DigestCopy, EmptyBlock, LoadingBlock, Notice, error_view, time_ago};
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
            <Transition fallback=move || view! { <LoadingBlock label="Loading durable work items…"/> }>
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
            </Transition>
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
        "ReadyForDelivery" => "Run dispatch to merge",
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

/// The local checks with the field each one belongs to. The factory parser is still the
/// authority; this only tells the operator where to fix what.
fn intake_field_errors(draft: &RequirementDraft) -> Vec<FieldError> {
    let mut errors = Vec::new();
    if draft.title.trim().is_empty() {
        errors.push(FieldError::new("intake-title", "Title", "A title is required."));
    }
    if draft.description.trim().is_empty() {
        errors.push(FieldError::new(
            "intake-description",
            "Description",
            "A description is required.",
        ));
    }
    if draft
        .acceptance_expectations
        .iter()
        .all(|line| line.trim().is_empty())
    {
        errors.push(FieldError::new(
            "intake-expectations",
            "Observable acceptance expectations",
            "Add at least one observable acceptance expectation. The factory refuses a requirement without one.",
        ));
    }
    if draft.supported_class.trim() != control_plane_domain::SUPPORTED_CLASS {
        errors.push(FieldError::new(
            "intake-document",
            "Generated document",
            "The generated document names a supported class the factory alpha does not accept.",
        ));
    }
    if let Some(json) = draft.acceptance_contract_json.as_deref() {
        if !json.trim().is_empty() {
            if let Err(message) = control_plane_domain::draft::validate_acceptance_contract(json) {
                errors.push(FieldError::new(
                    "intake-contract",
                    "Acceptance contract JSON",
                    message,
                ));
            }
        }
    }
    errors
}

/// Maps the factory parser's stable codes to a field and a sentence an operator can act on. The
/// raw code is not repeated here; it stays in the technical details rendered beside the verdict.
fn map_parser_error(raw: &str) -> FieldError {
    let code = raw.trim();
    match code {
        "requirement-missing-title" => FieldError::new(
            "intake-title",
            "Title",
            "The factory parser found no title. Add one in the Title field.",
        ),
        "requirement-without-acceptance-expectations" => FieldError::new(
            "intake-expectations",
            "Observable acceptance expectations",
            "The factory refuses a requirement without an observable expectation. Add at least one line that states an HTTP outcome, such as 'GET /health returns 200'.",
        ),
        "requirement-unterminated-front-matter" => FieldError::new(
            "intake-document",
            "Generated document",
            "The generated document's metadata block is not closed. A field probably contains a line of three dashes; simplify the title or description.",
        ),
        _ if code.starts_with("requirement-unknown-metadata:") => FieldError::new(
            "intake-document",
            "Generated document",
            "The generated document contains a metadata key the factory parser does not know. Remove it from the field that produced it.",
        ),
        _ if code.starts_with("unsupported-class") => FieldError::new(
            "intake-document",
            "Generated document",
            "The factory alpha accepts only its supported application class. This build should never produce another value; report the generated document.",
        ),
        _ if code.starts_with("contract-invalid:json") => FieldError::new(
            "intake-contract",
            "Acceptance contract JSON",
            "The acceptance contract is not valid JSON. Check the brackets, quotes and commas.",
        ),
        "contract-invalid:root-must-be-an-object" => FieldError::new(
            "intake-contract",
            "Acceptance contract JSON",
            "The acceptance contract must be a JSON object, not an array or a scalar.",
        ),
        "contract-invalid:schemaVersion" => FieldError::new(
            "intake-contract",
            "Acceptance contract JSON",
            "The contract needs a numeric schemaVersion (use 1).",
        ),
        "contract-invalid:supportedClass" => FieldError::new(
            "intake-contract",
            "Acceptance contract JSON",
            "The contract supportedClass must name the factory's supported application class.",
        ),
        "contract-invalid:scenarios" => FieldError::new(
            "intake-contract",
            "Acceptance contract JSON",
            "The contract needs a non-empty scenarios array.",
        ),
        "contract-invalid:duplicate-scenario-id" => FieldError::new(
            "intake-contract",
            "Acceptance contract JSON",
            "Two scenarios share the same id. Give every scenario a unique id.",
        ),
        "contract-invalid:scenario-missing-id-method-or-path" => FieldError::new(
            "intake-contract",
            "Acceptance contract JSON",
            "Every scenario needs a non-empty id, method and path.",
        ),
        "contract-invalid:scenario-not-an-object" => FieldError::new(
            "intake-contract",
            "Acceptance contract JSON",
            "Every entry in the scenarios array must be a JSON object.",
        ),
        _ if code.starts_with("contract-invalid:scenario-expect-status") => FieldError::new(
            "intake-contract",
            "Acceptance contract JSON",
            "Every scenario needs expect.status with an HTTP status code.",
        ),
        _ if code.starts_with("contract-unsupported-form:path-must-be-relative") => FieldError::new(
            "intake-contract",
            "Acceptance contract JSON",
            "A scenario path must start with '/'. Absolute URLs are not accepted.",
        ),
        _ if code.starts_with("contract-unsupported-form:absolute-url-not-allowed") => FieldError::new(
            "intake-contract",
            "Acceptance contract JSON",
            "A scenario path must be relative to the served application; absolute URLs are not accepted.",
        ),
        _ if code
            .starts_with("contract-unsupported-form:capture-must-be-a-string-expression") =>
        {
            FieldError::new(
                "intake-contract",
                "Acceptance contract JSON",
                "A capture value must be a string expression.",
            )
        }
        _ if code.starts_with("contract-") => FieldError::new(
            "intake-contract",
            "Acceptance contract JSON",
            "The factory rejected the acceptance contract. Check its schema, supported class, scenarios and expected statuses.",
        ),
        _ => FieldError::new(
            "intake-document",
            "Generated document",
            "The factory parser rejected the document. The raw code is preserved in Technical details.",
        ),
    }
}

fn map_parser_errors(errors: &[String]) -> Vec<FieldError> {
    errors.iter().map(|raw| map_parser_error(raw)).collect()
}

/// The message for one control, derived from the shared error list so the summary and the field
/// cannot disagree about which problem belongs where.
fn field_error(combined: Signal<Vec<FieldError>>, field_id: &'static str) -> Signal<Option<String>> {
    Signal::derive(move || {
        combined
            .get()
            .into_iter()
            .find(|error| error.field_id == field_id)
            .map(|error| error.message)
    })
}

/// A labelled control with a persistent label, a required marker and a field-associated error.
/// The error paragraph is `ValidationMessage`, so its id matches the summary's anchor.
#[component]
fn IntakeField(
    id: &'static str,
    #[prop(into)] label: String,
    #[prop(optional)] span2: bool,
    #[prop(optional)] required: bool,
    #[prop(optional_no_strip)] hint: Option<String>,
    error: Signal<Option<String>>,
    children: Children,
) -> impl IntoView {
    let class = if span2 { "field span-2" } else { "field" };
    view! {
        <div class=class>
            <label class="field-label" for=id>
                {label}
                {required.then(|| view! { <span class="field-required">" (required)"</span> })}
            </label>
            {hint.map(|hint| view! { <p class="form-hint">{hint}</p> })}
            {children()}
            {move || error.get().map(|message| view! {
                <crate::components::feedback::ValidationMessage field_id=id.to_string() message=message/>
            })}
        </div>
    }
}

fn focus_by_id(id: &str) {
    #[cfg(target_arch = "wasm32")]
    {
        use wasm_bindgen::JsCast;
        if let Some(element) = web_sys::window()
            .and_then(|window| window.document())
            .and_then(|document| document.get_element_by_id(id))
        {
            if let Ok(element) = element.dyn_into::<web_sys::HtmlElement>() {
                let _ = element.focus();
            }
        }
    }
    #[cfg(not(target_arch = "wasm32"))]
    let _ = id;
}

/// The fields of a requirement draft are stored while the operator is still writing. The stored
/// form is the same typed draft the submit handler builds, so it cannot drift from what is sent.
#[allow(dead_code)]
const INTAKE_DRAFT_KEY: &str = "cp_intake_draft";

#[allow(dead_code)]
fn intake_draft_is_empty(draft: &RequirementDraft) -> bool {
    draft.title.trim().is_empty()
        && draft.description.trim().is_empty()
        && draft.functional_requirements.is_empty()
        && draft.acceptance_expectations.is_empty()
        && draft.constraints.is_empty()
        && draft.non_goals.is_empty()
        && draft.template_reference.is_none()
        && draft.template_parameters_json.is_none()
        && draft.acceptance_contract_json.is_none()
        && draft.deadline_utc.is_none()
        && draft.tags.is_empty()
        && draft.links.is_empty()
        && draft.notes.is_none()
}
#[component]
fn WorkIntakeForm(instance: String, on_created: Callback<String>) -> impl IntoView {
    let context = AppContext::provide();
    let preview_instance = instance.clone();
    let submit_instance = instance.clone();
    let template_instance = instance.clone();

    let title = RwSignal::new(String::new());
    let description = RwSignal::new(String::new());
    let functional = RwSignal::new(String::new());
    let expectations = RwSignal::new(String::new());
    let constraints = RwSignal::new(String::new());
    let non_goals = RwSignal::new(String::new());
    let contract_json = RwSignal::new(String::new());
    let template_reference = RwSignal::new(String::new());
    let template_parameters = RwSignal::new(String::new());
    let priority = RwSignal::new("normal".to_string());
    let deadline = RwSignal::new(String::new());
    let tags = RwSignal::new(String::new());
    let links = RwSignal::new(String::new());
    let notes = RwSignal::new(String::new());

    let confirmed = RwSignal::new(false);
    let attempted = RwSignal::new(false);
    let preview = RwSignal::new(None::<Preview>);
    let preview_document = RwSignal::new(None::<String>);
    let parser_issues = RwSignal::new(Vec::<FieldError>::new());
    let parser_raw = RwSignal::new(Vec::<String>::new());
    let error = RwSignal::new(None::<api::UiError>);
    let busy = RwSignal::new(false);
    let idempotency_key = RwSignal::new(api::new_idempotency_key());
    #[cfg(target_arch = "wasm32")]
    let draft_loaded = RwSignal::new(false);

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

    let document = Signal::derive(move || build_draft().to_markdown());
    let local_errors = Signal::derive(move || intake_field_errors(&build_draft()));
    let preview_current = Signal::derive(move || match preview_document.get() {
        Some(saved) => saved == document.get(),
        None => false,
    });
    let combined_errors = Signal::derive(move || {
        let mut errors = local_errors.get();
        if preview_current.get() {
            // The parser repeats what the local checks already say about title, description and
            // expectations. Keep the local wording and add only the parser's new facts, so the
            // summary counts problems rather than repetitions.
            for issue in parser_issues.get() {
                if !errors
                    .iter()
                    .any(|existing| existing.field_id == issue.field_id)
                {
                    errors.push(issue);
                }
            }
        }
        errors
    });
    // Errors are not announced before the operator has asked the parser or the submit path to
    // check the form; a fresh form must not open with three red messages.
    let visible_errors = Signal::derive(move || {
        if attempted.get() {
            combined_errors.get()
        } else {
            Vec::new()
        }
    });
    let title_error = field_error(visible_errors, "intake-title");
    let description_error = field_error(visible_errors, "intake-description");
    let expectations_error = field_error(visible_errors, "intake-expectations");
    let contract_error = field_error(visible_errors, "intake-contract");
    let document_error = field_error(visible_errors, "intake-document");
    let no_error = Signal::derive(|| None::<String>);
    let stale_preview = Signal::derive(move || preview.get().is_some() && !preview_current.get());
    let can_submit = Signal::derive(move || {
        !busy.get()
            && confirmed.get()
            && preview_current.get()
            && preview.get().map(|value| value.valid).unwrap_or(false)
            && local_errors.get().is_empty()
    });

    // Load once. The save effect below waits for `draft_loaded`, so an empty form can never
    // overwrite the stored draft before it has been read.
    #[cfg(target_arch = "wasm32")]
    {
        Effect::new(move |_| {
            if draft_loaded.get() {
                return;
            }
            if let Some(storage) =
                web_sys::window().and_then(|window| window.local_storage().ok().flatten())
            {
                if let Ok(Some(raw)) = storage.get_item(INTAKE_DRAFT_KEY) {
                    if let Ok(draft) = serde_json::from_str::<RequirementDraft>(&raw) {
                        title.set(draft.title);
                        description.set(draft.description);
                        functional.set(draft.functional_requirements.join("\n"));
                        expectations.set(draft.acceptance_expectations.join("\n"));
                        constraints.set(draft.constraints.join("\n"));
                        non_goals.set(draft.non_goals.join("\n"));
                        contract_json.set(draft.acceptance_contract_json.unwrap_or_default());
                        template_reference.set(draft.template_reference.unwrap_or_default());
                        template_parameters.set(draft.template_parameters_json.unwrap_or_default());
                        priority.set(
                            draft
                                .priority
                                .filter(|value| !value.trim().is_empty())
                                .unwrap_or_else(|| "normal".to_string()),
                        );
                        deadline.set(draft.deadline_utc.unwrap_or_default());
                        tags.set(draft.tags.join(", "));
                        links.set(draft.links.join("\n"));
                        notes.set(draft.notes.unwrap_or_default());
                    }
                }
            }
            draft_loaded.set(true);
        });

        Effect::new(move |_| {
            if !draft_loaded.get() {
                return;
            }
            let draft = build_draft();
            if let Some(storage) =
                web_sys::window().and_then(|window| window.local_storage().ok().flatten())
            {
                if intake_draft_is_empty(&draft) {
                    let _ = storage.remove_item(INTAKE_DRAFT_KEY);
                } else if let Ok(raw) = serde_json::to_string(&draft) {
                    let _ = storage.set_item(INTAKE_DRAFT_KEY, &raw);
                }
            }
        });
    }
    let on_preview = move |_| {
        attempted.set(true);
        let draft = build_draft();
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
            match api::preview(&instance, &csrf, &request).await {
                Ok(value) => {
                    let raw = value.errors.clone();
                    let rejected = !raw.is_empty();
                    parser_issues.set(map_parser_errors(&raw));
                    parser_raw.set(raw);
                    preview_document.set(Some(request.requirement.clone()));
                    confirmed.set(false);
                    preview.set(Some(value));
                    if rejected {
                        focus_by_id("intake-validation-summary");
                    }
                }
                Err(issue) => {
                    preview.set(None);
                    preview_document.set(None);
                    parser_issues.set(Vec::new());
                    parser_raw.set(Vec::new());
                    error.set(Some(issue));
                }
            }
            busy.set(false);
        });
    };

    let on_submit = move |_| {
        attempted.set(true);
        let draft = build_draft();
        if !intake_field_errors(&draft).is_empty() {
            focus_by_id("intake-validation-summary");
            return;
        }
        if !preview_current.get() {
            error.set(Some(api::UiError::new(
                0,
                "preview-current-required",
                if preview.get().is_some() {
                    "The generated document changed after the last parser check. Validate again before submitting."
                } else {
                    "Validate the requirement with the factory parser before submitting."
                },
            )));
            focus_by_id("intake-validate");
            return;
        }
        let Some(preview_value) = preview.get() else {
            error.set(Some(api::UiError::new(
                0,
                "preview-required",
                "Validate the requirement with the factory parser before submitting.",
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
                "Confirm that the generated requirement shown is the exact document you intend to submit.",
            )));
            return;
        }

        let request = SubmitRequest {
            requirement: draft.to_markdown(),
            title: None,
            acceptance_contract_json: draft.acceptance_contract_json.clone(),
            planning: Some(draft.planning_input()),
            explicitly_confirmed: Some(confirmed.get()),
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
                    #[cfg(target_arch = "wasm32")]
                    if let Some(storage) =
                        web_sys::window().and_then(|window| window.local_storage().ok().flatten())
                    {
                        let _ = storage.remove_item(INTAKE_DRAFT_KEY);
                    }
                    on_created.run(response.work_item_id);
                }
                Err(issue) => {
                    error.set(Some(issue));
                    busy.set(false);
                }
            }
        });
    };

    let clear_draft = move || {
        title.set(String::new());
        description.set(String::new());
        functional.set(String::new());
        expectations.set(String::new());
        constraints.set(String::new());
        non_goals.set(String::new());
        contract_json.set(String::new());
        template_reference.set(String::new());
        template_parameters.set(String::new());
        priority.set("normal".to_string());
        deadline.set(String::new());
        tags.set(String::new());
        links.set(String::new());
        notes.set(String::new());
        preview.set(None);
        preview_document.set(None);
        parser_issues.set(Vec::new());
        parser_raw.set(Vec::new());
        confirmed.set(false);
        attempted.set(false);
        error.set(None);
        #[cfg(target_arch = "wasm32")]
        if let Some(storage) =
            web_sys::window().and_then(|window| window.local_storage().ok().flatten())
        {
            let _ = storage.remove_item(INTAKE_DRAFT_KEY);
        }
        context.notify_success("Draft cleared from this browser.");
    };
    view! {
        <section class="panel intake-panel">
            <header class="panel-header">
                <h2 class="panel-title">"New work item"</h2>
                <span class="tiny faint">"The factory parser is authoritative; planning metadata is control-plane only."</span>
            </header>
            <div class="panel-body">
                <div id="intake-validation-summary" class="intake-validation" tabindex="-1">
                    {move || attempted.get().then(|| view! {
                        <InlineValidation errors=visible_errors.get()/>
                    })}
                </div>

                <div class="form-grid">
                    <IntakeField id="intake-title" label="Title" span2=true required=true error=title_error>
                        <input
                            id="intake-title"
                            class="input"
                            autocomplete="off"
                            aria-required="true"
                            aria-invalid=move || title_error.get().is_some().to_string()
                            aria-describedby=move || if title_error.get().is_some() { "intake-title-error" } else { "" }
                            prop:value=move || title.get()
                            on:input=move |event| title.set(event_target_value(&event))
                        />
                    </IntakeField>
                    <IntakeField id="intake-description" label="Description" span2=true required=true error=description_error>
                        <textarea
                            id="intake-description"
                            class="textarea"
                            aria-required="true"
                            aria-invalid=move || description_error.get().is_some().to_string()
                            aria-describedby=move || if description_error.get().is_some() { "intake-description-error" } else { "" }
                            placeholder="What should exist, for whom, and why?"
                            prop:value=move || description.get()
                            on:input=move |event| description.set(event_target_value(&event))
                        ></textarea>
                    </IntakeField>
                    <IntakeField
                        id="intake-functional"
                        label="Functional requirements"
                        error=no_error
                        hint=Some("One per line. These are not acceptance bullets.".to_string())
                    >
                        <textarea
                            id="intake-functional"
                            class="textarea small-area"
                            prop:value=move || functional.get()
                            on:input=move |event| functional.set(event_target_value(&event))
                        ></textarea>
                    </IntakeField>
                    <IntakeField
                        id="intake-expectations"
                        label="Observable acceptance expectations"
                        required=true
                        error=expectations_error
                        hint=Some("One observable HTTP outcome per line, such as GET /health returns 200. The factory treats every line as an expectation.".to_string())
                    >
                        <textarea
                            id="intake-expectations"
                            class="textarea small-area"
                            aria-required="true"
                            aria-invalid=move || expectations_error.get().is_some().to_string()
                            aria-describedby=move || if expectations_error.get().is_some() { "intake-expectations-error" } else { "" }
                            placeholder="GET /health returns HTTP 200 with status ok"
                            prop:value=move || expectations.get()
                            on:input=move |event| expectations.set(event_target_value(&event))
                        ></textarea>
                    </IntakeField>
                    <IntakeField id="intake-constraints" label="Constraints" error=no_error hint=Some("One per line.".to_string())>
                        <textarea
                            id="intake-constraints"
                            class="textarea small-area"
                            prop:value=move || constraints.get()
                            on:input=move |event| constraints.set(event_target_value(&event))
                        ></textarea>
                    </IntakeField>
                    <IntakeField id="intake-non-goals" label="Non-goals" error=no_error hint=Some("One per line.".to_string())>
                        <textarea
                            id="intake-non-goals"
                            class="textarea small-area"
                            prop:value=move || non_goals.get()
                            on:input=move |event| non_goals.set(event_target_value(&event))
                        ></textarea>
                    </IntakeField>
                </div>

                <details class="intake-advanced">
                    <summary>"Acceptance contract, template and planning metadata"</summary>
                    <div class="form-grid" style="margin-top:12px">
                        <IntakeField
                            id="intake-contract"
                            label="Acceptance contract JSON"
                            span2=true
                            error=contract_error
                            hint=Some("Optional. Without an executable contract the factory can record a proposal but cannot approve or verify it.".to_string())
                        >
                            <textarea
                                id="intake-contract"
                                class="textarea code-area"
                                aria-invalid=move || contract_error.get().is_some().to_string()
                                aria-describedby=move || if contract_error.get().is_some() { "intake-contract-error" } else { "" }
                                placeholder=r#"{"schemaVersion":1,"supportedClass":"aspnetcore-http-net10","scenarios":[...]}"#
                                prop:value=move || contract_json.get()
                                on:input=move |event| contract_json.set(event_target_value(&event))
                            ></textarea>
                        </IntakeField>
                        <div class="form-grid span-2">
                            <IntakeField id="intake-template" label="Approved template" error=no_error>
                                <select
                                    id="intake-template"
                                    class="select"
                                    prop:value=move || template_reference.get()
                                    on:change=move |event| template_reference.set(event_target_value(&event))
                                >
                                    <option value="">"Custom requirement (no template)"</option>
                                    {move || match templates.get().flatten() {
                                        Some(Ok(list)) => list.into_iter().filter(|template| template.approved && !template.revoked).map(|template| {
                                            let value = format!("{}@{}", template.template_id, template.template_version);
                                            view! { <option value=value.clone()>{template.template_id}" v"{template.template_version}</option> }
                                        }).collect_view().into_any(),
                                        _ => view! { <option value="" disabled>"Loading approved templates…"</option> }.into_any(),
                                    }}
                                </select>
                            </IntakeField>
                            <IntakeField id="intake-template-reference" label="Template reference" error=no_error hint=Some("template-id@version".to_string())>
                                <input
                                    id="intake-template-reference"
                                    class="input"
                                    placeholder="template-id@version"
                                    prop:value=move || template_reference.get()
                                    on:input=move |event| template_reference.set(event_target_value(&event))
                                />
                            </IntakeField>
                        </div>
                        <IntakeField
                            id="intake-template-parameters"
                            label="Template parameters JSON"
                            error=no_error
                            hint=Some(r#"Optional, for example {"path":"/health"}"#.to_string())
                        >
                            <input
                                id="intake-template-parameters"
                                class="input"
                                prop:value=move || template_parameters.get()
                                on:input=move |event| template_parameters.set(event_target_value(&event))
                            />
                        </IntakeField>
                        <IntakeField id="intake-priority" label="Priority (planning only)" error=no_error>
                            <select
                                id="intake-priority"
                                class="select"
                                prop:value=move || priority.get()
                                on:change=move |event| priority.set(event_target_value(&event))
                            >
                                <option value="normal">"Normal"</option>
                                <option value="high">"High"</option>
                                <option value="urgent">"Urgent"</option>
                                <option value="low">"Low"</option>
                            </select>
                        </IntakeField>
                        <IntakeField id="intake-deadline" label="Deadline (planning only)" error=no_error>
                            <input
                                id="intake-deadline"
                                class="input"
                                type="date"
                                prop:value=move || deadline.get()
                                on:input=move |event| deadline.set(event_target_value(&event))
                            />
                        </IntakeField>
                        <IntakeField id="intake-tags" label="Tags (comma separated, planning only)" error=no_error>
                            <input
                                id="intake-tags"
                                class="input"
                                prop:value=move || tags.get()
                                on:input=move |event| tags.set(event_target_value(&event))
                            />
                        </IntakeField>
                        <IntakeField id="intake-links" label="Related links (one per line, planning only)" error=no_error>
                            <input
                                id="intake-links"
                                class="input"
                                prop:value=move || links.get()
                                on:input=move |event| links.set(event_target_value(&event))
                            />
                        </IntakeField>
                        <IntakeField id="intake-notes" label="Operator notes (planning only)" span2=true error=no_error>
                            <textarea
                                id="intake-notes"
                                class="textarea small-area"
                                prop:value=move || notes.get()
                                on:input=move |event| notes.set(event_target_value(&event))
                            ></textarea>
                        </IntakeField>
                    </div>
                    <p class="form-hint">
                        "Priority, deadline, tags, links and notes are stored by the control plane and are never converted into factory scheduling or execution authority."
                    </p>
                </details>

                <div class="intake-draft-row">
                    <button class="btn btn-quiet btn-sm" type="button" on:click=move |_| clear_draft()>
                        "Clear draft"
                    </button>
                    <p class="tiny faint">
                        "Draft fields are stored in this browser (localStorage) so a refresh or a trip to another page does not lose them. Nothing is sent to the factory until you validate or submit. Clear draft removes the stored copy."
                    </p>
                </div>

                {move || stale_preview.get().then(|| view! {
                    <Notice kind="warn">
                        <strong>"The generated document changed after the last parser check. "</strong>
                        <span>"Validate again to bind the parser result and the confirmation to the new bytes."</span>
                    </Notice>
                })}

                {move || error.get().map(|issue| view! {
                    <Notice kind="danger">
                        <strong>"The request did not complete. "</strong>
                        <span>{issue.message.clone()}</span>
                        <span class="tiny mono">"Code: "{issue.code.clone()}</span>
                    </Notice>
                })}

                {move || preview.get().map(|value| {
                    let current = preview_current.get();
                    view! {
                        <div class="preview-result">
                            <div class="row-between">
                                <strong>
                                    {if !current {
                                        "Parser result for an earlier version of the document"
                                    } else if value.valid {
                                        "Factory parser accepted this exact document"
                                    } else {
                                        "Factory parser rejected this exact document"
                                    }}
                                </strong>
                                <span class="tiny mono">{short_digest(value.requirement_digest.as_deref())}</span>
                            </div>
                            {(!current).then(|| view! {
                                <p class="small form-error">"This verdict does not apply to the generated document below."</p>
                            })}
                            {(current && !value.errors.is_empty()).then(|| view! {
                                <ul class="form-errors">
                                    {map_parser_errors(&value.errors).into_iter().map(|issue| view! {
                                        <li>
                                            <a href=format!("#{}", issue.field_id)>{issue.label}</a>
                                            {format!(": {}", issue.message)}
                                        </li>
                                    }).collect_view()}
                                </ul>
                            })}
                            {(current && !value.contract_scenarios.is_empty()).then(|| view! {
                                <div class="tiny faint">
                                    "Contract scenarios: "
                                    {value.contract_scenarios.iter().map(|scenario| scenario.id.clone()).collect::<Vec<_>>().join(", ")}
                                </div>
                            })}
                        </div>
                    }
                })}

                {move || {
                    let raw = parser_raw.get();
                    if raw.is_empty() || !preview_current.get() {
                        return None;
                    }
                    Some(view! {
                        <details class="technical-details">
                            <summary>"Technical details (raw parser codes)"</summary>
                            <ul>
                                {raw.into_iter().map(|code| view! { <li><code class="mono">{code}</code></li> }).collect_view()}
                            </ul>
                        </details>
                    })
                }}

                <div class="intake-document" id="intake-document" tabindex="-1" aria-label="Exact generated requirement">
                    <div class="label">"Generated requirement (the exact bytes sent after confirmation)"</div>
                    <pre class="requirement-draft">{move || document.get()}</pre>
                    {move || preview_current.get().then(|| preview.get()).flatten().map(|value| view! {
                        <DigestCopy
                            label="Factory digest of the exact document being submitted".to_string()
                            value=value.requirement_digest.clone()
                        />
                    })}
                    {move || document_error.get().map(|message| view! {
                        <p class="field-error" role="alert">{message}</p>
                    })}
                    <details class="md-render">
                        <summary>"Rendered preview"</summary>
                        <div class="md panel" inner_html=move || render_markdown(&document.get())></div>
                    </details>
                </div>

                <div class="intake-confirmation">
                    <Confirmation
                        assertion="I have read the exact generated requirement shown above and confirm it is the document to submit to the factory.".to_string()
                        checked=confirmed
                    />
                </div>

                <div class="intake-actions">
                    <button
                        class="btn"
                        id="intake-validate"
                        type="button"
                        disabled=move || busy.get()
                        on:click=on_preview
                    >
                        {move || if busy.get() { "Validating…" } else { "Validate with factory parser" }}
                    </button>
                    <span class="spacer"></span>
                    <button
                        class="btn btn-primary"
                        type="button"
                        disabled=move || !can_submit.get()
                        on:click=on_submit
                    >
                        {move || if busy.get() { "Submitting…" } else { "Submit to factory" }}
                    </button>
                </div>
                <p class="tiny faint intake-submit-hint">
                    {move || {
                        let parsed = preview.get().map(|value| value.valid).unwrap_or(false);
                        if preview_current.get() && parsed {
                            if confirmed.get() {
                                "Ready to submit the exact document shown."
                            } else {
                                "Confirm the exact generated requirement to enable submission."
                            }
                        } else if preview.get().is_some() {
                            "The parser verdict does not apply to the current document. Validate again."
                        } else {
                            "Validate the generated document with the factory parser to enable submission."
                        }
                    }}
                </p>
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
        assert_eq!(
            next_step_label(&item),
            "Run dispatch to merge",
            "a recorded authorization waits for the dispatch cycle, not another authorization"
        );
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

    fn supported_draft() -> RequirementDraft {
        RequirementDraft {
            supported_class: control_plane_domain::SUPPORTED_CLASS.to_string(),
            ..Default::default()
        }
    }

    #[test]
    fn every_local_validation_message_points_at_the_field_that_needs_fixing() {
        let errors = intake_field_errors(&supported_draft());
        let ids: Vec<&str> = errors.iter().map(|error| error.field_id.as_str()).collect();
        assert_eq!(
            ids,
            vec!["intake-title", "intake-description", "intake-expectations"]
        );

        let mut filled = supported_draft();
        filled.title = "Health".to_string();
        filled.description = "Add a health endpoint.".to_string();
        filled.acceptance_expectations = vec!["GET /health returns 200".to_string()];
        assert!(intake_field_errors(&filled).is_empty());

        filled.acceptance_contract_json = Some("{not json}".to_string());
        let errors = intake_field_errors(&filled);
        assert_eq!(errors.len(), 1);
        assert_eq!(errors[0].field_id, "intake-contract");
    }

    #[test]
    fn parser_codes_become_human_messages_and_keep_the_field_context() {
        let title = map_parser_error("requirement-missing-title");
        assert_eq!(title.field_id, "intake-title");
        assert!(title.message.to_lowercase().contains("title"));
        assert!(
            !title.message.contains("requirement-missing-title"),
            "the raw code belongs in technical details, not the summary"
        );

        let expectations = map_parser_error("requirement-without-acceptance-expectations");
        assert_eq!(expectations.field_id, "intake-expectations");

        let status = map_parser_error("contract-invalid:scenario-expect-status:AC-1");
        assert_eq!(status.field_id, "intake-contract");
        assert!(status.message.to_lowercase().contains("status"));

        let absolute = map_parser_error("contract-unsupported-form:absolute-url-not-allowed:AC-2");
        assert_eq!(absolute.field_id, "intake-contract");
        assert!(absolute.message.contains("relative"));

        let unknown = map_parser_error("some-new-code");
        assert_eq!(unknown.field_id, "intake-document");
        assert!(!unknown.message.contains("some-new-code"));
    }

    #[test]
    fn the_stored_draft_is_the_same_typed_draft_that_would_be_submitted() {
        let mut draft = supported_draft();
        draft.title = "Health".to_string();
        draft.description = "Add a health endpoint.".to_string();
        draft.functional_requirements = vec!["Expose readiness".to_string()];
        draft.acceptance_expectations = vec!["GET /health returns 200".to_string()];
        draft.tags = vec!["ops".to_string()];
        draft.priority = Some("high".to_string());

        let raw = serde_json::to_string(&draft).expect("draft serialises");
        let restored: RequirementDraft = serde_json::from_str(&raw).expect("draft restores");
        assert_eq!(restored, draft);
        assert!(!intake_draft_is_empty(&restored));
        assert!(intake_draft_is_empty(&RequirementDraft::default()));
    }
}