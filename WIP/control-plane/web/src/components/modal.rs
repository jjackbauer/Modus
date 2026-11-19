//! One accessible modal primitive for every governed confirmation.
//!
//! The dialog is rendered only while it is open, so mounting and unmounting are the open and
//! close transitions. On mount it moves itself to a document-level portal (a sibling of the
//! application frame, never a descendant), marks the application frame `inert`, stops the
//! document behind it from scrolling and moves focus inside; Tab and Shift+Tab stay inside while
//! it is open. Escape closes only when the caller marks the dialog dismissible, which a mutation
//! in flight never does. On close the frame becomes interactive again and the element that had
//! focus before the dialog opened receives it back.

use leptos::prelude::*;

/// The application frame that must not be reachable while a dialog is open. The dialog is moved
/// out of it, because an `inert` ancestor would also make the dialog itself inert.
const APP_FRAME_SELECTOR: &str = ".app-frame";

/// Stable per-render identifier used for `aria-labelledby`/`aria-describedby`.
fn next_dialog_id() -> u32 {
    use std::sync::atomic::{AtomicU32, Ordering};
    static NEXT: AtomicU32 = AtomicU32::new(0);
    NEXT.fetch_add(1, Ordering::Relaxed)
}

#[cfg(target_arch = "wasm32")]
const FOCUSABLE_SELECTOR: &str = "a[href], button:not([disabled]), input:not([disabled]), \
                                  select:not([disabled]), textarea:not([disabled]), \
                                  summary, [tabindex]:not([tabindex='-1'])";

#[cfg(target_arch = "wasm32")]
fn focusable_elements(scope: &web_sys::Element) -> Vec<web_sys::HtmlElement> {
    use wasm_bindgen::JsCast;
    let Ok(list) = scope.query_selector_all(FOCUSABLE_SELECTOR) else {
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

/// Applies or lifts the background lock. The dialog subtree was moved to the document body, so
/// `inert` on the frame cannot reach it. The open-dialog count keeps two overlapping transitions
/// from lifting the lock while a dialog is still on screen.
fn set_background_locked(locked: bool) {
    #[cfg(target_arch = "wasm32")]
    {
        let Some(document) = web_sys::window().and_then(|window| window.document()) else {
            return;
        };
        if let Some(frame) = document
            .query_selector(APP_FRAME_SELECTOR)
            .ok()
            .flatten()
        {
            if locked {
                let _ = frame.set_attribute("inert", "");
            } else {
                let _ = frame.remove_attribute("inert");
            }
        }
        if let Some(body) = document.body() {
            let classes = body.class_list();
            if locked {
                let _ = classes.add_1("dialog-open");
            } else {
                let _ = classes.remove_1("dialog-open");
            }
        }
    }
    #[cfg(not(target_arch = "wasm32"))]
    {
        let _ = locked;
    }
}

/// Registers one open dialog and returns the new count.
fn register_open_dialog(delta: i32) -> i32 {
    use std::sync::atomic::{AtomicI32, Ordering};
    static OPEN_DIALOGS: AtomicI32 = AtomicI32::new(0);
    (OPEN_DIALOGS.fetch_add(delta, Ordering::SeqCst) + delta).max(0)
}

#[cfg(target_arch = "wasm32")]
fn focus_inside(dialog_id: &str) -> bool {
    use wasm_bindgen::JsCast;
    let Some(document) = web_sys::window().and_then(|window| window.document()) else {
        return false;
    };
    let Some(dialog) = document.get_element_by_id(dialog_id) else {
        return false;
    };
    if let Some(first) = focusable_elements(&dialog).first() {
        return first.focus().is_ok();
    }
    match dialog.dyn_into::<web_sys::HtmlElement>() {
        Ok(element) => element.focus().is_ok(),
        Err(_) => false,
    }
}

/// Focus the dialog as soon as its portal has mounted it. The portal attaches in an effect that
/// runs after this component's effect, so the first attempts can legitimately find no element.
fn focus_when_mounted(dialog_id: String, remaining: u32) {
    #[cfg(target_arch = "wasm32")]
    {
        if focus_inside(&dialog_id) || remaining == 0 {
            return;
        }
        let next = dialog_id.clone();
        leptos::prelude::set_timeout(
            move || focus_when_mounted(next, remaining - 1),
            std::time::Duration::from_millis(16),
        );
    }
    #[cfg(not(target_arch = "wasm32"))]
    {
        let _ = (dialog_id, remaining);
    }
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
    let backdrop = NodeRef::<leptos::html::Div>::new();

    #[cfg(not(target_arch = "wasm32"))]
    let _ = dismissible;

    #[cfg(target_arch = "wasm32")]
    {
        let mounted_dialog_id = dialog_id.clone();
        Effect::new(move |_| {
            let Some(document) = web_sys::window().and_then(|window| window.document()) else {
                return;
            };
            let previous_focus = document.active_element();

            // Portal first: if this failed, `inert` would strand the dialog. The dialog backdrop is
            // a direct child of <body>, so the application frame can be inert without it.
            if let Some(element) = backdrop.get() {
                if let Some(body) = document.body() {
                    let _ = body.append_child(element.as_ref());
                }
                let element = element.clone();
                on_cleanup(move || {
                    let _ = element.remove();
                });
            }

            let open_count = register_open_dialog(1);
            set_background_locked(open_count > 0);
            focus_when_mounted(mounted_dialog_id.clone(), 24);

            let key_dialog_id = mounted_dialog_id.clone();
            let handle =
                window_event_listener(leptos::ev::keydown, move |event: web_sys::KeyboardEvent| {
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
                    let Some(document) = web_sys::window().and_then(|window| window.document())
                    else {
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
                // Lift the lock before restoring focus: the element that opened the dialog is
                // inside the frame and cannot take focus while the frame is inert.
                let open_count = register_open_dialog(-1);
                set_background_locked(open_count > 0);
                use wasm_bindgen::JsCast;
                if let Some(previous) = previous_focus
                    && let Ok(element) = previous.dyn_into::<web_sys::HtmlElement>()
                    && element.is_connected()
                {
                    let _ = element.focus();
                }
            });
        });
    }

    view! {
        <div class="dialog-backdrop" node_ref=backdrop>
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
