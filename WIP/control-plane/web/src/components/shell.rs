use leptos::prelude::*;
use leptos::task::spawn_local;
use leptos_router::hooks::use_location;

use crate::activity;
use crate::api;
use crate::components::ui::time_ago;
use crate::context::{AppContext, Toast};
use crate::instance_from_path;

#[component]
pub fn AppShell(children: Children) -> impl IntoView {
    let context = AppContext::provide();
    let location = use_location();
    let instance = move || instance_from_path(&location.pathname.get()).unwrap_or_default();
    let path = move || location.pathname.get();
    let nav_open = RwSignal::new(false);

    Effect::new(move |_| {
        let instance = instance();
        if !instance.is_empty() && context.session_loaded.get() {
            activity::connect(context, &instance);
        }
    });

    // The browser's own offline/online state is visible to the user immediately, even when the
    // EventSource has not noticed the drop yet. Report the distinct stale/reconnecting states and
    // reopen the stream when the connection returns.
    #[cfg(target_arch = "wasm32")]
    {
        let network_context = context;
        let network_location = location.clone();
        Effect::new(move |_| {
            install_network_listeners(network_context, network_location.clone());
        });
    }

    // A route change always closes the mobile drawer; the new page owns focus.
    Effect::new(move |_| {
        let _ = location.pathname.get();
        nav_open.set(false);
    });

    // A route change moves focus to the new page's h1, so a screen reader starts at the new page
    // instead of remaining on the control that triggered navigation. Suspense-backed routes
    // render the heading after their data arrives, so the helper retries briefly. The heading is
    // made programmatically focusable for this focus only; it does not enter the tab order. A
    // dialog owns focus while it is open, so the effect does nothing in that case.
    Effect::new(move |_| {
        #[cfg(target_arch = "wasm32")]
        {
            if location.pathname.get().is_empty() {
                return;
            }
            focus_main_heading(40);
        }
        #[cfg(not(target_arch = "wasm32"))]
        {
            let _ = location.pathname.get();
        }
    });

    // Keep the document behind the drawer from scrolling while it is open.
    Effect::new(move |_| {
        #[cfg(target_arch = "wasm32")]
        {
            if let Some(body) = web_sys::window()
                .and_then(|window| window.document())
                .and_then(|document| document.body())
            {
                let classes = body.class_list();
                if nav_open.get() {
                    let _ = classes.add_1("nav-open");
                } else {
                    let _ = classes.remove_1("nav-open");
                }
            }
        }
        #[cfg(not(target_arch = "wasm32"))]
        {
            let _ = nav_open.get();
        }
    });

    let close_nav = move || {
        nav_open.set(false);
        focus_element("nav-toggle");
    };

    view! {
        <a class="skip-link" href="#main-content">"Skip to content"</a>
        <div class="app-frame" class:nav-open=move || nav_open.get()>
            <button
                class="nav-backdrop"
                class:open=move || nav_open.get()
                type="button"
                tabindex="-1"
                aria-label="Close navigation"
                on:click=move |_| close_nav()
            ></button>
            <aside
                class="sidebar"
                class:open=move || nav_open.get()
                id="primary-nav"
                aria-label="Primary navigation"
                on:keydown=move |event| {
                    if !nav_open.get() {
                        return;
                    }
                    if event.key() == "Escape" {
                        event.prevent_default();
                        close_nav();
                        return;
                    }
                    if event.key() == "Tab" {
                        trap_tab(&event, "primary-nav");
                    }
                }
            >
                <a class="brand" href="/" aria-label="Wip.Factory Control Plane home">
                    <span class="brand-mark" aria-hidden="true">"WF"</span>
                    <span class="brand-text">
                        <strong>"Wip.Factory"</strong>
                        <small>"Control Plane"</small>
                    </span>
                </a>
                <button
                    class="rail-close"
                    id="rail-close"
                    type="button"
                    aria-label="Close navigation"
                    on:click=move |_| close_nav()
                >"Close"</button>
                <nav class="nav" aria-label="Sections">
                    {move || {
                        let instance = instance();
                        if instance.is_empty() {
                            return view! { <div class="nav-muted">"Select a factory instance"</div> }.into_any();
                        }
                        let base = format!("/i/{instance}");
                        let current = path();
                        let link = move |suffix: &'static str, label: &'static str| {
                            let href = format!("{base}{suffix}");
                            let active = current == href || current.starts_with(&(href.clone() + "/"));
                            let current_attr = if active { "page" } else { "false" };
                            view! {
                                <a
                                    class:active=active
                                    class="nav-link"
                                    href=href
                                    aria-current=current_attr
                                    on:click=move |_| nav_open.set(false)
                                >
                                    <span>{label}</span>
                                </a>
                            }
                        };
                        view! {
                            <div class="nav-group">
                                <div class="nav-group-label" id="nav-operations">"Operations"</div>
                                <div class="nav-group-links" role="group" aria-labelledby="nav-operations">
                                    {link("/overview", "Overview")}
                                    {link("/work", "Work queue")}
                                    {link("/quality", "Quality & approvals")}
                                    {link("/agents", "Agents")}
                                    {link("/releases", "Artifacts & releases")}
                                </div>
                            </div>
                            <div class="nav-group">
                                <div class="nav-group-label" id="nav-analysis">"Analysis"</div>
                                <div class="nav-group-links" role="group" aria-labelledby="nav-analysis">
                                    {link("/pipeline", "Production pipeline")}
                                    {link("/analytics", "Performance")}
                                    {link("/improvement", "Improvement")}
                                </div>
                            </div>
                            <div class="nav-group">
                                <div class="nav-group-label" id="nav-administration">"Administration"</div>
                                <div class="nav-group-links" role="group" aria-labelledby="nav-administration">
                                    {link("/settings", "Settings & audit")}
                                    <a class="nav-link" href="/docs" on:click=move |_| nav_open.set(false)>"Documentation"</a>
                                </div>
                            </div>
                        }
                            .into_any()
                    }}
                </nav>
                <div class="sidebar-footer">
                    <ConnectionIndicator context=context/>
                    <InstanceIdentity context=context instance=Signal::derive(instance)/>
                    <div class="account">
                        <span>{move || context.username()}</span>
                        <span class="tiny faint">{move || context.role()}</span>
                    </div>
                    <button class="btn btn-sm btn-quiet" on:click=move |_| {
                        let csrf = context.csrf.get();
                        spawn_local(async move {
                            let _ = api::logout(&csrf).await;
                            #[cfg(target_arch = "wasm32")]
                            if let Some(window) = web_sys::window() {
                                let _ = window.location().set_href("/login");
                            }
                        });
                    }>"Sign out"</button>
                </div>
            </aside>
            <div class="content-frame">
                <header class="topbar">
                    <button
                        class="nav-toggle"
                        id="nav-toggle"
                        type="button"
                        aria-label="Open navigation"
                        aria-expanded=move || nav_open.get().to_string()
                        aria-controls="primary-nav"
                        on:click=move |_| {
                            if nav_open.get() {
                                close_nav();
                            } else {
                                nav_open.set(true);
                                focus_element_after_frame("rail-close");
                            }
                        }
                    >
                        <span aria-hidden="true">"☰"</span>
                        <span>"Menu"</span>
                    </button>
                    <nav class="crumbs" aria-label="Breadcrumbs">
                        {move || breadcrumbs(&path(), &instance())}
                    </nav>
                    <div class="top-actions">
                        <TopInstance context=context instance=Signal::derive(instance)/>
                        <ThemeToggle/>
                    </div>
                </header>
                <main class="content" id="main-content">
                    {children()}
                </main>
            </div>
        </div>
        <ToastHost toast=context.toast/>
    }
}

