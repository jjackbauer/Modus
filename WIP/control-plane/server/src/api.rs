use std::convert::Infallible;
use std::sync::Arc;

use axum::extract::{Path, Query, State};
use axum::http::{HeaderMap, StatusCode};
use axum::response::sse::{Event, KeepAlive, Sse};
use axum::response::{IntoResponse, Response};
use axum::routing::{delete, get, patch, post};
use axum::{Extension, Json, Router};
use chrono::Utc;
use futures_util::StreamExt;
use serde::Deserialize;
use serde_json::json;

use control_plane_domain::{
    AuditEntry, ChangePasswordRequest, ContractApproveRequest, ContractRejectRequest,
    CreateUserRequest, DispatchRequest, MergeApprovalRequest, Page, PlanningUpdateRequest,
    PreviewRequest, ProfileDraftRequest, RegisterInstanceRequest, ResetPasswordRequest,
    ServeRequest, SubmitRequest, UpdateUserRequest,
};
use control_plane_factory_client::{ClientError, FactoryClient};

use crate::audit::NewAuditEntry;
use crate::auth::{AuthUser, Role};
use crate::config::InstanceConfig;
use crate::instances::PublicInstance;
use crate::overview;
use crate::state::{AppState, RequestContext, now_rfc3339};

#[derive(Debug)]
pub struct ApiFailure {
    pub status: u16,
    pub code: String,
    pub message: String,
    pub category: String,
    pub retryable: bool,
}

impl ApiFailure {
    pub fn new(
        status: u16,
        code: impl Into<String>,
        message: impl Into<String>,
        category: impl Into<String>,
    ) -> Self {
        Self {
            status,
            code: code.into(),
            message: message.into(),
            category: category.into(),
            retryable: status >= 500,
        }
    }

    pub fn not_found(code: impl Into<String>, message: impl Into<String>) -> Self {
        Self::new(404, code, message, "not-found")
    }

    pub fn forbidden(message: impl Into<String>) -> Self {
        Self::new(403, "forbidden", message, "forbidden")
    }

    pub fn bad_gateway(code: impl Into<String>, message: impl Into<String>) -> Self {
        Self::new(502, code, message, "integration")
    }

    pub fn bad_request(code: impl Into<String>, message: impl Into<String>) -> Self {
        Self::new(400, code, message, "validation")
    }
}

impl From<ClientError> for ApiFailure {
    fn from(error: ClientError) -> Self {
        let (status, code, message) = overview::client_error_status(&error);
        let category = error.category().to_string();
        let retryable = error.retryable() || status >= 500;
        Self {
            status,
            code,
            message,
            category,
            retryable,
        }
    }
}

impl IntoResponse for ApiFailure {
    fn into_response(self) -> Response {
        (
            StatusCode::from_u16(self.status).unwrap_or(StatusCode::INTERNAL_SERVER_ERROR),
            Json(json!({
                "error": {
                    "code": self.code,
                    "message": self.message,
                    "category": self.category,
                    "retryable": self.retryable
                }
            })),
        )
            .into_response()
    }
}

pub fn router() -> Router<AppState> {
    Router::new()
        .route("/session", get(session))
        .route("/instances", get(list_instances).post(register_instance))
        .route("/users", get(list_users).post(create_user))
        .route("/users/{username}", patch(update_user))
        .route("/users/{username}/password", post(reset_password))
        .route("/account/password", post(change_own_password))
        .route("/audit", get(server_audit))
        .route("/instances/{instance}/overview", get(overview_handler))
        .route(
            "/instances/{instance}/work-items",
            get(work_items).post(submit),
        )
        .route(
            "/instances/{instance}/work-items/{work_item_id}",
            get(work_item),
        )
        .route(
            "/instances/{instance}/work-items/{work_item_id}/events",
            get(work_events),
        )
        .route(
            "/instances/{instance}/work-items/{work_item_id}/contract",
            get(contract),
        )
        .route(
            "/instances/{instance}/work-items/{work_item_id}/lineage",
            get(lineage),
        )
        .route(
            "/instances/{instance}/work-items/{work_item_id}/sessions",
            get(work_sessions),
        )
        .route(
            "/instances/{instance}/work-items/{work_item_id}/diff",
            get(diff),
        )
        .route(
            "/instances/{instance}/work-items/{work_item_id}/files/{*path}",
            get(file),
        )
        .route(
            "/instances/{instance}/work-items/{work_item_id}/serve",
            get(serve_status).post(start_serve).delete(stop_serve),
        )
        .route("/instances/{instance}/requirement/preview", post(preview))
        .route(
            "/instances/{instance}/work-items/{work_item_id}/contract/approve",
            post(approve_contract),
        )
        .route(
            "/instances/{instance}/work-items/{work_item_id}/contract/reject",
            post(reject_contract),
        )
        .route(
            "/instances/{instance}/work-items/{work_item_id}/contract/approve-policy",
            post(approve_contract_policy),
        )
        .route(
            "/instances/{instance}/work-items/{work_item_id}/merge-approvals",
            post(approve_merge),
        )
        .route(
            "/instances/{instance}/work-items/{work_item_id}/planning",
            post(update_planning),
        )
        .route(
            "/instances/{instance}/dispatch",
            get(dispatch_status).post(dispatch),
        )
        .route("/instances/{instance}/jobs/{job_id}", get(dispatch_job))
        .route("/instances/{instance}/approvals", get(approvals))
        .route("/instances/{instance}/reviews", get(reviews))
        .route("/instances/{instance}/releases", get(releases))
        .route(
            "/instances/{instance}/releases/{work_item_id}",
            get(release),
        )
        .route(
            "/instances/{instance}/templates",
            get(templates).post(register_template),
        )
        .route(
            "/instances/{instance}/templates/{template_id}/{version}/approve",
            post(approve_template),
        )
        .route(
            "/instances/{instance}/templates/{template_id}/{version}/revoke",
            post(revoke_template),
        )
        .route("/instances/{instance}/agents", get(agents))
        .route("/instances/{instance}/agents/{definition_id}", get(agent))
        .route(
            "/instances/{instance}/agents/{definition_id}/profiles",
            get(agent_profiles).post(create_profile),
        )
        .route(
            "/instances/{instance}/agents/{definition_id}/profiles/{version}/promote",
            post(promote_profile),
        )
        .route(
            "/instances/{instance}/agents/{definition_id}/rollback",
            post(rollback_profile),
        )
        .route("/instances/{instance}/analytics", get(analytics))
        .route("/instances/{instance}/pipeline", get(pipeline))
        .route("/instances/{instance}/settings", get(settings))
        .route("/instances/{instance}/audit", get(adapter_audit))
        .route("/instances/{instance}/events", get(events))
        .route("/instances/{instance}/events/stream", get(event_stream))
}

