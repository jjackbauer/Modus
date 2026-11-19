//! Layout primitives: a titled section with an accessible heading, an action row, and the one
//! filter bar every queue-like surface uses.

use leptos::prelude::*;

fn next_region_id() -> u32 {
    use std::sync::atomic::{AtomicU32, Ordering};
    static NEXT: AtomicU32 = AtomicU32::new(0);
    NEXT.fetch_add(1, Ordering::Relaxed)
}

/// The top of a route: what this page is, what it is for, and the actions that belong to it.
/// The title is the `h1`; a page never repeats its own name in a second heading.
#[component]
pub fn PageHeader(
    #[prop(into)] title: String,
    #[prop(into)] subtitle: String,
    /// Extra facts that belong with the title, e.g. instance identity or freshness.
    #[prop(optional_no_strip)] meta: Option<AnyView>,
    children: Children,
) -> impl IntoView {
    view! {
        <header class="page-header">
            <div class="page-heading">
                <h1 class="page-title">{title}</h1>
                <p class="page-subtitle">{subtitle}</p>
                {meta}
            </div>
            <div class="page-actions">{children()}</div>
        </header>
    }
}

/// A titled region. The heading is the accessible name of the section, so a screen-reader user can
/// jump between the regions of a long page instead of reading it linearly.
#[component]
pub fn Section(
    /// Stable id base for the heading. Must be unique on the page.
    id: &'static str,
    #[prop(into)] title: String,
    /// A short fact beside the title, e.g. "2 open decisions".
    #[prop(optional_no_strip)] count: Option<String>,
    /// One sentence that explains how to read the section. Not decoration: it states definitions.
    #[prop(optional_no_strip)] note: Option<String>,
    #[prop(optional_no_strip)] actions: Option<AnyView>,
    children: Children,
) -> impl IntoView {
    let title_id = format!("{id}-title");
    view! {
        <section class="section" aria-labelledby=title_id.clone()>
            <div class="section-head">
                <h2 class="section-title" id=title_id.clone()>{title}</h2>
                <div class="section-head-side">
                    {count.map(|count| view! { <span class="section-count">{count}</span> })}
                    {actions}
                </div>
            </div>
            {note.map(|note| view! { <p class="section-note">{note}</p> })}
            {children()}
        </section>
    }
}

/// A grouped row of controls with an accessible name. Grouping matters when several actions look
/// alike: the name says what the group acts on.
#[component]
pub fn ActionToolbar(#[prop(into)] label: String, children: Children) -> impl IntoView {
    view! {
        <div class="action-toolbar" role="group" aria-label=label>
            {children()}
        </div>
    }
}

/// Search, labelled filters, saved views and an always-available clear. On narrow screens the
/// filter fields collapse behind a named button instead of being hidden with no way back.
#[component]
pub fn FilterBar(
    #[prop(into)] label: String,
    /// The search control, always visible.
    leading: AnyView,
    /// The labelled filter fields. These collapse behind the named Filters button on narrow screens.
    fields: AnyView,
    /// A second row: view switching, saved views and any other control that describes the whole set.
    #[prop(optional_no_strip)] secondary: Option<AnyView>,
    /// View switching, saved views and any other trailing control.
    #[prop(optional_no_strip)] trailing: Option<AnyView>,
    /// True when any filter or search term is narrowing the result set.
    active: Signal<bool>,
    on_clear: Callback<()>,
) -> impl IntoView {
    let fields_id = format!("filter-fields-{}", next_region_id());
    let open = RwSignal::new(false);
    view! {
        <div class="queue-toolbar filter-bar" role="group" aria-label=label>
            <div class="toolbar-row">
                {leading}
                <button
                    class="btn btn-sm queue-filter-toggle"
                    type="button"
                    aria-expanded=move || open.get().to_string()
                    aria-controls=fields_id.clone()
                    on:click=move |_| open.update(|value| *value = !*value)
                >
                    {move || if open.get() { "Hide filters" } else { "Filters" }}
                </button>
                {trailing.map(|trailing| view! { <div class="toolbar-actions">{trailing}</div> })}
            </div>
            <div
                class="toolbar-row toolbar-fields"
                id=fields_id
                class:open=move || open.get()
            >
                {fields}
                <button
                    class="btn btn-sm"
                    type="button"
                    disabled=move || !active.get()
                    title=move || if active.get() { "Clear search, state and attention filters" } else { "No filters are applied" }
                    on:click=move |_| on_clear.run(())
                >
                    "Clear filters"
                </button>
            </div>
            {secondary.map(|secondary| view! { <div class="toolbar-secondary">{secondary}</div> })}
        </div>
    }
}