#[component]
fn ConnectionIndicator(context: AppContext) -> impl IntoView {
    let connected = context.activity.connected;
    let stale = context.activity.stale;
    let offline = context.activity.offline;
    let error = context.activity.gateway_error;
    let heartbeat = context.activity.last_heartbeat;
    let label = move || {
        if offline.get() {
            return "Offline · showing the last durable facts".to_string();
        }
        if connected.get() && !stale.get() {
            match heartbeat.get() {
                Some(value) => format!("Live activity · last event {}", time_ago(Some(value.as_str()))),
                None => "Live activity · waiting for the first event".to_string(),
            }
        } else if stale.get() {
            "Activity stale · last durable facts shown".to_string()
        } else {
            "Activity reconnecting…".to_string()
        }
    };
    let tone = move || {
        if offline.get() {
            "dot dot-danger"
        } else if connected.get() && !stale.get() {
            "dot dot-ok"
        } else if stale.get() {
            "dot dot-danger"
        } else {
            "dot dot-warn"
        }
    };
    view! {
        <div class="conn" role="status" title=move || error.get().unwrap_or_default()>
            <span class=tone></span>
            <span>{label}</span>
        </div>
    }
}

#[component]
fn InstanceIdentity(context: AppContext, instance: Signal<String>) -> impl IntoView {
    let identity = move || {
        let id = instance.get();
        context
            .session
            .get()
            .and_then(|session| session.instances.into_iter().find(|item| item.id == id))
    };
    let label = move || {
        identity()
            .map(|item| item.label)
            .filter(|label| !label.is_empty())
            .unwrap_or_else(|| readable_instance(&instance.get()))
    };
    let detail = move || {
        identity()
            .map(|item| {
                if item.healthy {
                    "Adapter healthy".to_string()
                } else {
                    item.detail.unwrap_or_else(|| "Adapter needs attention".to_string())
                }
            })
            .unwrap_or_else(|| "Instance health unavailable".to_string())
    };
    view! {
        <div class="sidebar-instance">
            <span class="sidebar-instance-label">{label}</span>
            <span class="tiny faint" title=move || detail()>{move || detail()}</span>
        </div>
    }
}