// ---------------------------------------------------------------------- session / admin

async fn session(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
) -> Result<Json<control_plane_domain::SessionInfo>, ApiFailure> {
    let instances = state.instances.list_public().await;
    Ok(Json(control_plane_domain::SessionInfo {
        username: user.username.clone(),
        display_name: user.display_name.clone(),
        role: user.role.as_str().to_string(),
        csrf_token: user.csrf.clone(),
        expires_at_utc: user.expires_at.to_rfc3339(),
        instances: instances
            .into_iter()
            .map(|instance| control_plane_domain::InstanceSummary {
                id: instance.id,
                label: instance.label,
                adapter_url: instance.adapter_url,
                target_repository: instance.target_repository,
                healthy: instance.healthy,
                detail: instance.detail,
                implementation_runner: instance.implementation_runner,
                execution_enabled: instance.execution_enabled,
                capabilities: instance.capabilities,
                checked_at_utc: instance.checked_at_utc,
            })
            .collect(),
        server_time_utc: now_rfc3339(),
    }))
}

async fn list_instances(
    State(state): State<AppState>,
    Extension(_user): Extension<AuthUser>,
) -> Json<Vec<PublicInstance>> {
    Json(state.instances.list_public().await)
}

async fn register_instance(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    headers: HeaderMap,
    Json(request): Json<RegisterInstanceRequest>,
) -> Result<(StatusCode, Json<PublicInstance>), ApiFailure> {
    require_admin(&user)?;
    let context = RequestContext::from_request(&headers, "registry", None);
    let instance = InstanceConfig {
        id: request.id.trim().to_ascii_lowercase(),
        label: request.label.trim().to_string(),
        adapter_url: request.adapter_url.trim_end_matches('/').to_string(),
        token: request.token,
        target_repository: request
            .target_repository
            .filter(|value| !value.trim().is_empty()),
    };
    if instance.label.is_empty() {
        return Err(ApiFailure::bad_request(
            "instance-label-required",
            "A display label is required.",
        ));
    }
    if instance.adapter_url.is_empty() {
        return Err(ApiFailure::bad_request(
            "instance-url-required",
            "An adapter URL is required.",
        ));
    }
    let result = state.instances.add(instance.clone()).await;
    match result {
        Ok(public) => {
            let _ = state.audit.append(NewAuditEntry {
                actor: user.username.clone(),
                role: user.role.as_str().to_string(),
                action: "instance.register".to_string(),
                subject: instance.id,
                result: "succeeded".to_string(),
                detail: Some(format!("registered adapter {}", instance.adapter_url)),
                previous_version: None,
                resulting_version: None,
                correlation_id: Some(context.correlation_id),
                operation_id: None,
                idempotency_key: context.idempotency_key,
                remote_address: Some(context.remote),
            });
            Ok((StatusCode::CREATED, Json(public)))
        }
        Err(error) => Err(ApiFailure::bad_gateway(
            "instance-registration-failed",
            error.to_string(),
        )),
    }
}

async fn list_users(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
) -> Result<Json<Vec<control_plane_domain::UserSummary>>, ApiFailure> {
    require_admin(&user)?;
    Ok(Json(
        state
            .auth
            .list_users()
            .into_iter()
            .map(|user| control_plane_domain::UserSummary {
                username: user.username,
                display_name: user.display_name,
                role: user.role.as_str().to_string(),
                active: user.active,
                created_at: user.created_at.to_rfc3339(),
                updated_at: user.updated_at.to_rfc3339(),
            })
            .collect(),
    ))
}

async fn create_user(
    State(state): State<AppState>,
    Extension(actor): Extension<AuthUser>,
    headers: HeaderMap,
    Json(request): Json<CreateUserRequest>,
) -> Result<(StatusCode, Json<control_plane_domain::UserSummary>), ApiFailure> {
    require_admin(&actor)?;
    let role = parse_role(&request.role)?;
    let context = RequestContext::from_request(&headers, "users", None);
    let result = state.auth.create_user(
        &request.username,
        &request.display_name,
        &request.password,
        role,
    );
    match result {
        Ok(user) => {
            let _ = state.audit.append(NewAuditEntry {
                actor: actor.username.clone(),
                role: actor.role.as_str().to_string(),
                action: "user.create".to_string(),
                subject: user.username.clone(),
                result: "succeeded".to_string(),
                detail: Some(format!("role={}", role.as_str())),
                previous_version: None,
                resulting_version: None,
                correlation_id: Some(context.correlation_id),
                operation_id: None,
                idempotency_key: context.idempotency_key,
                remote_address: Some(context.remote),
            });
            Ok((
                StatusCode::CREATED,
                Json(control_plane_domain::UserSummary {
                    username: user.username,
                    display_name: user.display_name,
                    role: user.role.as_str().to_string(),
                    active: user.active,
                    created_at: user.created_at.to_rfc3339(),
                    updated_at: user.updated_at.to_rfc3339(),
                }),
            ))
        }
        Err(error) => Err(ApiFailure::bad_request(
            "user-create-refused",
            error.to_string(),
        )),
    }
}

