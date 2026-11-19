//! Tabs with the WAI-ARIA tablist contract and a narrow-screen alternative.
//!
//! Above the breakpoint the tabs are a horizontal tablist with roving tabindex: only the selected
//! tab is in the tab order, Arrow/Home/End move between tabs, and `aria-controls` always resolves
//! to the single rendered panel. Below it the same selection is exposed as a labelled picker, so
//! the last tab is never clipped and a keyboard or screen-reader user is never stranded.

use leptos::prelude::*;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct TabItem {
    pub id: &'static str,
    pub label: &'static str,
}

impl TabItem {
    pub const fn new(id: &'static str, label: &'static str) -> Self {
        Self { id, label }
    }
}

/// The tab a roving-tabindex key press moves to, or `None` when the key is not a tablist key.
/// Wrapping is intentional: Home/End exist, so Arrow from the last tab may wrap to the first.
pub fn roving_target(current: usize, count: usize, key: &str) -> Option<usize> {
    if count == 0 {
        return None;
    }
    match key {
        "ArrowRight" | "ArrowDown" => Some((current + 1) % count),
        "ArrowLeft" | "ArrowUp" => Some((current + count - 1) % count),
        "Home" => Some(0),
        "End" => Some(count - 1),
        _ => None,
    }
}

#[cfg(target_arch = "wasm32")]
fn focus_tab(panel_id: &str, tab_id: &str) {
    use wasm_bindgen::JsCast;
    if let Some(document) = web_sys::window().and_then(|window| window.document()) {
        if let Some(element) = document.get_element_by_id(&format!("{panel_id}-tab-{tab_id}")) {
            if let Ok(element) = element.dyn_into::<web_sys::HtmlElement>() {
                let _ = element.focus();
            }
        }
    }
}

#[component]
pub fn Tabs(
    /// Accessible name of the tablist.
    label: String,
    tabs: Vec<TabItem>,
    selected: RwSignal<&'static str>,
    /// Id of the single panel the tabs control.
    panel_id: &'static str,
) -> impl IntoView {
    let ids_for_keys: Vec<&'static str> = tabs.iter().map(|tab| tab.id).collect();
    let entries = tabs.clone();
    let picker_label = label.clone();

    let on_keydown = move |event: leptos::ev::KeyboardEvent| {
        let current = ids_for_keys
            .iter()
            .position(|id| *id == selected.get_untracked())
            .unwrap_or(0);
        let Some(target) = roving_target(current, ids_for_keys.len(), event.key().as_str()) else {
            return;
        };
        event.prevent_default();
        let id = ids_for_keys[target];
        selected.set(id);
        #[cfg(target_arch = "wasm32")]
        leptos::prelude::set_timeout(
            move || focus_tab(panel_id, id),
            std::time::Duration::from_millis(0),
        );
        #[cfg(not(target_arch = "wasm32"))]
        {
            let _ = (panel_id, id);
        }
    };

    view! {
        <div class="tabs-bar">
            <div class="tabs" role="tablist" aria-label=label.clone() on:keydown=on_keydown>
                {tabs
                    .into_iter()
                    .map(|tab| {
                        let id = tab.id;
                        view! {
                            <button
                                type="button"
                                role="tab"
                                id=format!("{panel_id}-tab-{id}")
                                aria-controls=panel_id
                                aria-selected=move || (selected.get() == id).to_string()
                                tabindex=move || if selected.get() == id { "0" } else { "-1" }
                                class:active=move || selected.get() == id
                                on:click=move |_| selected.set(id)
                            >
                                {tab.label}
                            </button>
                        }
                    })
                    .collect_view()}
            </div>
            <label class="tab-picker">
                <span class="label">{picker_label}</span>
                <select
                    class="select"
                    prop:value=move || selected.get()
                    on:change=move |event| {
                        let value = event_target_value(&event);
                        if let Some(entry) = entries.iter().find(|entry| entry.id == value) {
                            selected.set(entry.id);
                        }
                    }
                >
                    {entries
                        .iter()
                        .map(|entry| {
                            let entry = *entry;
                            view! {
                                <option value=entry.id selected=move || selected.get() == entry.id>
                                    {entry.label}
                                </option>
                            }
                        })
                        .collect_view()}
                </select>
            </label>
        </div>
    }
}

/// The one panel every tab controls. It stays mounted across tab changes, so `aria-controls` is
/// never a broken reference and focus is never lost to a missing element.
#[component]
pub fn TabPanel(
    panel_id: &'static str,
    labelled_by: Signal<String>,
    children: Children,
) -> impl IntoView {
    view! {
        <div
            class="tab-panel"
            role="tabpanel"
            id=panel_id
            tabindex="0"
            aria-labelledby=labelled_by
        >
            {children()}
        </div>
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn roving_tabindex_wraps_and_honours_home_and_end() {
        assert_eq!(roving_target(0, 6, "ArrowRight"), Some(1));
        assert_eq!(roving_target(5, 6, "ArrowRight"), Some(0));
        assert_eq!(roving_target(0, 6, "ArrowLeft"), Some(5));
        assert_eq!(roving_target(3, 6, "Home"), Some(0));
        assert_eq!(roving_target(3, 6, "End"), Some(5));
        assert_eq!(roving_target(3, 6, "Tab"), None);
        assert_eq!(roving_target(3, 6, "Enter"), None);
        assert_eq!(roving_target(0, 0, "ArrowRight"), None);
    }

    #[test]
    fn a_tab_id_is_stable_because_the_panel_links_to_it() {
        let tab = TabItem::new("evidence", "Evidence");
        assert_eq!(tab.id, "evidence");
        assert_eq!(format!("work-tab-panel-tab-{}", tab.id), "work-tab-panel-tab-evidence");
    }
}
