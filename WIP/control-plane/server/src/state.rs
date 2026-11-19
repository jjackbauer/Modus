use std::sync::Arc;
use std::time::Instant;

use chrono::{DateTime, Utc};

use control_plane_factory_client::{AdapterRole, FactoryClient, FactoryClientConfig};

use crate::audit::AuditLog;
use crate::auth::{AuthService, AuthUser, Role};
use crate::config::ServerConfig;
use crate::instances::InstanceRegistry;

#[derive(Clone, axum::extract::FromRef)]
pub struct AppState {
    pub config: Arc<ServerConfig>,
    pub auth: Arc<AuthService>,
    pub instances: Arc<InstanceRegistry>,
    pub audit: Arc<AuditLog>,
    pub started: Instant,
    pub leptos_options: leptos::prelude::LeptosOptions,
}

impl AppState {
    pub async fn client(
        &self,
        instance: &str,
        user: &AuthUser,
        correlation_id: Option<String>,
    ) -> Result<FactoryClient, crate::api::ApiFailure> {
        let role = match user.role {
            Role::Viewer => AdapterRole::Viewer,
            Role::Operator => AdapterRole::Operator,
            Role::Admin => AdapterRole::Admin,
        };
        self.instances
            .client(instance, role, &user.username, correlation_id)
            .await
    }
}

pub struct RequestContext {
    pub correlation_id: String,
    pub remote: String,
    pub instance: String,
    pub idempotency_key: Option<String>,
}

impl RequestContext {
    pub fn from_request(
        headers: &http::HeaderMap,
        instance: &str,
        remote: Option<std::net::SocketAddr>,
    ) -> Self {
        let correlation_id = headers
            .get("x-request-id")
            .and_then(|value| value.to_str().ok())
            .filter(|value| !value.is_empty() && value.len() <= 80)
            .map(str::to_string)
            .unwrap_or_else(|| uuid::Uuid::new_v4().to_string());
        let idempotency_key = headers
            .get("idempotency-key")
            .and_then(|value| value.to_str().ok())
            .filter(|value| !value.is_empty())
            .map(str::to_string);
        Self {
            correlation_id,
            remote: remote
                .map(|address| address.ip().to_string())
                .unwrap_or_else(|| "local".to_string()),
            instance: instance.to_string(),
            idempotency_key,
        }
    }
}

pub fn now_rfc3339() -> String {
    Utc::now().to_rfc3339()
}

pub fn parse_time(value: &str) -> Option<DateTime<Utc>> {
    DateTime::parse_from_rfc3339(value)
        .ok()
        .map(|value| value.with_timezone(&Utc))
}