async fn update_user(
    State(state): State<AppState>,
    Extension(actor): Extension<AuthUser>,
    Path(username): Path<String>,
    headers: HeaderMap,
    Json(request): Json<UpdateUserRequest>,
) -> Result<Json<control_plane_domain::UserSummary>, ApiFailure> {
    require_admin(&actor)?;
    let role = parse_role(&request.role)?;
    if username == actor.username && (!request.active || role != Role::Admin) {
        return Err(ApiFailure::bad_request(
            "self-lockout-refused",
            "An administrator cannot remove their own administrator access or deactivate their own account.",
        ));
    }
    let context = RequestContext::from_request(&headers, "users", None);
    match state.auth.update_role(&username, role, request.active) {
        Ok(user) => {
            let _ = state.audit.append(NewAuditEntry {
                actor: actor.username.clone(),
                role: actor.role.as_str().to_string(),
                action: "user.update".to_string(),
                subject: username,
                result: "succeeded".to_string(),
                detail: Some(format!("role={} active={}", role.as_str(), request.active)),
                previous_version: None,
                resulting_version: None,
                correlation_id: Some(context.correlation_id),
                operation_id: None,
                idempotency_key: context.idempotency_key,
                remote_address: Some(context.remote),
            });
            Ok(Json(control_plane_domain::UserSummary {
                username: user.username,
                display_name: user.display_name,
                role: user.role.as_str().to_string(),
                active: user.active,
                created_at: user.created_at.to_rfc3339(),
                updated_at: user.updated_at.to_rfc3339(),
            }))
        }
        Err(error) => Err(ApiFailure::bad_request(
            "user-update-refused",
            error.to_string(),
        )),
    }
}

async fn reset_password(
    State(state): State<AppState>,
    Extension(actor): Extension<AuthUser>,
    Path(username): Path<String>,
    headers: HeaderMap,
    Json(request): Json<control_plane_domain::ResetPasswordRequest>,
) -> Result<Json<serde_json::Value>, ApiFailure> {
    require_admin(&actor)?;
    let context = RequestContext::from_request(&headers, "users", None);
    match state.auth.reset_password(&username, &request.password) {
        Ok(()) => {
            let _ = state.audit.append(NewAuditEntry {
                actor: actor.username,
                role: actor.role.as_str().to_string(),
                action: "user.password_reset".to_string(),
                subject: username,
                result: "succeeded".to_string(),
                detail: Some(
                    "password hash replaced; existing sessions are not revoked by this action"
                        .to_string(),
                ),
                previous_version: None,
                resulting_version: None,
                correlation_id: Some(context.correlation_id),
                operation_id: None,
                idempotency_key: context.idempotency_key,
                remote_address: Some(context.remote),
            });
            Ok(Json(json!({ "ok": true })))
        }
        Err(error) => Err(ApiFailure::bad_request(
            "password-reset-refused",
            error.to_string(),
        )),
    }
}

async fn change_own_password(
    State(state): State<AppState>,
    Extension(actor): Extension<AuthUser>,
    headers: HeaderMap,
    Json(request): Json<control_plane_domain::ChangePasswordRequest>,
) -> Result<Json<serde_json::Value>, ApiFailure> {
    let context = RequestContext::from_request(&headers, "account", None);
    match state.auth.change_own_password(
        &actor.username,
        &request.current_password,
        &request.new_password,
    ) {
        Ok(()) => {
            let _ = state.audit.append(NewAuditEntry {
                actor: actor.username,
                role: actor.role.as_str().to_string(),
                action: "account.password_change".to_string(),
                subject: "self".to_string(),
                result: "succeeded".to_string(),
                detail: None,
                previous_version: None,
                resulting_version: None,
                correlation_id: Some(context.correlation_id),
                operation_id: None,
                idempotency_key: context.idempotency_key,
                remote_address: Some(context.remote),
            });
            Ok(Json(json!({ "ok": true })))
        }
        Err(error) => Err(ApiFailure::bad_request(
            "password-change-refused",
            error.to_string(),
        )),
    }
}

async fn server_audit(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Query(query): Query<PageQuery>,
) -> Result<Json<Page<AuditEntry>>, ApiFailure> {
    require_operator(&user)?;
    let limit = query.limit.unwrap_or(50).clamp(1, 500);
    let offset = query.offset.unwrap_or(0);
    let (items, total) = state.audit.read(limit, offset).map_err(|error| {
        ApiFailure::new(500, "audit-read-failed", error.to_string(), "internal")
    })?;
    Ok(Json(Page {
        items,
        limit,
        offset,
        total,
        has_more: offset + limit < total,
    }))
}

// ---------------------------------------------------------------------- overview / reads

#[derive(Debug, Deserialize)]
struct PageQuery {
    limit: Option<u32>,
    offset: Option<u32>,
}

#[derive(Debug, Deserialize)]
struct WorkQueryParams {
    state: Option<String>,
    search: Option<String>,
    indicator: Option<String>,
    sort: Option<String>,
    limit: Option<u32>,
    offset: Option<u32>,
}

