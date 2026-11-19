//! Downstream SSE connection and connection-health state.
//!
//! The browser consumes only the Rust server's relay. Every factory event still carries the
//! adapter's durable sequence; the UI never derives a transition from a timer or animation.

use leptos::prelude::*;

use control_plane_domain::{EventStreamEnvelope, Heartbeat, StreamResync};

use crate::context::AppContext;

#[cfg(target_arch = "wasm32")]
use std::cell::RefCell;

#[cfg(target_arch = "wasm32")]
use wasm_bindgen::JsCast;
#[cfg(target_arch = "wasm32")]
use wasm_bindgen::closure::Closure;
#[cfg(target_arch = "wasm32")]
use web_sys::{EventSource, MessageEvent};

#[cfg(target_arch = "wasm32")]
thread_local! {
    static CONNECTION: RefCell<Option<Connection>> = const { RefCell::new(None) };
    static CURRENT: RefCell<String> = const { RefCell::new(String::new()) };
    static DEBOUNCE: RefCell<Option<gloo_timers::callback::Timeout>> = const { RefCell::new(None) };
}

#[cfg(target_arch = "wasm32")]
struct Connection {
    source: EventSource,
    listeners: Vec<Closure<dyn FnMut(web_sys::Event)>>,
    open: Closure<dyn FnMut(web_sys::Event)>,
    error: Closure<dyn FnMut(web_sys::Event)>,
}

#[cfg(target_arch = "wasm32")]
impl Connection {
    fn close(self) {
        self.source.set_onopen(None);
        self.source.set_onerror(None);
        self.source.close();
        drop(self.listeners);
        drop(self.open);
        drop(self.error);
    }
}