#[component]
fn TopInstance(context: AppContext, instance: Signal<String>) -> impl IntoView {
    let info = move || {
        let id = instance.get();
        context
            .session
            .get()
            .and_then(|session| session.instances.into_iter().find(|item| item.id == id))
    };
    view! {
        <span class="top-instance" title=move || info().and_then(|item| item.detail).unwrap_or_default()>
            <span class=move || {
                if info().map(|item| item.healthy).unwrap_or(false) { "dot dot-ok" } else { "dot dot-warn" }
            }></span>
            <span>{move || info().map(|item| item.label).filter(|label| !label.is_empty()).unwrap_or_else(|| readable_instance(&instance.get()))}</span>
        </span>
    }
}

#[component]
fn ToastHost(toast: RwSignal<Option<Toast>>) -> impl IntoView {
    view! {
        {move || toast.get().map(|toast_value| {
            let class = format!("toast toast-{}", toast_value.kind);
            view! {
                <div class=class role="status">
                    <div>
                        <strong>{toast_value.message}</strong>
                        {toast_value.detail.map(|detail| view! { <div class="tiny mono">{detail}</div> })}
                    </div>
                    <button class="btn btn-sm btn-quiet" on:click=move |_| toast.set(None)>"Dismiss"</button>
                </div>
            }
        })}
    }
}

