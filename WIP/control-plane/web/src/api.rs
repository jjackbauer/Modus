//! Browser-side typed access to the Rust control-plane API.
//!
//! Every request is same-origin and authenticated by the HttpOnly session cookie. Mutations also
//! carry the session CSRF token. The adapter credential never reaches this layer.

use control_plane_domain::*;
use serde::de::DeserializeOwned;
use serde::{Deserialize, Serialize};
use serde_json::Value;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct UiError {
    pub status: u16,
    pub code: String,
    pub message: String,
    pub category: String,
    pub retryable: bool,
}

impl UiError {
    pub fn new(status: u16, code: impl Into<String>, message: impl Into<String>) -> Self {
        Self {
            status,
            code: code.into(),
            message: message.into(),
            category: "internal".to_string(),
            retryable: status >= 500,
        }
    }

    pub fn network(message: impl Into<String>) -> Self {
        Self {
            status: 0,
            code: "network".to_string(),
            message: message.into(),
            category: "unavailable".to_string(),
            retryable: true,
        }
    }

    pub fn server() -> Self {
        Self::new(0, "server-rendering", "Not loaded during server rendering.")
    }

    pub fn is_csrf(&self) -> bool {
        self.code == "csrf-token-invalid" || self.code == "authentication-required"
    }
}

pub type UiResult<T> = Result<T, UiError>;

