mod api;
mod app;
mod audit;
mod auth;
mod config;
mod instances;
mod overview;
mod state;

use std::net::SocketAddr;
use std::sync::Arc;
use std::time::Instant;

use anyhow::Context;
use leptos::prelude::*;
use leptos_axum::generate_route_list;
use tracing_subscriber::EnvFilter;

use crate::app::build;
use crate::audit::AuditLog;
use crate::auth::AuthService;
use crate::config::ServerConfig;
use crate::instances::InstanceRegistry;
use crate::state::AppState;

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(
            EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| EnvFilter::new("info,control_plane_server=debug")),
        )
        .with_target(false)
        .compact()
        .init();

    let config = Arc::new(ServerConfig::load()?);
    let (auth, bootstrap_path) = AuthService::load(&config)?;
    if let Some(path) = bootstrap_path {
        tracing::warn!(
            "bootstrap administrator created. The generated password is in {}. Sign in and rotate it.",
            path.display()
        );
    }
    if auth.user_count() == 0 {
        anyhow::bail!(
            "the operator user store is empty; refusing to start without an accountable identity"
        );
    }

    let audit = AuditLog::open(&config.data_dir)?;
    let instances = InstanceRegistry::new(config.clone());

    let leptos_options = LeptosOptions::builder()
        .output_name("control_plane_web")
        .site_root(config.static_dir.to_string_lossy().to_string())
        .site_pkg_dir("pkg")
        .site_addr(
            config
                .bind
                .parse::<std::net::SocketAddr>()
                .unwrap_or_else(|_| "127.0.0.1:3000".parse().unwrap()),
        )
        .build();

    let state = AppState {
        config: config.clone(),
        auth: Arc::new(auth),
        instances: Arc::new(instances),
        audit: Arc::new(audit),
        started: Instant::now(),
        leptos_options,
    };

    let routes = generate_route_list(control_plane_web::App);
    let app = build(state, routes);

    let listener = tokio::net::TcpListener::bind(&config.bind)
        .await
        .with_context(|| format!("binding control plane to {}", config.bind))?;
    let local = listener.local_addr()?;
    tracing::info!(
        "Wip.Factory control plane listening on http://{local} (instances={}, data={})",
        config.instances.len(),
        config.data_dir.display()
    );

    axum::serve(
        listener,
        app.into_make_service_with_connect_info::<SocketAddr>(),
    )
    .with_graceful_shutdown(shutdown_signal())
    .await
    .context("serving control plane")?;

    Ok(())
}

async fn shutdown_signal() {
    let ctrl_c = async {
        let _ = tokio::signal::ctrl_c().await;
    };

    #[cfg(unix)]
    let terminate = async {
        use tokio::signal::unix::{SignalKind, signal};
        if let Ok(mut stream) = signal(SignalKind::terminate()) {
            stream.recv().await;
        }
    };

    #[cfg(not(unix))]
    let terminate = std::future::pending::<()>();

    tokio::select! {
        _ = ctrl_c => {},
        _ = terminate => {},
    }

    tracing::info!("control plane shutting down");
}
