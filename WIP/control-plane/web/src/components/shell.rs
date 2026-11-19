use leptos::prelude::*;
use leptos::task::spawn_local;
use leptos_router::hooks::use_location;

use crate::activity;
use crate::api;
use crate::context::{AppContext, Toast};
use crate::instance_from_path;

#[component]
pub fn AppShell(children: Children) -> impl IntoView {
    let context = AppContext::provide();
    let location = use_location();
    let instance = move || instance_from_path(&location.pathname.get()).unwrap_or_default();
    let path = move || location.pathname.get();

    Effect::new(move |_| {
        let instance = instance();
        if !instance.is_empty() && context.session_loaded.get() {
            activity::connect(context, &instance);
        }
    });

    view! {
        <a class="skip-link" href="#main-content">"Skip to content"</a>
        <div class="app-frame">
            <aside class="sidebar" aria-label="Primary navigation">
                <a class="brand" href="/">
                    <span class="brand-mark">"WF"</span>
                    <span class="brand-text">
                        <strong>"Wip.Factory"</strong>
                        <small>"Control Plane"</small>
                    </span>
                </a>
                <nav class="nav">
                    {move || {
                        let instance = instance();
                        if instance.is_empty() {
                            return view! { <div class="nav-muted">"Select a factory instance"</div> }.into_any();
                        }
                        let base = format!("/i/{instance}");
                        let current = path();
                        let link = move |suffix: &'static str, label: &'static str, icon: &'static str| {
                            let href = format!("{base}{suffix}");
                            let active = current == href || current.starts_with(&(href.clone() + "/"));
                            view! {
                                <a class:active=active class="nav-link" href=href>
                                    <span class="nav-icon" aria-hidden="true">{icon}</span>
                                    <span>{label}</span>
                                </a>
                            }
                        };
                        view! {
                            <div class="nav-section">"Operate"</div>
                            {link("/overview", "Overview", "◉")}
                            {link("/work", "Work queue", "▤")}
                            {link("/pipeline", "Production pipeline", "⇥")}
                            {link("/quality", "Quality & approvals", "✓")}
                            {link("/releases", "Artifacts & releases", "▣")}
                            <div class="nav-section">"Understand"</div>
                            {link("/agents", "Agents", "◈")}
                            {link("/analytics", "Performance", "▥")}
                            {link("/improvement", "Improvement", "↻")}
                            {link("/settings", "Settings & audit", "⚙")}
                        }
                            .into_any()
                    }}
                </nav>
                <div class="sidebar-footer">
                    <ConnectionIndicator context=context/>
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
                    <nav class="crumbs" aria-label="Breadcrumbs">
                        {move || breadcrumbs(&path(), &instance())}
                    </nav>
                    <div class="top-actions">
                        <ThemeToggle/>
                        <span class="status tiny">
                            <span class="dot dot-ok"></span>
                            <span>{move || readable_instance(&instance())}</span>
                        </span>
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
    let error = context.activity.gateway_error;
    view! {
        <div class="conn" title=move || error.get().unwrap_or_default()>
            <span class=move || {
                if connected.get() && !stale.get() {
                    "dot dot-ok"
                } else if stale.get() {
                    "dot dot-danger"
                } else {
                    "dot dot-warn"
                }
            }></span>
            <span>{move || {
                if connected.get() && !stale.get() {
                    "Live activity connected".to_string()
                } else if let Some(error) = error.get() {
                    format!("Activity disconnected: {}", error.chars().take(60).collect::<String>())
                } else {
                    "Activity stream reconnecting…".to_string()
                }
            }}</span>
        </div>
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
    let mut items: Vec<(String, String)> = vec![(base.clone(), readable_instance(instance))];
    let mut accumulated = base;
    for (index, part) in parts.iter().enumerate().skip(2) {
        accumulated.push_str(&format!("/{part}"));
        let label = match *part {
            "overview" => "Overview".to_string(),
            "work" => "Work queue".to_string(),
            "pipeline" => "Pipeline".to_string(),
            "agents" => "Agents".to_string(),
            "quality" => "Quality".to_string(),
            "releases" => "Releases".to_string(),
            "analytics" => "Performance".to_string(),
            "improvement" => "Improvement".to_string(),
            "settings" => "Settings".to_string(),
            other if index == parts.len() - 1 => other.replace('-', " "),
            _ => part.to_string(),
        };
        items.push((accumulated.clone(), label));
    }

    let mut nodes: Vec<AnyView> = Vec::new();
    for (index, (href, label)) in items.into_iter().enumerate() {
        if index > 0 {
            nodes.push(view! { <span class="crumb-sep">"/"</span> }.into_any());
        }
        nodes.push(view! { <a href=href>{label}</a> }.into_any());
    }
    nodes.into_iter().collect_view().into_any()
}

#[component]
fn ThemeToggle() -> impl IntoView {
    #[cfg(target_arch = "wasm32")]
    Effect::new(move |_| {
        if let Some(window) = web_sys::window() {
            if let Some(storage) = window.local_storage().ok().flatten() {
                if let Ok(Some(theme)) = storage.get_item("cp_theme") {
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
            class="btn btn-quiet btn-sm"
            aria-label="Toggle color theme"
            on:click=move |_| {
                #[cfg(target_arch = "wasm32")]
                if let Some(window) = web_sys::window() {
                    if let Some(document) = window.document() {
                        if let Some(root) = document.document_element() {
                            let current = root
                                .get_attribute("data-theme")
                                .unwrap_or_else(|| "dark".to_string());
                            let next = if current == "light" { "dark" } else { "light" };
                            let _ = root.set_attribute("data-theme", next);
                            if let Some(storage) = window.local_storage().ok().flatten() {
                                let _ = storage.set_item("cp_theme", next);
                            }
                        }
                    }
                }
            }
        >
            "Theme"
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
