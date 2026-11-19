use leptos::prelude::*;
use leptos::task::spawn_local;

use control_plane_domain::{EventStreamEnvelope, SessionInfo};

use crate::api;
use crate::api::is_client;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Toast {
    pub kind: String,
    pub message: String,
    pub detail: Option<String>,
}

#[derive(Clone, Copy)]
pub struct ActivityState {
    pub events: RwSignal<Vec<EventStreamEnvelope>>,
    pub connected: RwSignal<bool>,
    pub stale: RwSignal<bool>,
    pub last_heartbeat: RwSignal<Option<String>>,
    pub gateway_error: RwSignal<Option<String>>,
}

impl ActivityState {
    pub fn new() -> Self {
        Self {
            events: RwSignal::new(Vec::new()),
            connected: RwSignal::new(false),
            stale: RwSignal::new(true),
            last_heartbeat: RwSignal::new(None),
            gateway_error: RwSignal::new(None),
        }
    }
}

#[derive(Clone, Copy)]
pub struct AppContext {
    pub session: RwSignal<Option<SessionInfo>>,
    pub session_loaded: RwSignal<bool>,
    pub csrf: RwSignal<String>,
    pub refresh: RwSignal<u64>,
    pub activity: ActivityState,
    pub toast: RwSignal<Option<Toast>>,
}

impl AppContext {
    pub fn new() -> Self {
        Self {
            session: RwSignal::new(None),
            session_loaded: RwSignal::new(false),
            csrf: RwSignal::new(String::new()),
            refresh: RwSignal::new(0),
            activity: ActivityState::new(),
            toast: RwSignal::new(None),
        }
    }

    pub fn provide() -> Self {
        if let Some(existing) = use_context::<AppContext>() {
            return existing;
        }
        let context = Self::new();
        provide_context(context);
        context
    }

    pub fn role(&self) -> String {
        self.session
            .get()
            .map(|session| session.role)
            .unwrap_or_else(|| "viewer".to_string())
    }

    pub fn username(&self) -> String {
        self.session
            .get()
            .map(|session| session.display_name)
            .unwrap_or_else(|| "Account".to_string())
    }

    pub fn can_write(&self) -> bool {
        matches!(self.role().as_str(), "operator" | "admin")
    }

    pub fn is_admin(&self) -> bool {
        self.role() == "admin"
    }

    pub fn touch(&self) {
        self.refresh.update(|value| *value += 1);
    }

    pub fn notify_error(&self, error: api::UiError) {
        if error.is_csrf() && is_client() {
            self.session.set(None);
            self.session_loaded.set(false);
        }
        self.toast.set(Some(Toast {
            kind: "danger".to_string(),
            message: error.message,
            detail: Some(error.code),
        }));
    }

    pub fn notify_success(&self, message: impl Into<String>) {
        self.toast.set(Some(Toast {
            kind: "success".to_string(),
            message: message.into(),
            detail: None,
        }));
    }

    pub fn dismiss_toast(&self) {
        self.toast.set(None);
    }
}

pub fn install_session_loader(context: AppContext) {
    Effect::new(move |_| {
        if !is_client() {
            return;
        }

        spawn_local(async move {
            match api::session().await {
                Ok(session) => {
                    context.csrf.set(session.csrf_token.clone());
                    context.session.set(Some(session));
                    context.session_loaded.set(true);
                }
                Err(error) => {
                    if error.status == 401 {
                        #[cfg(target_arch = "wasm32")]
                        if let Some(window) = web_sys::window() {
                            let _ = window.location().set_href("/login");
                        }
                    } else {
                        context.notify_error(error);
                    }
                }
            }
        });
    });
}