#[derive(Debug, Deserialize)]
struct WorkPath {
    instance: String,
    work_item_id: String,
}

#[derive(Debug, Deserialize)]
struct InstancePath {
    instance: String,
}

#[derive(Debug, Deserialize)]
struct JobPath {
    instance: String,
    job_id: String,
}

#[derive(Debug, Deserialize)]
struct FilePath {
    instance: String,
    work_item_id: String,
    path: String,
}

#[derive(Debug, Deserialize)]
struct AgentPath {
    instance: String,
    definition_id: String,
}

#[derive(Debug, Deserialize)]
struct TemplatePath {
    instance: String,
    template_id: String,
    version: i32,
}

#[derive(Debug, Deserialize)]
struct EventQuery {
    after: Option<i64>,
    limit: Option<u32>,
}

async fn overview_handler(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<InstancePath>,
    headers: HeaderMap,
) -> Result<Json<control_plane_domain::Overview>, ApiFailure> {
    let correlation = correlation(&headers);
    let client = state.client(&path.instance, &user, correlation).await?;
    Ok(Json(overview::build(&client).await))
}

async fn work_items(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<InstancePath>,
    Query(query): Query<WorkQueryParams>,
    headers: HeaderMap,
) -> Result<Json<Page<control_plane_domain::WorkItemSummary>>, ApiFailure> {
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(
        client
            .work_items(
                query.state.as_deref(),
                query.search.as_deref(),
                query.indicator.as_deref(),
                query.sort.as_deref(),
                query.limit.unwrap_or(100).clamp(1, 500),
                query.offset.unwrap_or(0),
            )
            .await?,
    ))
}

async fn work_item(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<WorkPath>,
    headers: HeaderMap,
) -> Result<Json<control_plane_domain::WorkItemDetail>, ApiFailure> {
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(client.work_item(&path.work_item_id).await?))
}

async fn work_events(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<WorkPath>,
    Query(query): Query<EventQuery>,
    headers: HeaderMap,
) -> Result<Json<Page<control_plane_domain::FactoryEvent>>, ApiFailure> {
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(
        client
            .work_events(
                &path.work_item_id,
                query.after.unwrap_or(0).max(0),
                query.limit.unwrap_or(200).clamp(1, 2000),
            )
            .await?,
    ))
}

async fn contract(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<WorkPath>,
    headers: HeaderMap,
) -> Result<Json<control_plane_domain::Contract>, ApiFailure> {
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(client.contract(&path.work_item_id).await?))
}

async fn lineage(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<WorkPath>,
    headers: HeaderMap,
) -> Result<Json<control_plane_domain::Lineage>, ApiFailure> {
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(client.lineage(&path.work_item_id).await?))
}

async fn diff(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<WorkPath>,
    headers: HeaderMap,
) -> Result<Json<control_plane_domain::Diff>, ApiFailure> {
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(client.diff(&path.work_item_id).await?))
}

async fn work_sessions(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<WorkPath>,
    headers: HeaderMap,
) -> Result<Json<Vec<control_plane_domain::AgentSession>>, ApiFailure> {
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(client.work_sessions(&path.work_item_id).await?))
}

async fn file(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<FilePath>,
    headers: HeaderMap,
) -> Result<Json<control_plane_domain::FileContent>, ApiFailure> {
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(client.file(&path.work_item_id, &path.path).await?))
}

async fn serve_status(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<WorkPath>,
    headers: HeaderMap,
) -> Result<Json<control_plane_domain::ServeStatus>, ApiFailure> {
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(client.serve_status(&path.work_item_id).await?))
}

async fn dispatch_status(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<InstancePath>,
    headers: HeaderMap,
) -> Result<Json<control_plane_domain::DispatchStatus>, ApiFailure> {
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(client.dispatch_status().await?))
}

async fn dispatch_job(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<JobPath>,
    headers: HeaderMap,
) -> Result<Json<control_plane_domain::DispatchJob>, ApiFailure> {
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(client.dispatch_job(&path.job_id).await?))
}

async fn approvals(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<InstancePath>,
    headers: HeaderMap,
) -> Result<Json<control_plane_domain::ApprovalInbox>, ApiFailure> {
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(client.approvals().await?))
}

async fn reviews(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<InstancePath>,
    headers: HeaderMap,
) -> Result<Json<Vec<control_plane_domain::WorkReview>>, ApiFailure> {
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(client.reviews().await?))
}

async fn releases(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<InstancePath>,
    headers: HeaderMap,
) -> Result<Json<control_plane_domain::ReleaseList>, ApiFailure> {
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(client.releases().await?))
}

async fn release(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<WorkPath>,
    headers: HeaderMap,
) -> Result<Json<control_plane_domain::Release>, ApiFailure> {
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(client.release(&path.work_item_id).await?))
}

async fn templates(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<InstancePath>,
    headers: HeaderMap,
) -> Result<Json<Vec<control_plane_domain::Template>>, ApiFailure> {
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(client.templates().await?))
}

async fn agents(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<InstancePath>,
    headers: HeaderMap,
) -> Result<Json<Vec<control_plane_domain::AgentSummary>>, ApiFailure> {
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(client.agents().await?))
}

async fn agent(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<AgentPath>,
    headers: HeaderMap,
) -> Result<Json<control_plane_domain::AgentDetail>, ApiFailure> {
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(client.agent(&path.definition_id).await?))
}

async fn agent_profiles(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<AgentPath>,
    headers: HeaderMap,
) -> Result<Json<Vec<control_plane_domain::Profile>>, ApiFailure> {
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(client.agent_profiles(&path.definition_id).await?))
}