pub fn is_client() -> bool {
    cfg!(target_arch = "wasm32")
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
struct ErrorEnvelope {
    error: ErrorBody,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
struct ErrorBody {
    code: String,
    message: String,
    category: String,
    retryable: bool,
}

fn encode(value: &str) -> String {
    percent_encoding::utf8_percent_encode(value, percent_encoding::NON_ALPHANUMERIC).to_string()
}

#[cfg(target_arch = "wasm32")]
fn redirect_to_login() {
    let Some(window) = web_sys::window() else {
        return;
    };
    let location = window.location();
    let Ok(path) = location.pathname() else {
        return;
    };
    if path == "/login" {
        return;
    }
    let search = location.search().unwrap_or_default();
    let next = js_sys::encode_uri_component(&format!("{path}{search}"))
        .as_string()
        .unwrap_or_default();
    let _ = location.set_href(&format!("/login?next={next}"));
}

fn instance_base(instance: &str) -> String {
    format!("/api/instances/{}", encode(instance))
}

async fn parse_response<T: DeserializeOwned>(response: gloo_net::http::Response) -> UiResult<T> {
    let status = response.status();
    let text = response
        .text()
        .await
        .map_err(|error| UiError::network(error.to_string()))?;

    // An expired or revoked session is not a page error: send the operator to sign-in and return
    // to the current page afterwards. The login handler sanitises `next` to a local path.
    #[cfg(target_arch = "wasm32")]
    if status == 401 {
        redirect_to_login();
    }

    if !(200..300).contains(&status) {
        if let Ok(envelope) = serde_json::from_str::<ErrorEnvelope>(&text) {
            return Err(UiError {
                status,
                code: envelope.error.code,
                message: envelope.error.message,
                category: envelope.error.category,
                retryable: envelope.error.retryable,
            });
        }

        return Err(UiError::new(
            status,
            "request-failed",
            if text.is_empty() {
                format!("The control plane returned HTTP {status}.")
            } else {
                text.chars().take(500).collect()
            },
        ));
    }

    serde_json::from_str::<T>(&text)
        .map_err(|error| UiError::new(502, "response-decode", error.to_string()))
}

#[cfg(target_arch = "wasm32")]
async fn get<T: DeserializeOwned>(path: &str) -> UiResult<T> {
    let response = gloo_net::http::Request::get(path)
        .send()
        .await
        .map_err(|error| UiError::network(error.to_string()))?;
    parse_response(response).await
}

#[cfg(not(target_arch = "wasm32"))]
async fn get<T: DeserializeOwned>(_path: &str) -> UiResult<T> {
    Err(UiError::server())
}

#[cfg(target_arch = "wasm32")]
async fn send_json<T: DeserializeOwned, B: Serialize + ?Sized>(
    method: &str,
    path: &str,
    body: Option<&B>,
    csrf: &str,
    idempotency_key: Option<&str>,
) -> UiResult<T> {
    let mut request = match method {
        "POST" => gloo_net::http::Request::post(path),
        "PATCH" => gloo_net::http::Request::patch(path),
        "DELETE" => gloo_net::http::Request::delete(path),
        _ => gloo_net::http::Request::get(path),
    }
    .header("x-csrf-token", csrf);

    if let Some(key) = idempotency_key {
        request = request.header("idempotency-key", key);
    }

    let request = match body {
        Some(body) => request
            .json(body)
            .map_err(|error| UiError::new(0, "request-encode", error.to_string()))?,
        None => request
            .build()
            .map_err(|error| UiError::new(0, "request-build", error.to_string()))?,
    };

    let response = request
        .send()
        .await
        .map_err(|error| UiError::network(error.to_string()))?;
    parse_response(response).await
}

#[cfg(not(target_arch = "wasm32"))]
async fn send_json<T: DeserializeOwned, B: Serialize + ?Sized>(
    _method: &str,
    _path: &str,
    _body: Option<&B>,
    _csrf: &str,
    _idempotency_key: Option<&str>,
) -> UiResult<T> {
    Err(UiError::server())
}

pub fn new_idempotency_key() -> String {
    uuid::Uuid::new_v4().to_string()
}

// ---------------------------------------------------------------------- session

pub async fn session() -> UiResult<SessionInfo> {
    get("/api/session").await
}

pub async fn logout(csrf: &str) -> UiResult<()> {
    let _: Value = send_json(
        "POST",
        "/api/logout",
        Some(&serde_json::json!({})),
        csrf,
        Some(&new_idempotency_key()),
    )
    .await?;
    Ok(())
}

pub async fn list_instances() -> UiResult<Vec<InstanceSummary>> {
    get("/api/instances").await
}

pub async fn register_instance(
    csrf: &str,
    request: &RegisterInstanceRequest,
) -> UiResult<InstanceSummary> {
    send_json(
        "POST",
        "/api/instances",
        Some(request),
        csrf,
        Some(&new_idempotency_key()),
    )
    .await
}

// ---------------------------------------------------------------------- overview and work

pub async fn overview(instance: &str) -> UiResult<Overview> {
    get(&format!("{}/overview", instance_base(instance))).await
}

pub async fn work_items(
    instance: &str,
    state: Option<&str>,
    search: Option<&str>,
    indicator: Option<&str>,
    sort: Option<&str>,
    limit: u32,
    offset: u32,
) -> UiResult<Page<WorkItemSummary>> {
    let mut query = vec![format!("limit={limit}"), format!("offset={offset}")];
    for (key, value) in [
        ("state", state),
        ("search", search),
        ("indicator", indicator),
        ("sort", sort),
    ] {
        if let Some(value) = value.filter(|value| !value.trim().is_empty()) {
            query.push(format!("{key}={}", encode(value)));
        }
    }
    get(&format!(
        "{}/work-items?{}",
        instance_base(instance),
        query.join("&")
    ))
    .await
}

pub async fn work_item(instance: &str, work_item_id: &str) -> UiResult<WorkItemDetail> {
    get(&format!(
        "{}/work-items/{}",
        instance_base(instance),
        encode(work_item_id)
    ))
    .await
}

pub async fn work_events(
    instance: &str,
    work_item_id: &str,
    after: i64,
    limit: u32,
) -> UiResult<Page<FactoryEvent>> {
    get(&format!(
        "{}/work-items/{}/events?after={after}&limit={limit}",
        instance_base(instance),
        encode(work_item_id)
    ))
    .await
}

pub async fn contract(instance: &str, work_item_id: &str) -> UiResult<Contract> {
    get(&format!(
        "{}/work-items/{}/contract",
        instance_base(instance),
        encode(work_item_id)
    ))
    .await
}

pub async fn lineage(instance: &str, work_item_id: &str) -> UiResult<Lineage> {
    get(&format!(
        "{}/work-items/{}/lineage",
        instance_base(instance),
        encode(work_item_id)
    ))
    .await
}

pub async fn diff(instance: &str, work_item_id: &str) -> UiResult<Diff> {
    get(&format!(
        "{}/work-items/{}/diff",
        instance_base(instance),
        encode(work_item_id)
    ))
    .await
}

pub async fn file(instance: &str, work_item_id: &str, path: &str) -> UiResult<FileContent> {
    let encoded_path = path.split('/').map(encode).collect::<Vec<_>>().join("/");
    get(&format!(
        "{}/work-items/{}/files/{}",
        instance_base(instance),
        encode(work_item_id),
        encoded_path
    ))
    .await
}

pub async fn work_sessions(instance: &str, work_item_id: &str) -> UiResult<Vec<AgentSession>> {
    get(&format!(
        "{}/work-items/{}/sessions",
        instance_base(instance),
        encode(work_item_id)
    ))
    .await
}

pub async fn preview(instance: &str, csrf: &str, request: &PreviewRequest) -> UiResult<Preview> {
    send_json(
        "POST",
        &format!("{}/requirement/preview", instance_base(instance)),
        Some(request),
        csrf,
        None,
    )
    .await
}

pub async fn submit(
    instance: &str,
    csrf: &str,
    request: &SubmitRequest,
    idempotency_key: &str,
) -> UiResult<SubmitResponse> {
    send_json(
        "POST",
        &format!("{}/work-items", instance_base(instance)),
        Some(request),
        csrf,
        Some(idempotency_key),
    )
    .await
}

pub async fn approve_contract(
    instance: &str,
    csrf: &str,
    work_item_id: &str,
    request: &ContractApproveRequest,
    idempotency_key: &str,
) -> UiResult<MutationResult> {
    send_json(
        "POST",
        &format!(
            "{}/work-items/{}/contract/approve",
            instance_base(instance),
            encode(work_item_id)
        ),
        Some(request),
        csrf,
        Some(idempotency_key),
    )
    .await
}

pub async fn reject_contract(
    instance: &str,
    csrf: &str,
    work_item_id: &str,
    request: &ContractRejectRequest,
    idempotency_key: &str,
) -> UiResult<MutationResult> {
    send_json(
        "POST",
        &format!(
            "{}/work-items/{}/contract/reject",
            instance_base(instance),
            encode(work_item_id)
        ),
        Some(request),
        csrf,
        Some(idempotency_key),
    )
    .await
}

pub async fn approve_merge(
    instance: &str,
    csrf: &str,
    work_item_id: &str,
    request: &MergeApprovalRequest,
    idempotency_key: &str,
) -> UiResult<MutationResult> {
    send_json(
        "POST",
        &format!(
            "{}/work-items/{}/merge-approvals",
            instance_base(instance),
            encode(work_item_id)
        ),
        Some(request),
        csrf,
        Some(idempotency_key),
    )
    .await
}

pub async fn update_planning(
    instance: &str,
    csrf: &str,
    work_item_id: &str,
    request: &PlanningUpdateRequest,
    idempotency_key: &str,
) -> UiResult<Planning> {
    send_json(
        "POST",
        &format!(
            "{}/work-items/{}/planning",
            instance_base(instance),
            encode(work_item_id)
        ),
        Some(request),
        csrf,
        Some(idempotency_key),
    )
    .await
}

pub async fn dispatch(instance: &str, csrf: &str, max_passes: i32) -> UiResult<MutationResult> {
    send_json(
        "POST",
        &format!("{}/dispatch", instance_base(instance)),
        Some(&DispatchRequest {
            max_passes: Some(max_passes),
        }),
        csrf,
        Some(&new_idempotency_key()),
    )
    .await
}

pub async fn dispatch_status(instance: &str) -> UiResult<DispatchStatus> {
    get(&format!("{}/dispatch", instance_base(instance))).await
}

pub async fn dispatch_job(instance: &str, job_id: &str) -> UiResult<DispatchJob> {
    get(&format!(
        "{}/jobs/{}",
        instance_base(instance),
        encode(job_id)
    ))
    .await
}

pub async fn serve_status(instance: &str, work_item_id: &str) -> UiResult<ServeStatus> {
    get(&format!(
        "{}/work-items/{}/serve",
        instance_base(instance),
        encode(work_item_id)
    ))
    .await
}

pub async fn start_serve(
    instance: &str,
    csrf: &str,
    work_item_id: &str,
    seconds: i32,
) -> UiResult<MutationResult> {
    send_json(
        "POST",
        &format!(
            "{}/work-items/{}/serve",
            instance_base(instance),
            encode(work_item_id)
        ),
        Some(&ServeRequest {
            duration_seconds: seconds,
        }),
        csrf,
        Some(&new_idempotency_key()),
    )
    .await
}

pub async fn stop_serve(
    instance: &str,
    csrf: &str,
    work_item_id: &str,
) -> UiResult<MutationResult> {
    send_json(
        "DELETE",
        &format!(
            "{}/work-items/{}/serve",
            instance_base(instance),
            encode(work_item_id)
        ),
        None::<&Value>,
        csrf,
        Some(&new_idempotency_key()),
    )
    .await
}

// ---------------------------------------------------------------------- evidence

pub async fn approvals(instance: &str) -> UiResult<ApprovalInbox> {
    get(&format!("{}/approvals", instance_base(instance))).await
}

pub async fn reviews(instance: &str) -> UiResult<Vec<WorkReview>> {
    get(&format!("{}/reviews", instance_base(instance))).await
}

pub async fn releases(instance: &str) -> UiResult<ReleaseList> {
    get(&format!("{}/releases", instance_base(instance))).await
}

pub async fn release(instance: &str, work_item_id: &str) -> UiResult<Release> {
    get(&format!(
        "{}/releases/{}",
        instance_base(instance),
        encode(work_item_id)
    ))
    .await
}

pub async fn templates(instance: &str) -> UiResult<Vec<Template>> {
    get(&format!("{}/templates", instance_base(instance))).await
}

pub async fn register_template(
    instance: &str,
    csrf: &str,
    manifest_json: &str,
) -> UiResult<Template> {
    send_json(
        "POST",
        &format!("{}/templates", instance_base(instance)),
        Some(&serde_json::json!({ "manifestJson": manifest_json })),
        csrf,
        Some(&new_idempotency_key()),
    )
    .await
}

pub async fn approve_template(
    instance: &str,
    csrf: &str,
    template_id: &str,
    version: i32,
    digest: &str,
    confirmed: bool,
) -> UiResult<Template> {
    send_json(
        "POST",
        &format!(
            "{}/templates/{}/{}/approve",
            instance_base(instance),
            encode(template_id),
            version
        ),
        Some(&serde_json::json!({ "manifestDigest": digest, "confirmed": confirmed })),
        csrf,
        Some(&new_idempotency_key()),
    )
    .await
}

pub async fn revoke_template(
    instance: &str,
    csrf: &str,
    template_id: &str,
    version: i32,
) -> UiResult<Template> {
    send_json(
        "POST",
        &format!(
            "{}/templates/{}/{}/revoke",
            instance_base(instance),
            encode(template_id),
            version
        ),
        Some(&serde_json::json!({})),
        csrf,
        Some(&new_idempotency_key()),
    )
    .await
}

// ---------------------------------------------------------------------- agents

pub async fn agents(instance: &str) -> UiResult<Vec<AgentSummary>> {
    get(&format!("{}/agents", instance_base(instance))).await
}

pub async fn agent(instance: &str, definition_id: &str) -> UiResult<AgentDetail> {
    get(&format!(
        "{}/agents/{}",
        instance_base(instance),
        encode(definition_id)
    ))
    .await
}

pub async fn agent_profiles(instance: &str, definition_id: &str) -> UiResult<Vec<Profile>> {
    get(&format!(
        "{}/agents/{}/profiles",
        instance_base(instance),
        encode(definition_id)
    ))
    .await
}

pub async fn create_profile(
    instance: &str,
    csrf: &str,
    definition_id: &str,
    body: &Value,
) -> UiResult<Profile> {
    send_json(
        "POST",
        &format!(
            "{}/agents/{}/profiles",
            instance_base(instance),
            encode(definition_id)
        ),
        Some(body),
        csrf,
        Some(&new_idempotency_key()),
    )
    .await
}

pub async fn promote_profile(
    instance: &str,
    csrf: &str,
    definition_id: &str,
    version: i32,
    confirmed: bool,
) -> UiResult<Profile> {
    send_json(
        "POST",
        &format!(
            "{}/agents/{}/profiles/{}/promote",
            instance_base(instance),
            encode(definition_id),
            version
        ),
        Some(&serde_json::json!({ "version": version, "confirmed": confirmed })),
        csrf,
        Some(&new_idempotency_key()),
    )
    .await
}

pub async fn rollback_profile(
    instance: &str,
    csrf: &str,
    definition_id: &str,
) -> UiResult<Profile> {
    send_json(
        "POST",
        &format!(
            "{}/agents/{}/rollback",
            instance_base(instance),
            encode(definition_id)
        ),
        Some(&serde_json::json!({})),
        csrf,
        Some(&new_idempotency_key()),
    )
    .await
}

// ---------------------------------------------------------------------- analytics / settings

pub async fn analytics(
    instance: &str,
    from: Option<&str>,
    to: Option<&str>,
) -> UiResult<Analytics> {
    let mut query = Vec::new();
    if let Some(from) = from {
        query.push(format!("from={}", encode(from)));
    }
    if let Some(to) = to {
        query.push(format!("to={}", encode(to)));
    }
    let suffix = if query.is_empty() {
        String::new()
    } else {
        format!("?{}", query.join("&"))
    };
    get(&format!("{}/analytics{suffix}", instance_base(instance))).await
}

pub async fn pipeline(instance: &str, at: Option<&str>) -> UiResult<Pipeline> {
    match at {
        Some(at) => {
            get(&format!(
                "{}/pipeline?at={}",
                instance_base(instance),
                encode(at)
            ))
            .await
        }
        None => get(&format!("{}/pipeline", instance_base(instance))).await,
    }
}

pub async fn settings(instance: &str) -> UiResult<Settings> {
    get(&format!("{}/settings", instance_base(instance))).await
}

pub async fn adapter_audit(instance: &str, limit: u32, offset: u32) -> UiResult<Page<AuditEntry>> {
    get(&format!(
        "{}/audit?limit={limit}&offset={offset}",
        instance_base(instance)
    ))
    .await
}

pub async fn server_audit(limit: u32, offset: u32) -> UiResult<Page<AuditEntry>> {
    get(&format!("/api/audit?limit={limit}&offset={offset}")).await
}

pub async fn users() -> UiResult<Vec<UserSummary>> {
    get("/api/users").await
}

pub async fn create_user(csrf: &str, request: &CreateUserRequest) -> UiResult<UserSummary> {
    send_json(
        "POST",
        "/api/users",
        Some(request),
        csrf,
        Some(&new_idempotency_key()),
    )
    .await
}

pub async fn update_user(
    username: &str,
    csrf: &str,
    request: &UpdateUserRequest,
) -> UiResult<UserSummary> {
    send_json(
        "PATCH",
        &format!("/api/users/{}", encode(username)),
        Some(request),
        csrf,
        None,
    )
    .await
}

pub async fn reset_password(username: &str, csrf: &str, password: &str) -> UiResult<Value> {
    send_json(
        "POST",
        &format!("/api/users/{}/password", encode(username)),
        Some(&serde_json::json!({ "password": password })),
        csrf,
        Some(&new_idempotency_key()),
    )
    .await
}

pub async fn change_password(csrf: &str, current: &str, new: &str) -> UiResult<Value> {
    send_json(
        "POST",
        "/api/account/password",
        Some(&serde_json::json!({ "currentPassword": current, "newPassword": new })),
        csrf,
        Some(&new_idempotency_key()),
    )
    .await
}