fn breadcrumbs(path: &str, instance: &str) -> AnyView {
    if instance.is_empty() {
        return view! { <span class="faint">"No factory instance"</span> }.into_any();
    }

    let parts: Vec<&str> = path.split('/').filter(|part| !part.is_empty()).collect();
    let base = format!("/i/{instance}");
    let mut items: Vec<(String, String)> = vec![(format!("{base}/overview"), readable_instance(instance))];
    let mut accumulated = base;
    for (index, part) in parts.iter().enumerate().skip(2) {
        accumulated.push_str(&format!("/{part}"));
        let label = match *part {
            "overview" => "Overview".to_string(),
            "work" => "Work queue".to_string(),
            "pipeline" => "Production pipeline".to_string(),
            "agents" => "Agents".to_string(),
            "quality" => "Quality & approvals".to_string(),
            "releases" => "Artifacts & releases".to_string(),
            "analytics" => "Performance".to_string(),
            "improvement" => "Improvement".to_string(),
            "settings" => "Settings & audit".to_string(),
            other if index == parts.len() - 1 => other.to_string(),
            _ => part.to_string(),
        };
        items.push((accumulated.clone(), label));
    }

    let last = items.len().saturating_sub(1);
    let mut nodes: Vec<AnyView> = Vec::new();
    for (index, (href, label)) in items.into_iter().enumerate() {
        if index > 0 {
            nodes.push(view! { <span class="crumb-sep" aria-hidden="true">"/"</span> }.into_any());
        }
        if index == last {
            nodes.push(view! { <span class="crumb-current" aria-current="page">{label}</span> }.into_any());
        } else {
            nodes.push(view! { <a href=href>{label}</a> }.into_any());
        }
    }
    nodes.into_iter().collect_view().into_any()
}

#[component]
fn ThemeToggle() -> impl IntoView {
    let light = RwSignal::new(false);

    #[cfg(target_arch = "wasm32")]
    Effect::new(move |_| {
        if let Some(window) = web_sys::window() {
            if let Some(storage) = window.local_storage().ok().flatten() {
                if let Ok(Some(theme)) = storage.get_item("cp_theme") {
                    let is_light = theme == "light";
                    light.set(is_light);
                    if let Some(document) = window.document() {
                        if let Some(root) = document.document_element() {
                            let _ = root.set_attribute("data-theme", &theme);
                        }
                    }
                }
            }
        }
    });

    view! {
        <button
            class="btn btn-quiet btn-sm theme-toggle"
            type="button"
            aria-label=move || if light.get() { "Switch to dark theme" } else { "Switch to light theme" }
            on:click=move |_| {
                #[cfg(target_arch = "wasm32")]
                if let Some(window) = web_sys::window() {
                    if let Some(document) = window.document() {
                        if let Some(root) = document.document_element() {
                            let next = if light.get() { "dark" } else { "light" };
                            light.set(!light.get());
                            let _ = root.set_attribute("data-theme", next);
                            if let Some(storage) = window.local_storage().ok().flatten() {
                                let _ = storage.set_item("cp_theme", next);
                            }
                        }
                    }
                }
            }
        >
            {move || if light.get() { "Dark theme" } else { "Light theme" }}
        </button>
    }
}

fn readable_instance(instance: &str) -> String {
    if instance.is_empty() {
        "No instance".to_string()
    } else {
        instance.to_string()
    }
}

fn focus_element(id: &str) {
    #[cfg(target_arch = "wasm32")]
    {
        use wasm_bindgen::JsCast;
        if let Some(document) = web_sys::window().and_then(|window| window.document()) {
            if let Some(element) = document.get_element_by_id(id) {
                if let Ok(element) = element.dyn_into::<web_sys::HtmlElement>() {
                    let _ = element.focus();
                }
            }
        }
    }
    #[cfg(not(target_arch = "wasm32"))]
    {
        let _ = id;
    }
}

/// The drawer's visibility flips in the same tick as the signal; wait one frame so the
/// element can actually receive focus.
fn focus_element_after_frame(id: &'static str) {
    #[cfg(target_arch = "wasm32")]
    leptos::prelude::set_timeout(
        move || focus_element(id),
        std::time::Duration::from_millis(60),
    );
    #[cfg(not(target_arch = "wasm32"))]
    {
        let _ = id;
    }
}