async fn analytics(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<InstancePath>,
    Query(query): Query<AnalyticsQuery>,
    headers: HeaderMap,
) -> Result<Json<control_plane_domain::Analytics>, ApiFailure> {
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(
        client
            .analytics(query.from.as_deref(), query.to.as_deref())
            .await?,
    ))
}

#[derive(Debug, Deserialize)]
struct AnalyticsQuery {
    from: Option<String>,
    to: Option<String>,
}

async fn pipeline(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<InstancePath>,
    Query(query): Query<PipelineQuery>,
    headers: HeaderMap,
) -> Result<Json<control_plane_domain::Pipeline>, ApiFailure> {
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(client.pipeline(query.at.as_deref()).await?))
}

#[derive(Debug, Deserialize)]
struct PipelineQuery {
    at: Option<String>,
}

async fn settings(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<InstancePath>,
    headers: HeaderMap,
) -> Result<Json<control_plane_domain::Settings>, ApiFailure> {
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(client.settings().await?))
}

async fn adapter_audit(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<InstancePath>,
    Query(query): Query<PageQuery>,
    headers: HeaderMap,
) -> Result<Json<Page<AuditEntry>>, ApiFailure> {
    require_operator(&user)?;
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(
        client
            .audit(
                query.limit.unwrap_or(50).clamp(1, 500),
                query.offset.unwrap_or(0),
            )
            .await?,
    ))
}

async fn events(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<InstancePath>,
    Query(query): Query<EventQuery>,
    headers: HeaderMap,
) -> Result<Json<Page<control_plane_domain::EventStreamEnvelope>>, ApiFailure> {
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(
        client
            .events(
                query.after.unwrap_or(0).max(0),
                query.limit.unwrap_or(200).clamp(1, 1000),
            )
            .await?,
    ))
}

// ---------------------------------------------------------------------- mutations

#[derive(Debug, Deserialize)]
struct TemplateRegisterBody {
    manifest_json: String,
}

#[derive(Debug, Deserialize)]
struct TemplateApproveBody {
    manifest_digest: String,
    confirmed: bool,
}

#[derive(Debug, Deserialize)]
struct PromoteProfileBody {
    version: i32,
    confirmed: bool,
}

async fn preview(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<InstancePath>,
    headers: HeaderMap,
    Json(request): Json<PreviewRequest>,
) -> Result<Json<control_plane_domain::Preview>, ApiFailure> {
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;
    Ok(Json(client.preview(&request).await?))
}

async fn submit(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<InstancePath>,
    headers: HeaderMap,
    Json(request): Json<SubmitRequest>,
) -> Result<impl IntoResponse, ApiFailure> {
    require_operator(&user)?;
    let context = RequestContext::from_request(&headers, &path.instance, None);
    let client = state
        .client(&path.instance, &user, Some(context.correlation_id.clone()))
        .await?;
    match client
        .submit(&request, context.idempotency_key.clone())
        .await
    {
        Ok(response) => {
            let duplicate = response.value.duplicate;
            let status = if duplicate {
                StatusCode::OK
            } else {
                StatusCode::CREATED
            };
            audit_success(
                &state,
                &user,
                &context,
                "work.submit",
                &response.value.work_item_id,
                response.operation_id,
                Some(response.value.requirement_digest.clone()),
                None,
                Some(response.value.state.clone()),
            );
            Ok((status, Json(response.value)))
        }
        Err(error) => {
            audit_failure(
                &state,
                &user,
                &context,
                "work.submit",
                "requirement",
                &error,
            );
            Err(error.into())
        }
    }
}

async fn approve_contract(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<WorkPath>,
    headers: HeaderMap,
    Json(request): Json<ContractApproveRequest>,
) -> Result<Json<control_plane_domain::MutationResult>, ApiFailure> {
    require_operator(&user)?;
    let context = RequestContext::from_request(&headers, &path.instance, None);
    let client = state
        .client(&path.instance, &user, Some(context.correlation_id.clone()))
        .await?;
    match client
        .approve_contract(
            &path.work_item_id,
            &request,
            context.idempotency_key.clone(),
        )
        .await
    {
        Ok(response) => {
            audit_success(
                &state,
                &user,
                &context,
                "contract.approve",
                &path.work_item_id,
                response.operation_id,
                Some(request.proposal_digest),
                None,
                None,
            );
            Ok(Json(response.value))
        }
        Err(error) => {
            audit_failure(
                &state,
                &user,
                &context,
                "contract.approve",
                &path.work_item_id,
                &error,
            );
            Err(error.into())
        }
    }
}

async fn reject_contract(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<WorkPath>,
    headers: HeaderMap,
    Json(request): Json<ContractRejectRequest>,
) -> Result<Json<control_plane_domain::MutationResult>, ApiFailure> {
    require_operator(&user)?;
    let context = RequestContext::from_request(&headers, &path.instance, None);
    let client = state
        .client(&path.instance, &user, Some(context.correlation_id.clone()))
        .await?;
    match client
        .reject_contract(
            &path.work_item_id,
            &request,
            context.idempotency_key.clone(),
        )
        .await
    {
        Ok(response) => {
            audit_success(
                &state,
                &user,
                &context,
                "contract.reject",
                &path.work_item_id,
                response.operation_id,
                Some(request.proposal_digest),
                None,
                None,
            );
            Ok(Json(response.value))
        }
        Err(error) => {
            audit_failure(
                &state,
                &user,
                &context,
                "contract.reject",
                &path.work_item_id,
                &error,
            );
            Err(error.into())
        }
    }
}

