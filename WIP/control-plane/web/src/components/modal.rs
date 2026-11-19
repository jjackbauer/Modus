//! One accessible modal primitive for every governed confirmation.
//!
//! The dialog is rendered only while it is open, so mounting and unmounting are the open and
//! close transitions. On mount the component moves focus inside, marks the application frame
//! inert, and traps Tab/Shift+Tab; on unmount it restores the element that had focus before the
//! dialog opened. Escape closes only when the caller marks the dialog dismissible, which a
//! mutation in flight never does.

use leptos::prelude::*;

/// Stable per-render identifier used for `aria-labelledby`/`aria-describedby`.
fn next_dialog_id() -> u32 {
    use std::sync::atomic::{AtomicU32, Ordering};
    static NEXT: AtomicU32 = AtomicU32::new(0);
    NEXT.fetch_add(1, Ordering::Relaxed)
}

#[cfg(target_arch = "wasm32")]
fn focusable_elements(dialog: &web_sys::Element) -> Vec<web_sys::HtmlElement> {
    use wasm_bindgen::JsCast;
    const SELECTOR: &str = "a[href], button:not([disabled]), input:not([disabled]), \
                            select:not([disabled]), textarea:not([disabled]), \
                            summary, [tabindex]:not([tabindex='-1'])";
    let Ok(list) = dialog.query_selector_all(SELECTOR) else {
        return Vec::new();
    };
    let mut elements = Vec::new();
    for index in 0..list.length() {
        if let Some(node) = list.item(index) {
            if let Ok(element) = node.dyn_into::<web_sys::HtmlElement>() {
                elements.push(element);
            }
        }
    }
    elements
}

#[component]
pub fn Modal(
    title: String,
    #[prop(optional_no_strip)] description: Option<String>,
    on_close: Callback<()>,
    /// True only when closing cannot discard an in-progress governed mutation.
    #[prop(optional)]
    dismissible: bool,
    /// Wider layout for decision dialogs that show a proposal and its evidence.
    #[prop(optional)]
    wide: bool,
    footer: AnyView,
    children: Children,
) -> impl IntoView {
    let dialog_id = format!("cp-dialog-{}", next_dialog_id());
    let title_id = format!("{dialog_id}-title");
    let description_id = description.as_ref().map(|_| format!("{dialog_id}-description"));

    #[cfg(target_arch = "wasm32")]
    {
        let dialog_id = dialog_id.clone();
        Effect::new(move |_| {
            let Some(document) = web_sys::window().and_then(|window| window.document()) else {
                return;
            };
            let previous_focus = document.active_element();

            if let Some(dialog) = document.get_element_by_id(&dialog_id) {
                let focusables = focusable_elements(&dialog);
                match focusables.first() {
                    Some(first) => {
                        let _ = first.focus();
                    }
                    None => {
                        use wasm_bindgen::JsCast;
                        if let Ok(element) = dialog.dyn_into::<web_sys::HtmlElement>() {
                            let _ = element.focus();
                        }
                    }
                }
            }

            let key_dialog_id = dialog_id.clone();
            let handle = window_event_listener(leptos::ev::keydown, move |event: web_sys::KeyboardEvent| {
                if event.key() == "Escape" {
                    if dismissible {
                        event.prevent_default();
                        on_close.run(());
                    }
                    return;
                }
                if event.key() != "Tab" {
                    return;
                }
                let Some(document) = web_sys::window().and_then(|window| window.document()) else {
                    return;
                };
                let Some(dialog) = document.get_element_by_id(&key_dialog_id) else {
                    return;
                };
                let items = focusable_elements(&dialog);
                if items.is_empty() {
                    event.prevent_default();
                    return;
                }
                let active = document.active_element();
                let current = active.and_then(|active| {
                    items.iter().position(|item| {
                        let item: &web_sys::Element = item;
                        js_sys::Object::is(item.as_ref(), active.as_ref())
                    })
                });
                let next = if event.shift_key() {
                    current
                        .map(|index| if index == 0 { items.len() - 1 } else { index - 1 })
                        .unwrap_or(items.len() - 1)
                } else {
                    current.map(|index| (index + 1) % items.len()).unwrap_or(0)
                };
                event.prevent_default();
                let _ = items[next].focus();
            });

            on_cleanup(move || {
                handle.remove();
                use wasm_bindgen::JsCast;
                if let Some(previous) = previous_focus
                    && let Ok(element) = previous.dyn_into::<web_sys::HtmlElement>()
                {
                    let _ = element.focus();
                }
            });
        });
    }

    view! {
        <div class="dialog-backdrop">
            <div
                id=dialog_id
                class=if wide { "dialog dialog-wide" } else { "dialog" }
                role="dialog"
                aria-modal="true"
                aria-labelledby=title_id.clone()
                aria-describedby=description_id.clone()
                tabindex="-1"
            >
                <div class="dialog-head">
                    <div class="dialog-heading">
                        <h2 id=title_id.clone()>{title.clone()}</h2>
                        {description.clone().map(|description| view! {
                            <p id=description_id.clone().unwrap_or_default() class="dialog-description small muted">{description}</p>
                        })}
                    </div>
                    <button
                        class="btn btn-quiet btn-sm"
                        type="button"
                        aria-label="Close dialog"
                        on:click=move |_| on_close.run(())
                    >
                        "Close"
                    </button>
                </div>
                <div class="dialog-body">{children()}</div>
                <div class="dialog-foot">{footer}</div>
            </div>
        </div>
    }
}