#[cfg(target_arch = "wasm32")]
fn install_network_listeners(context: AppContext, location: leptos_router::location::Location) {
    use wasm_bindgen::closure::Closure;
    use wasm_bindgen::JsCast;

    let Some(window) = web_sys::window() else {
        return;
    };

    let offline_context = context;
    let offline = Closure::<dyn FnMut(web_sys::Event)>::wrap(Box::new(move |_| {
        offline_context.activity.offline.set(true);
        offline_context.activity.connected.set(false);
        offline_context.activity.stale.set(true);
        offline_context.activity.gateway_error.set(Some(
            "The browser is offline. Showing the last durable facts; the activity stream will resync when the connection returns."
                .to_string(),
        ));
        activity::suspend();
    }));

    let online_context = context;
    let online = Closure::<dyn FnMut(web_sys::Event)>::wrap(Box::new(move |_| {
        online_context.activity.offline.set(false);
        online_context.activity.connected.set(false);
        online_context.activity.stale.set(false);
        online_context.activity.gateway_error.set(Some(
            "The browser is back online. Reconnecting the activity stream.".to_string(),
        ));
        let pathname = location.pathname.get_untracked();
        let instance = crate::instance_from_path(&pathname).unwrap_or_default();
        if !instance.is_empty() {
            activity::reconnect(online_context, &instance);
        }
    }));

    let _ = window.add_event_listener_with_callback("offline", offline.as_ref().unchecked_ref());
    let _ = window.add_event_listener_with_callback("online", online.as_ref().unchecked_ref());
    // The shell is mounted once for the lifetime of the document; the listeners must outlive this
    // function. A document reload drops them with the document.
    offline.forget();
    online.forget();
}

/// Focuses the new route's `h1`. A Suspense-backed route renders its heading once the data
/// resolves, and a reactive refresh can replace that heading node, so this keeps checking for a
/// short bounded window. It only moves focus while the active element is the document body or the
/// heading itself; the moment the operator focuses a control it stops.
fn focus_main_heading(remaining: u32) {
    #[cfg(target_arch = "wasm32")]
    {
        use wasm_bindgen::JsCast;
        let Some(document) = web_sys::window().and_then(|window| window.document()) else {
            return;
        };
        if document
            .query_selector(".dialog")
            .ok()
            .flatten()
            .is_some()
        {
            return;
        }
        let active = document.active_element();
        let active_tag = active
            .as_ref()
            .map(|element| element.tag_name())
            .unwrap_or_default();
        if active_tag != "BODY" && active_tag != "H1" {
            return;
        }
        if let Some(heading) = document.query_selector("main h1").ok().flatten() {
            let already_focused = active
                .as_ref()
                .map(|element| element.is_same_node(Some(&heading)))
                .unwrap_or(false);
            if !already_focused {
                if let Some(element) = heading.dyn_ref::<web_sys::HtmlElement>() {
                    let _ = element.set_tab_index(-1);
                    let _ = element.focus();
                }
            }
        }
        if remaining > 0 {
            leptos::prelude::set_timeout(
                move || focus_main_heading(remaining - 1),
                std::time::Duration::from_millis(80),
            );
        }
    }
    #[cfg(not(target_arch = "wasm32"))]
    let _ = remaining;
}

fn trap_tab(event: &leptos::ev::KeyboardEvent, container_id: &str) {
    #[cfg(target_arch = "wasm32")]
    {
        use wasm_bindgen::JsCast;
        let Some(document) = web_sys::window().and_then(|window| window.document()) else {
            return;
        };
        let Some(container) = document.get_element_by_id(container_id) else {
            return;
        };
        let Ok(list) = container.query_selector_all(
            "a[href], button:not([disabled]), [tabindex]:not([tabindex='-1'])",
        ) else {
            return;
        };
        let mut items: Vec<web_sys::HtmlElement> = Vec::new();
        for index in 0..list.length() {
            if let Some(node) = list.item(index) {
                if let Ok(element) = node.dyn_into::<web_sys::HtmlElement>() {
                    items.push(element);
                }
            }
        }
        if items.is_empty() {
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
    }
    #[cfg(not(target_arch = "wasm32"))]
    {
        let _ = (event, container_id);
    }
}