async fn approve_contract_policy(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<WorkPath>,
    headers: HeaderMap,
) -> Result<Json<control_plane_domain::MutationResult>, ApiFailure> {
    require_operator(&user)?;
    let context = RequestContext::from_request(&headers, &path.instance, None);
    let client = state
        .client(&path.instance, &user, Some(context.correlation_id.clone()))
        .await?;
    match client
        .approve_contract_policy(&path.work_item_id, context.idempotency_key.clone())
        .await
    {
        Ok(response) => {
            audit_success(
                &state,
                &user,
                &context,
                "contract.approve-policy",
                &path.work_item_id,
                response.operation_id,
                None,
                None,
                None,
            );
            Ok(Json(response.value))
        }
        Err(error) => {
            audit_failure(
                &state,
                &user,
                &context,
                "contract.approve-policy",
                &path.work_item_id,
                &error,
            );
            Err(error.into())
        }
    }
}

async fn approve_merge(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<WorkPath>,
    headers: HeaderMap,
    Json(request): Json<MergeApprovalRequest>,
) -> Result<Json<control_plane_domain::MutationResult>, ApiFailure> {
    require_operator(&user)?;
    let context = RequestContext::from_request(&headers, &path.instance, None);
    let client = state
        .client(&path.instance, &user, Some(context.correlation_id.clone()))
        .await?;
    match client
        .approve_merge(
            &path.work_item_id,
            &request,
            context.idempotency_key.clone(),
        )
        .await
    {
        Ok(response) => {
            audit_success(
                &state,
                &user,
                &context,
                "merge.approve",
                &path.work_item_id,
                response.operation_id,
                Some(request.review_digest),
                None,
                None,
            );
            Ok(Json(response.value))
        }
        Err(error) => {
            audit_failure(
                &state,
                &user,
                &context,
                "merge.approve",
                &path.work_item_id,
                &error,
            );
            Err(error.into())
        }
    }
}

async fn update_planning(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<WorkPath>,
    headers: HeaderMap,
    Json(request): Json<PlanningUpdateRequest>,
) -> Result<Json<serde_json::Value>, ApiFailure> {
    require_operator(&user)?;
    let context = RequestContext::from_request(&headers, &path.instance, None);
    let client = state
        .client(&path.instance, &user, Some(context.correlation_id.clone()))
        .await?;
    match client
        .update_planning(
            &path.work_item_id,
            &request,
            context.idempotency_key.clone(),
        )
        .await
    {
        Ok(response) => {
            audit_success(
                &state,
                &user,
                &context,
                "work.planning.update",
                &path.work_item_id,
                response.operation_id,
                None,
                None,
                None,
            );
            Ok(Json(response.value))
        }
        Err(error) => {
            audit_failure(
                &state,
                &user,
                &context,
                "work.planning.update",
                &path.work_item_id,
                &error,
            );
            Err(error.into())
        }
    }
}

async fn dispatch(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<InstancePath>,
    headers: HeaderMap,
    Json(request): Json<DispatchRequest>,
) -> Result<(StatusCode, Json<control_plane_domain::MutationResult>), ApiFailure> {
    require_operator(&user)?;
    let context = RequestContext::from_request(&headers, &path.instance, None);
    let client = state
        .client(&path.instance, &user, Some(context.correlation_id.clone()))
        .await?;
    match client
        .dispatch(request, context.idempotency_key.clone())
        .await
    {
        Ok(response) => {
            audit_success(
                &state,
                &user,
                &context,
                "factory.dispatch",
                &path.instance,
                response.operation_id,
                None,
                None,
                None,
            );
            Ok((StatusCode::ACCEPTED, Json(response.value)))
        }
        Err(error) => {
            audit_failure(
                &state,
                &user,
                &context,
                "factory.dispatch",
                &path.instance,
                &error,
            );
            Err(error.into())
        }
    }
}

async fn start_serve(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<WorkPath>,
    headers: HeaderMap,
    Json(request): Json<ServeRequest>,
) -> Result<(StatusCode, Json<control_plane_domain::MutationResult>), ApiFailure> {
    require_operator(&user)?;
    let context = RequestContext::from_request(&headers, &path.instance, None);
    let client = state
        .client(&path.instance, &user, Some(context.correlation_id.clone()))
        .await?;
    match client
        .start_serve(
            &path.work_item_id,
            &request,
            context.idempotency_key.clone(),
        )
        .await
    {
        Ok(response) => {
            audit_success(
                &state,
                &user,
                &context,
                "release.serve.start",
                &path.work_item_id,
                response.operation_id,
                None,
                None,
                None,
            );
            Ok((StatusCode::ACCEPTED, Json(response.value)))
        }
        Err(error) => {
            audit_failure(
                &state,
                &user,
                &context,
                "release.serve.start",
                &path.work_item_id,
                &error,
            );
            Err(error.into())
        }
    }
}

async fn stop_serve(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<WorkPath>,
    headers: HeaderMap,
) -> Result<Json<control_plane_domain::MutationResult>, ApiFailure> {
    require_operator(&user)?;
    let context = RequestContext::from_request(&headers, &path.instance, None);
    let client = state
        .client(&path.instance, &user, Some(context.correlation_id.clone()))
        .await?;
    match client
        .stop_serve(&path.work_item_id, context.idempotency_key.clone())
        .await
    {
        Ok(response) => {
            audit_success(
                &state,
                &user,
                &context,
                "release.serve.stop",
                &path.work_item_id,
                response.operation_id,
                None,
                None,
                None,
            );
            Ok(Json(response.value))
        }
        Err(error) => {
            audit_failure(
                &state,
                &user,
                &context,
                "release.serve.stop",
                &path.work_item_id,
                &error,
            );
            Err(error.into())
        }
    }
}