/// Opens a resumable SSE connection for the selected instance. Calling it again for the same
/// instance is a no-op; calling it for another instance closes the previous one first.
pub fn connect(context: AppContext, instance: &str) {
    if instance.is_empty() {
        return;
    }

    #[cfg(target_arch = "wasm32")]
    {
        CURRENT.with(|current| {
            if current.borrow().as_str() == instance {
                return;
            }
            *current.borrow_mut() = instance.to_string();
        });

        CONNECTION.with(|slot| {
            if let Some(connection) = slot.borrow_mut().take() {
                connection.close();
            }

            let url = format!("/api/instances/{}/events/stream", encode(instance));
            let source = match EventSource::new(&url) {
                Ok(source) => source,
                Err(_) => {
                    CURRENT.with(|current| *current.borrow_mut() = String::new());
                    context.activity.connected.set(false);
                    context.activity.stale.set(true);
                    context.activity.gateway_error.set(Some(
                        "The browser could not open the activity stream.".to_string(),
                    ));
                    return;
                }
            };
            context.activity.connected.set(false);
            context.activity.stale.set(true);

            let factory_context = context;
            let factory = Closure::<dyn FnMut(web_sys::Event)>::wrap(Box::new(
                move |event: web_sys::Event| {
                    let Some(message) = event.dyn_ref::<MessageEvent>() else {
                        return;
                    };
                    let Some(data) = message.data().as_string() else {
                        return;
                    };
                    if let Ok(envelope) = serde_json::from_str::<EventStreamEnvelope>(&data) {
                        factory_context.activity.events.update(|events| {
                            events.push(envelope);
                            if events.len() > 200 {
                                let excess = events.len() - 200;
                                events.drain(0..excess);
                            }
                        });
                        factory_context.activity.connected.set(true);
                        factory_context.activity.stale.set(false);
                        factory_context.activity.gateway_error.set(None);
                        schedule_refresh(factory_context);
                    }
                },
            ));

            let heartbeat_context = context;
            let heartbeat = Closure::<dyn FnMut(web_sys::Event)>::wrap(Box::new(
                move |event: web_sys::Event| {
                    let Some(message) = event.dyn_ref::<MessageEvent>() else {
                        return;
                    };
                    let Some(data) = message.data().as_string() else {
                        return;
                    };
                    if let Ok(beat) = serde_json::from_str::<Heartbeat>(&data) {
                        heartbeat_context.activity.connected.set(beat.healthy);
                        heartbeat_context.activity.stale.set(!beat.healthy);
                        heartbeat_context
                            .activity
                            .last_heartbeat
                            .set(Some(beat.at_utc.to_rfc3339()));
                    }
                },
            ));

            let resync_context = context;
            let resync = Closure::<dyn FnMut(web_sys::Event)>::wrap(Box::new(
                move |event: web_sys::Event| {
                    let Some(message) = event.dyn_ref::<MessageEvent>() else {
                        return;
                    };
                    let Some(data) = message.data().as_string() else {
                        return;
                    };
                    if let Ok(resync) = serde_json::from_str::<StreamResync>(&data) {
                        resync_context.activity.stale.set(true);
                        resync_context
                            .activity
                            .gateway_error
                            .set(Some(format!("Stream resynchronised: {}", resync.reason)));
                        resync_context.touch();
                    }
                },
            ));

            let gateway_context = context;
            let gateway = Closure::<dyn FnMut(web_sys::Event)>::wrap(Box::new(
                move |event: web_sys::Event| {
                    let Some(message) = event.dyn_ref::<MessageEvent>() else {
                        return;
                    };
                    let data = message.data().as_string().unwrap_or_default();
                    let detail = serde_json::from_str::<serde_json::Value>(&data)
                        .ok()
                        .and_then(|value| {
                            value
                                .get("message")
                                .and_then(|value| value.as_str())
                                .map(str::to_string)
                        })
                        .unwrap_or(data);
                    gateway_context.activity.connected.set(false);
                    gateway_context.activity.stale.set(true);
                    gateway_context.activity.gateway_error.set(Some(detail));
                },
            ));

            let open_context = context;
            let open = Closure::<dyn FnMut(web_sys::Event)>::wrap(Box::new(
                move |_event: web_sys::Event| {
                    open_context.activity.connected.set(true);
                    open_context.activity.gateway_error.set(None);
                },
            ));

            let error_context = context;
            let error = Closure::<dyn FnMut(web_sys::Event)>::wrap(Box::new(
                move |_event: web_sys::Event| {
                    error_context.activity.connected.set(false);
                    error_context.activity.stale.set(true);
                    error_context
                        .activity
                        .gateway_error
                        .set(Some("The activity stream is reconnecting.".to_string()));
                },
            ));

            for (name, closure) in [
                ("factory", &factory),
                ("heartbeat", &heartbeat),
                ("connected", &heartbeat),
                ("resync", &resync),
                ("gateway", &gateway),
            ] {
                let _ =
                    source.add_event_listener_with_callback(name, closure.as_ref().unchecked_ref());
            }

            source.set_onopen(Some(open.as_ref().unchecked_ref()));
            source.set_onerror(Some(error.as_ref().unchecked_ref()));

            *slot.borrow_mut() = Some(Connection {
                source,
                listeners: vec![factory, heartbeat, resync, gateway],
                open,
                error,
            });
        });
    }

    #[cfg(not(target_arch = "wasm32"))]
    {
        let _ = (context, instance);
    }
}

#[cfg(target_arch = "wasm32")]
fn schedule_refresh(context: AppContext) {
    DEBOUNCE.with(|slot| {
        if slot.borrow().is_some() {
            return;
        }
        let timeout = gloo_timers::callback::Timeout::new(350, move || {
            context.touch();
            DEBOUNCE.with(|slot| {
                *slot.borrow_mut() = None;
            });
        });
        *slot.borrow_mut() = Some(timeout);
    });
}

fn encode(value: &str) -> String {
    percent_encoding::utf8_percent_encode(value, percent_encoding::NON_ALPHANUMERIC).to_string()
}