async fn register_template(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<InstancePath>,
    headers: HeaderMap,
    Json(body): Json<TemplateRegisterBody>,
) -> Result<(StatusCode, Json<control_plane_domain::Template>), ApiFailure> {
    require_admin(&user)?;
    let context = RequestContext::from_request(&headers, &path.instance, None);
    let client = state
        .client(&path.instance, &user, Some(context.correlation_id.clone()))
        .await?;
    match client
        .register_template(&body.manifest_json, context.idempotency_key.clone())
        .await
    {
        Ok(response) => {
            audit_success(
                &state,
                &user,
                &context,
                "template.register",
                &format!(
                    "{}@{}",
                    response.value.template_id, response.value.template_version
                ),
                response.operation_id,
                Some(response.value.manifest_digest.clone()),
                None,
                None,
            );
            Ok((StatusCode::CREATED, Json(response.value)))
        }
        Err(error) => {
            audit_failure(
                &state,
                &user,
                &context,
                "template.register",
                "template",
                &error,
            );
            Err(error.into())
        }
    }
}

async fn approve_template(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<TemplatePath>,
    headers: HeaderMap,
    Json(body): Json<TemplateApproveBody>,
) -> Result<Json<control_plane_domain::Template>, ApiFailure> {
    require_admin(&user)?;
    let context = RequestContext::from_request(&headers, &path.instance, None);
    let client = state
        .client(&path.instance, &user, Some(context.correlation_id.clone()))
        .await?;
    match client
        .approve_template(
            &path.template_id,
            path.version,
            &body.manifest_digest,
            body.confirmed,
            context.idempotency_key.clone(),
        )
        .await
    {
        Ok(response) => {
            audit_success(
                &state,
                &user,
                &context,
                "template.approve",
                &format!("{}@{}", path.template_id, path.version),
                response.operation_id,
                Some(body.manifest_digest),
                None,
                None,
            );
            Ok(Json(response.value))
        }
        Err(error) => {
            audit_failure(
                &state,
                &user,
                &context,
                "template.approve",
                &format!("{}@{}", path.template_id, path.version),
                &error,
            );
            Err(error.into())
        }
    }
}

async fn revoke_template(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<TemplatePath>,
    headers: HeaderMap,
) -> Result<Json<control_plane_domain::Template>, ApiFailure> {
    require_admin(&user)?;
    let context = RequestContext::from_request(&headers, &path.instance, None);
    let client = state
        .client(&path.instance, &user, Some(context.correlation_id.clone()))
        .await?;
    match client
        .revoke_template(
            &path.template_id,
            path.version,
            context.idempotency_key.clone(),
        )
        .await
    {
        Ok(response) => {
            audit_success(
                &state,
                &user,
                &context,
                "template.revoke",
                &format!("{}@{}", path.template_id, path.version),
                response.operation_id,
                None,
                None,
                None,
            );
            Ok(Json(response.value))
        }
        Err(error) => {
            audit_failure(
                &state,
                &user,
                &context,
                "template.revoke",
                &format!("{}@{}", path.template_id, path.version),
                &error,
            );
            Err(error.into())
        }
    }
}

async fn create_profile(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<AgentPath>,
    headers: HeaderMap,
    Json(body): Json<ProfileDraftRequest>,
) -> Result<(StatusCode, Json<control_plane_domain::Profile>), ApiFailure> {
    require_admin(&user)?;
    let context = RequestContext::from_request(&headers, &path.instance, None);
    let client = state
        .client(&path.instance, &user, Some(context.correlation_id.clone()))
        .await?;
    let payload = serde_json::to_value(&body)
        .map_err(|error| ApiFailure::bad_request("profile-body-invalid", error.to_string()))?;
    match client
        .create_profile(
            &path.definition_id,
            &payload,
            context.idempotency_key.clone(),
        )
        .await
    {
        Ok(response) => {
            audit_success(
                &state,
                &user,
                &context,
                "agent.profile.publish",
                &format!("{}@{}", path.definition_id, response.value.version),
                response.operation_id,
                Some(response.value.content_digest.clone()),
                None,
                None,
            );
            Ok((StatusCode::CREATED, Json(response.value)))
        }
        Err(error) => {
            audit_failure(
                &state,
                &user,
                &context,
                "agent.profile.publish",
                &path.definition_id,
                &error,
            );
            Err(error.into())
        }
    }
}

async fn promote_profile(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<AgentPath>,
    headers: HeaderMap,
    Json(body): Json<PromoteProfileBody>,
) -> Result<Json<control_plane_domain::Profile>, ApiFailure> {
    require_admin(&user)?;
    let context = RequestContext::from_request(&headers, &path.instance, None);
    let client = state
        .client(&path.instance, &user, Some(context.correlation_id.clone()))
        .await?;
    match client
        .promote_profile(
            &path.definition_id,
            body.version,
            body.confirmed,
            context.idempotency_key.clone(),
        )
        .await
    {
        Ok(response) => {
            audit_success(
                &state,
                &user,
                &context,
                "agent.profile.promote",
                &format!("{}@{}", path.definition_id, body.version),
                response.operation_id,
                Some(response.value.content_digest.clone()),
                None,
                None,
            );
            Ok(Json(response.value))
        }
        Err(error) => {
            audit_failure(
                &state,
                &user,
                &context,
                "agent.profile.promote",
                &path.definition_id,
                &error,
            );
            Err(error.into())
        }
    }
}

async fn rollback_profile(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<AgentPath>,
    headers: HeaderMap,
) -> Result<Json<control_plane_domain::Profile>, ApiFailure> {
    require_admin(&user)?;
    let context = RequestContext::from_request(&headers, &path.instance, None);
    let client = state
        .client(&path.instance, &user, Some(context.correlation_id.clone()))
        .await?;
    match client
        .rollback_profile(&path.definition_id, context.idempotency_key.clone())
        .await
    {
        Ok(response) => {
            audit_success(
                &state,
                &user,
                &context,
                "agent.profile.rollback",
                &path.definition_id,
                response.operation_id,
                Some(response.value.content_digest.clone()),
                None,
                None,
            );
            Ok(Json(response.value))
        }
        Err(error) => {
            audit_failure(
                &state,
                &user,
                &context,
                "agent.profile.rollback",
                &path.definition_id,
                &error,
            );
            Err(error.into())
        }
    }
}

// ---------------------------------------------------------------------- SSE relay

async fn event_stream(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    Path(path): Path<InstancePath>,
    Query(query): Query<EventQuery>,
    headers: HeaderMap,
) -> Result<Sse<impl futures_util::Stream<Item = Result<Event, Infallible>>>, ApiFailure> {
    let last_event_id = headers
        .get("last-event-id")
        .and_then(|value| value.to_str().ok())
        .and_then(|value| value.parse::<i64>().ok());
    let after = query.after.or(last_event_id);
    let client = state
        .client(&path.instance, &user, correlation(&headers))
        .await?;

    let stream = async_stream::stream! {
        let mut cursor = after;
        loop {
            match client.event_stream(cursor, cursor).await {
                Ok(mut upstream) => {
                    let mut upstream = Box::pin(upstream);
                    while let Some(frame) = upstream.next().await {
                        match frame {
                            Ok(frame) => {
                                if let Some(id) = frame.id {
                                    cursor = Some(id);
                                }
                                let event = Event::default()
                                    .event(frame.event)
                                    .data(frame.data);
                                let event = match frame.id {
                                    Some(id) => event.id(id.to_string()),
                                    None => event,
                                };
                                yield Ok(event.retry(std::time::Duration::from_secs(2)));
                            }
                            Err(error) => {
                                let payload = json!({
                                    "status": "disconnected",
                                    "message": error.to_string(),
                                    "atUtc": Utc::now().to_rfc3339()
                                });
                                yield Ok(Event::default().event("gateway").data(payload.to_string()));
                                break;
                            }
                        }
                    }
                }
                Err(error) => {
                    let payload = json!({
                        "status": "disconnected",
                        "message": error.to_string(),
                        "atUtc": Utc::now().to_rfc3339()
                    });
                    yield Ok(Event::default().event("gateway").data(payload.to_string()));
                }
            }

            tokio::time::sleep(std::time::Duration::from_secs(3)).await;
        }
    };

    Ok(Sse::new(stream).keep_alive(
        KeepAlive::new()
            .interval(std::time::Duration::from_secs(15))
            .text("control-plane-alive"),
    ))
}

// ---------------------------------------------------------------------- helpers

fn correlation(headers: &HeaderMap) -> Option<String> {
    headers
        .get("x-request-id")
        .and_then(|value| value.to_str().ok())
        .filter(|value| !value.is_empty() && value.len() <= 80)
        .map(str::to_string)
}

fn require_operator(user: &AuthUser) -> Result<(), ApiFailure> {
    if user.role.can_write() {
        Ok(())
    } else {
        Err(ApiFailure::forbidden(
            "A read-only viewer may inspect the factory but may not mutate it.",
        ))
    }
}

fn require_admin(user: &AuthUser) -> Result<(), ApiFailure> {
    if user.role.is_admin() {
        Ok(())
    } else {
        Err(ApiFailure::new(
            403,
            "administrator-required",
            "This action changes execution authority and requires the administrator role.",
            "forbidden",
        ))
    }
}

fn parse_role(value: &str) -> Result<Role, ApiFailure> {
    match value.trim().to_ascii_lowercase().as_str() {
        "viewer" => Ok(Role::Viewer),
        "operator" => Ok(Role::Operator),
        "admin" => Ok(Role::Admin),
        _ => Err(ApiFailure::bad_request(
            "role-invalid",
            "Role must be viewer, operator or admin.",
        )),
    }
}

fn audit_success(
    state: &AppState,
    user: &AuthUser,
    context: &RequestContext,
    action: &str,
    subject: &str,
    operation_id: Option<String>,
    detail: Option<String>,
    previous_version: Option<String>,
    resulting_version: Option<String>,
) {
    let _ = state.audit.append(NewAuditEntry {
        actor: user.username.clone(),
        role: user.role.as_str().to_string(),
        action: action.to_string(),
        subject: subject.to_string(),
        result: "succeeded".to_string(),
        detail,
        previous_version,
        resulting_version,
        correlation_id: Some(context.correlation_id.clone()),
        operation_id,
        idempotency_key: context.idempotency_key.clone(),
        remote_address: Some(context.remote.clone()),
    });
}

fn audit_failure(
    state: &AppState,
    user: &AuthUser,
    context: &RequestContext,
    action: &str,
    subject: &str,
    error: &ClientError,
) {
    let _ = state.audit.append(NewAuditEntry {
        actor: user.username.clone(),
        role: user.role.as_str().to_string(),
        action: action.to_string(),
        subject: subject.to_string(),
        result: "refused".to_string(),
        detail: Some(error.to_string()),
        previous_version: None,
        resulting_version: None,
        correlation_id: Some(context.correlation_id.clone()),
        operation_id: None,
        idempotency_key: context.idempotency_key.clone(),
        remote_address: Some(context.remote.clone()),
    });
}
