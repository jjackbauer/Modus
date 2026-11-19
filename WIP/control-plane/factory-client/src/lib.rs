//! Typed, versioned client for the Wip.Factory control adapter.
//!
//! Only this crate talks to the adapter. The browser never receives the adapter service credential.

use std::time::Duration;

use control_plane_domain::{
    AgentDetail, AgentSummary, Analytics, ApiError, ApprovalInbox, AuditEntry, Contract,
    ContractApproveRequest, ContractRejectRequest, Diff, DispatchJob, DispatchRequest,
    DispatchStatus, EventStreamEnvelope, Factory, FactoryEvent, FileContent, Health, Heartbeat,
    Lineage, MergeApprovalRequest, Meta, MetricDefinition, MutationResult, Page, Pipeline,
    PlanningUpdateRequest, Preview, PreviewRequest, Profile, Release, ReleaseList, ReviewHistory,
    ServeRequest, ServeStatus, Settings, StreamResync, SubmitRequest, SubmitResponse, Template,
    WorkItemDetail, WorkItemSummary, WorkReview,
};
use futures_util::Stream;
use percent_encoding::{NON_ALPHANUMERIC, utf8_percent_encode};
use reqwest::{Client, Method, Response, StatusCode};
use serde::Serialize;
use serde::de::DeserializeOwned;
use serde_json::Value;
use thiserror::Error;

#[derive(Debug, Clone, Error)]
pub enum ClientError {
    #[error("adapter transport error: {0}")]
    Transport(String),
    #[error("adapter returned {status}: {code}: {message}")]
    Status {
        status: u16,
        code: String,
        message: String,
        category: String,
        retryable: bool,
    },
    #[error("adapter response could not be decoded: {0}")]
    Decode(String),
    #[error("adapter is not configured: {0}")]
    Configuration(String),
}

pub type ApiResult<T> = Result<T, ClientError>;

impl ClientError {
    pub fn status_code(&self) -> Option<u16> {
        match self {
            Self::Status { status, .. } => Some(*status),
            _ => None,
        }
    }

    pub fn code(&self) -> Option<&str> {
        match self {
            Self::Status { code, .. } => Some(code),
            _ => None,
        }
    }

    pub fn category(&self) -> &str {
        match self {
            Self::Status { category, .. } => category,
            Self::Configuration(_) => "configuration",
            Self::Transport(_) => "unavailable",
            Self::Decode(_) => "internal",
        }
    }

    pub fn retryable(&self) -> bool {
        match self {
            Self::Status { retryable, .. } => *retryable,
            Self::Transport(_) => true,
            Self::Decode(_) | Self::Configuration(_) => false,
        }
    }
}

#[derive(Debug, Clone)]
pub struct MutationResponse<T> {
    pub value: T,
    pub replayed: bool,
    pub operation_id: Option<String>,
}

/// A resumable SSE frame from the adapter.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SseFrame {
    pub id: Option<i64>,
    pub event: String,
    pub data: String,
}

#[derive(Debug, Clone)]
pub struct FactoryClientConfig {
    pub base_url: String,
    pub token: String,
    pub role: String,
    pub actor: String,
    pub correlation_id: Option<String>,
}

/// Role supplied by the authenticated Rust session. The adapter enforces it independently.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AdapterRole {
    Viewer,
    Operator,
    Admin,
}

impl AdapterRole {
    pub fn as_str(self) -> &'static str {
        match self {
            Self::Viewer => "viewer",
            Self::Operator => "operator",
            Self::Admin => "admin",
        }
    }
}

#[derive(Clone)]
pub struct FactoryClient {
    http: Client,
    config: FactoryClientConfig,
}

impl FactoryClient {
    pub fn new(config: FactoryClientConfig) -> ApiResult<Self> {
        if config.token.len() < 24 {
            return Err(ClientError::Configuration(
                "the adapter service credential is missing or shorter than 24 characters"
                    .to_string(),
            ));
        }

        let http = Client::builder()
            .timeout(Duration::from_secs(90))
            .pool_idle_timeout(Duration::from_secs(30))
            .build()
            .map_err(|error| ClientError::Configuration(error.to_string()))?;

        Ok(Self { http, config })
    }

    pub fn base_url(&self) -> &str {
        &self.config.base_url
    }

    fn url(&self, path: &str) -> String {
        format!(
            "{}/v1{}",
            self.config.base_url.trim_end_matches('/'),
            if path.starts_with('/') {
                path.to_string()
            } else {
                format!("/{path}")
            }
        )
    }

    fn request(&self, method: Method, path: &str) -> reqwest::RequestBuilder {
        let builder = self
            .http
            .request(method, self.url(path))
            .header("authorization", format!("Bearer {}", self.config.token))
            .header("x-control-plane-role", &self.config.role)
            .header(
                "x-control-plane-actor",
                utf8_percent_encode(&self.config.actor, NON_ALPHANUMERIC).to_string(),
            );
        match self.config.correlation_id.as_deref() {
            Some(correlation) => builder.header("x-correlation-id", correlation),
            None => builder,
        }
    }

    async fn decode<T: DeserializeOwned>(response: Response) -> ApiResult<T> {
        let status = response.status();
        let bytes = response
            .bytes()
            .await
            .map_err(|error| ClientError::Decode(error.to_string()))?;

        if !status.is_success() {
            let body = serde_json::from_slice::<ApiError>(&bytes).ok();
            if let Some(body) = body {
                return Err(ClientError::Status {
                    status: status.as_u16(),
                    code: body.error.code,
                    message: body.error.message,
                    category: body.error.category,
                    retryable: body.error.retryable,
                });
            }

            return Err(ClientError::Status {
                status: status.as_u16(),
                code: "adapter-error".to_string(),
                message: String::from_utf8_lossy(&bytes).chars().take(500).collect(),
                category: if status == StatusCode::NOT_FOUND {
                    "not-found"
                } else {
                    "internal"
                }
                .to_string(),
                retryable: status.is_server_error(),
            });
        }

        serde_json::from_slice::<T>(&bytes).map_err(|error| ClientError::Decode(error.to_string()))
    }

    async fn get<T: DeserializeOwned>(&self, path: &str) -> ApiResult<T> {
        let response = self
            .request(Method::GET, path)
            .send()
            .await
            .map_err(|error| ClientError::Transport(error.to_string()))?;
        Self::decode(response).await
    }

    async fn post<T: DeserializeOwned, B: Serialize + ?Sized>(
        &self,
        path: &str,
        body: &B,
        idempotency_key: Option<String>,
    ) -> ApiResult<MutationResponse<T>> {
        let key = idempotency_key.unwrap_or_else(|| uuid::Uuid::new_v4().to_string());
        let response = self
            .request(Method::POST, path)
            .header("idempotency-key", key)
            .json(body)
            .send()
            .await
            .map_err(|error| ClientError::Transport(error.to_string()))?;
        let replayed = response
            .headers()
            .get("idempotency-replayed")
            .and_then(|value| value.to_str().ok())
            .is_some_and(|value| value.eq_ignore_ascii_case("true"));
        let operation_id = response
            .headers()
            .get("x-operation-id")
            .and_then(|value| value.to_str().ok())
            .map(str::to_string);
        let value = Self::decode(response).await?;
        Ok(MutationResponse {
            value,
            replayed,
            operation_id,
        })
    }

    async fn delete<T: DeserializeOwned>(
        &self,
        path: &str,
        idempotency_key: Option<String>,
    ) -> ApiResult<MutationResponse<T>> {
        let key = idempotency_key.unwrap_or_else(|| uuid::Uuid::new_v4().to_string());
        let response = self
            .request(Method::DELETE, path)
            .header("idempotency-key", key)
            .send()
            .await
            .map_err(|error| ClientError::Transport(error.to_string()))?;
        let replayed = response
            .headers()
            .get("idempotency-replayed")
            .and_then(|value| value.to_str().ok())
            .is_some_and(|value| value.eq_ignore_ascii_case("true"));
        let operation_id = response
            .headers()
            .get("x-operation-id")
            .and_then(|value| value.to_str().ok())
            .map(str::to_string);
        let value = Self::decode(response).await?;
        Ok(MutationResponse {
            value,
            replayed,
            operation_id,
        })
    }

    // ------------------------------------------------------------------ meta

    pub async fn meta(&self) -> ApiResult<Meta> {
        self.get("/meta").await
    }

    pub async fn health(&self) -> ApiResult<Health> {
        self.get("/health").await
    }

    pub async fn factory(&self) -> ApiResult<Factory> {
        self.get("/factory").await
    }

    pub async fn settings(&self) -> ApiResult<Settings> {
        self.get("/settings").await
    }

    // ------------------------------------------------------------------ work

    pub async fn work_items(
        &self,
        state: Option<&str>,
        search: Option<&str>,
        indicator: Option<&str>,
        sort: Option<&str>,
        limit: u32,
        offset: u32,
    ) -> ApiResult<Page<WorkItemSummary>> {
        let mut query = vec![format!("limit={limit}"), format!("offset={offset}")];
        if let Some(value) = state.filter(|value| !value.is_empty()) {
            query.push(format!(
                "state={}",
                utf8_percent_encode(value, NON_ALPHANUMERIC)
            ));
        }
        if let Some(value) = search.filter(|value| !value.is_empty()) {
            query.push(format!(
                "search={}",
                utf8_percent_encode(value, NON_ALPHANUMERIC)
            ));
        }
        if let Some(value) = indicator.filter(|value| !value.is_empty()) {
            query.push(format!(
                "indicator={}",
                utf8_percent_encode(value, NON_ALPHANUMERIC)
            ));
        }
        if let Some(value) = sort.filter(|value| !value.is_empty()) {
            query.push(format!(
                "sort={}",
                utf8_percent_encode(value, NON_ALPHANUMERIC)
            ));
        }
        self.get(&format!("/work-items?{}", query.join("&"))).await
    }

    pub async fn work_item(&self, work_item_id: &str) -> ApiResult<WorkItemDetail> {
        self.get(&format!("/work-items/{}", path_segment(work_item_id)))
            .await
    }

    pub async fn work_events(
        &self,
        work_item_id: &str,
        after: i64,
        limit: u32,
    ) -> ApiResult<Page<FactoryEvent>> {
        self.get(&format!(
            "/work-items/{}/events?after={after}&limit={limit}",
            path_segment(work_item_id)
        ))
        .await
    }

    pub async fn contract(&self, work_item_id: &str) -> ApiResult<Contract> {
        self.get(&format!(
            "/work-items/{}/contract",
            path_segment(work_item_id)
        ))
        .await
    }

    pub async fn lineage(&self, work_item_id: &str) -> ApiResult<Lineage> {
        self.get(&format!(
            "/work-items/{}/lineage",
            path_segment(work_item_id)
        ))
        .await
    }

    pub async fn work_sessions(
        &self,
        work_item_id: &str,
    ) -> ApiResult<Vec<control_plane_domain::AgentSession>> {
        self.get(&format!(
            "/work-items/{}/sessions",
            path_segment(work_item_id)
        ))
        .await
    }

    pub async fn diff(&self, work_item_id: &str) -> ApiResult<Diff> {
        self.get(&format!("/work-items/{}/diff", path_segment(work_item_id)))
            .await
    }

    pub async fn file(&self, work_item_id: &str, path: &str) -> ApiResult<FileContent> {
        let encoded = path
            .split('/')
            .map(|segment| utf8_percent_encode(segment, NON_ALPHANUMERIC).to_string())
            .collect::<Vec<_>>()
            .join("/");
        self.get(&format!(
            "/work-items/{}/files/{}",
            path_segment(work_item_id),
            encoded
        ))
        .await
    }

    pub async fn preview(&self, request: &PreviewRequest) -> ApiResult<Preview> {
        let response = self
            .request(Method::POST, "/requirement/preview")
            .json(request)
            .send()
            .await
            .map_err(|error| ClientError::Transport(error.to_string()))?;
        Self::decode(response).await
    }

    pub async fn submit(
        &self,
        request: &SubmitRequest,
        idempotency_key: Option<String>,
    ) -> ApiResult<MutationResponse<SubmitResponse>> {
        self.post("/work-items", request, idempotency_key).await
    }

    pub async fn approve_contract(
        &self,
        work_item_id: &str,
        request: &ContractApproveRequest,
        idempotency_key: Option<String>,
    ) -> ApiResult<MutationResponse<MutationResult>> {
        self.post(
            &format!(
                "/work-items/{}/contract/approve",
                path_segment(work_item_id)
            ),
            request,
            idempotency_key,
        )
        .await
    }

    pub async fn reject_contract(
        &self,
        work_item_id: &str,
        request: &ContractRejectRequest,
        idempotency_key: Option<String>,
    ) -> ApiResult<MutationResponse<MutationResult>> {
        self.post(
            &format!("/work-items/{}/contract/reject", path_segment(work_item_id)),
            request,
            idempotency_key,
        )
        .await
    }

    pub async fn approve_contract_policy(
        &self,
        work_item_id: &str,
        idempotency_key: Option<String>,
    ) -> ApiResult<MutationResponse<MutationResult>> {
        self.post(
            &format!(
                "/work-items/{}/contract/approve-policy",
                path_segment(work_item_id)
            ),
            &serde_json::json!({}),
            idempotency_key,
        )
        .await
    }

    pub async fn approve_merge(
        &self,
        work_item_id: &str,
        request: &MergeApprovalRequest,
        idempotency_key: Option<String>,
    ) -> ApiResult<MutationResponse<MutationResult>> {
        self.post(
            &format!("/work-items/{}/merge-approvals", path_segment(work_item_id)),
            request,
            idempotency_key,
        )
        .await
    }

    pub async fn update_planning(
        &self,
        work_item_id: &str,
        request: &PlanningUpdateRequest,
        idempotency_key: Option<String>,
    ) -> ApiResult<MutationResponse<Value>> {
        self.post(
            &format!("/work-items/{}/planning", path_segment(work_item_id)),
            request,
            idempotency_key,
        )
        .await
    }

    pub async fn dispatch(
        &self,
        request: DispatchRequest,
        idempotency_key: Option<String>,
    ) -> ApiResult<MutationResponse<MutationResult>> {
        self.post("/dispatch", &request, idempotency_key).await
    }

    pub async fn dispatch_status(&self) -> ApiResult<DispatchStatus> {
        self.get("/dispatch").await
    }

    pub async fn dispatch_job(&self, job_id: &str) -> ApiResult<DispatchJob> {
        self.get(&format!("/jobs/{}", path_segment(job_id))).await
    }

    pub async fn serve_status(&self, work_item_id: &str) -> ApiResult<ServeStatus> {
        self.get(&format!("/work-items/{}/serve", path_segment(work_item_id)))
            .await
    }

    pub async fn start_serve(
        &self,
        work_item_id: &str,
        request: &ServeRequest,
        idempotency_key: Option<String>,
    ) -> ApiResult<MutationResponse<MutationResult>> {
        self.post(
            &format!("/work-items/{}/serve", path_segment(work_item_id)),
            request,
            idempotency_key,
        )
        .await
    }

    pub async fn stop_serve(
        &self,
        work_item_id: &str,
        idempotency_key: Option<String>,
    ) -> ApiResult<MutationResponse<MutationResult>> {
        self.delete(
            &format!("/work-items/{}/serve", path_segment(work_item_id)),
            idempotency_key,
        )
        .await
    }

    // ------------------------------------------------------------------ evidence

    pub async fn approvals(&self) -> ApiResult<ApprovalInbox> {
        self.get("/approvals").await
    }

    pub async fn reviews(&self) -> ApiResult<Vec<WorkReview>> {
        self.get("/reviews").await
    }

    pub async fn review_history(&self, work_item_id: &str) -> ApiResult<Vec<ReviewHistory>> {
        Ok(self.work_item(work_item_id).await?.reviews)
    }

    pub async fn releases(&self) -> ApiResult<ReleaseList> {
        self.get("/releases").await
    }

    pub async fn release(&self, work_item_id: &str) -> ApiResult<Release> {
        self.get(&format!("/releases/{}", path_segment(work_item_id)))
            .await
    }

    pub async fn templates(&self) -> ApiResult<Vec<Template>> {
        self.get("/templates").await
    }

    pub async fn register_template(
        &self,
        manifest_json: &str,
        idempotency_key: Option<String>,
    ) -> ApiResult<MutationResponse<Template>> {
        self.post(
            "/templates",
            &serde_json::json!({ "manifestJson": manifest_json }),
            idempotency_key,
        )
        .await
    }

    pub async fn approve_template(
        &self,
        template_id: &str,
        version: i32,
        manifest_digest: &str,
        confirmed: bool,
        idempotency_key: Option<String>,
    ) -> ApiResult<MutationResponse<Template>> {
        self.post(
            &format!(
                "/templates/{}/{}/approve",
                path_segment(template_id),
                version
            ),
            &serde_json::json!({ "manifestDigest": manifest_digest, "confirmed": confirmed }),
            idempotency_key,
        )
        .await
    }

    pub async fn revoke_template(
        &self,
        template_id: &str,
        version: i32,
        idempotency_key: Option<String>,
    ) -> ApiResult<MutationResponse<Template>> {
        self.post(
            &format!(
                "/templates/{}/{}/revoke",
                path_segment(template_id),
                version
            ),
            &serde_json::json!({}),
            idempotency_key,
        )
        .await
    }

    // ------------------------------------------------------------------ agents

    pub async fn agents(&self) -> ApiResult<Vec<AgentSummary>> {
        self.get("/agents").await
    }

    pub async fn agent(&self, definition_id: &str) -> ApiResult<AgentDetail> {
        self.get(&format!("/agents/{}", path_segment(definition_id)))
            .await
    }

    pub async fn agent_profiles(&self, definition_id: &str) -> ApiResult<Vec<Profile>> {
        self.get(&format!("/agents/{}/profiles", path_segment(definition_id)))
            .await
    }

    pub async fn create_profile(
        &self,
        definition_id: &str,
        body: &Value,
        idempotency_key: Option<String>,
    ) -> ApiResult<MutationResponse<Profile>> {
        self.post(
            &format!("/agents/{}/profiles", path_segment(definition_id)),
            body,
            idempotency_key,
        )
        .await
    }

    pub async fn promote_profile(
        &self,
        definition_id: &str,
        version: i32,
        confirmed: bool,
        idempotency_key: Option<String>,
    ) -> ApiResult<MutationResponse<Profile>> {
        self.post(
            &format!(
                "/agents/{}/profiles/{}/promote",
                path_segment(definition_id),
                version
            ),
            &serde_json::json!({ "version": version, "confirmed": confirmed }),
            idempotency_key,
        )
        .await
    }

    pub async fn rollback_profile(
        &self,
        definition_id: &str,
        idempotency_key: Option<String>,
    ) -> ApiResult<MutationResponse<Profile>> {
        self.post(
            &format!("/agents/{}/rollback", path_segment(definition_id)),
            &serde_json::json!({}),
            idempotency_key,
        )
        .await
    }

    // ------------------------------------------------------------------ analytics

    pub async fn analytics(&self, from: Option<&str>, to: Option<&str>) -> ApiResult<Analytics> {
        let mut query = Vec::new();
        if let Some(value) = from {
            query.push(format!(
                "from={}",
                utf8_percent_encode(value, NON_ALPHANUMERIC)
            ));
        }
        if let Some(value) = to {
            query.push(format!(
                "to={}",
                utf8_percent_encode(value, NON_ALPHANUMERIC)
            ));
        }
        let suffix = if query.is_empty() {
            String::new()
        } else {
            format!("?{}", query.join("&"))
        };
        self.get(&format!("/analytics{suffix}")).await
    }

    pub async fn analytics_definitions(&self) -> ApiResult<Vec<MetricDefinition>> {
        self.get("/analytics/definitions").await
    }

    pub async fn pipeline(&self, at: Option<&str>) -> ApiResult<Pipeline> {
        match at {
            Some(value) => {
                self.get(&format!(
                    "/pipeline?at={}",
                    utf8_percent_encode(value, NON_ALPHANUMERIC)
                ))
                .await
            }
            None => self.get("/pipeline").await,
        }
    }

    pub async fn audit(&self, limit: u32, offset: u32) -> ApiResult<Page<AuditEntry>> {
        self.get(&format!("/audit?limit={limit}&offset={offset}"))
            .await
    }

    pub async fn events(&self, after: i64, limit: u32) -> ApiResult<Page<EventStreamEnvelope>> {
        self.get(&format!("/events?after={after}&limit={limit}"))
            .await
    }

    // ------------------------------------------------------------------ SSE

    /// Opens the adapter SSE stream. The returned stream yields parsed frames. Reconnection is the
    /// caller's responsibility; pass the last durable sequence to resume.
    pub async fn event_stream(
        &self,
        after: Option<i64>,
        last_event_id: Option<i64>,
    ) -> ApiResult<impl Stream<Item = ApiResult<SseFrame>> + Send + 'static> {
        let mut builder = self.request(Method::GET, "/events/stream");
        if let Some(after) = after {
            builder = builder.query(&[("after", after)]);
        }
        if let Some(last) = last_event_id {
            builder = builder.header("last-event-id", last.to_string());
        }
        let response = builder
            .send()
            .await
            .map_err(|error| ClientError::Transport(error.to_string()))?;

        if !response.status().is_success() {
            return Err(Self::error_from_response(response).await);
        }

        let mut bytes = response.bytes_stream();
        let stream = async_stream::stream! {
            let mut buffer = String::new();
            while let Some(chunk) = futures_util::StreamExt::next(&mut bytes).await {
                match chunk {
                    Ok(chunk) => {
                        buffer.push_str(&String::from_utf8_lossy(&chunk));
                        while let Some(index) = buffer.find("\n\n") {
                            let block = buffer[..index].to_string();
                            buffer.drain(..index + 2);
                            if let Some(frame) = parse_frame(&block) {
                                yield Ok(frame);
                            }
                        }
                    }
                    Err(error) => {
                        yield Err(ClientError::Transport(error.to_string()));
                        break;
                    }
                }
            }
        };

        Ok(stream)
    }

    async fn error_from_response(response: Response) -> ClientError {
        let status = response.status();
        let bytes = response.bytes().await.unwrap_or_default();
        match serde_json::from_slice::<ApiError>(&bytes) {
            Ok(body) => ClientError::Status {
                status: status.as_u16(),
                code: body.error.code,
                message: body.error.message,
                category: body.error.category,
                retryable: body.error.retryable,
            },
            Err(_) => ClientError::Status {
                status: status.as_u16(),
                code: "adapter-error".to_string(),
                message: String::from_utf8_lossy(&bytes).chars().take(500).collect(),
                category: "internal".to_string(),
                retryable: status.is_server_error(),
            },
        }
    }

    /// Typed frames for the server's own downstream SSE proxy. The server re-emits the adapter's
    /// durable sequence; it never invents a transition.

    pub fn typed_stream_frame<T: DeserializeOwned>(frame: &SseFrame) -> ApiResult<Option<T>> {
        if frame.event == "heartbeat" || frame.event == "resync" || frame.event == "connected" {
            return Ok(None);
        }

        serde_json::from_str::<T>(&frame.data)
            .map(Some)
            .map_err(|error| ClientError::Decode(error.to_string()))
    }

    pub fn heartbeat(frame: &SseFrame) -> ApiResult<Option<Heartbeat>> {
        if frame.event != "heartbeat" && frame.event != "connected" {
            return Ok(None);
        }
        serde_json::from_str::<Heartbeat>(&frame.data)
            .map(Some)
            .map_err(|error| ClientError::Decode(error.to_string()))
    }

    pub fn resync(frame: &SseFrame) -> ApiResult<Option<StreamResync>> {
        if frame.event != "resync" {
            return Ok(None);
        }
        serde_json::from_str::<StreamResync>(&frame.data)
            .map(Some)
            .map_err(|error| ClientError::Decode(error.to_string()))
    }
}

fn parse_frame(block: &str) -> Option<SseFrame> {
    let mut event = None;
    let mut data = String::new();
    let mut id = None;

    for line in block.lines() {
        if let Some(value) = line.strip_prefix("event:") {
            event = Some(value.trim().to_string());
        } else if let Some(value) = line.strip_prefix("data:") {
            if !data.is_empty() {
                data.push('\n');
            }
            data.push_str(value.trim_start());
        } else if let Some(value) = line.strip_prefix("id:") {
            id = value.trim().parse::<i64>().ok();
        }
    }

    let event = event?;
    Some(SseFrame { id, event, data })
}

fn path_segment(value: &str) -> String {
    utf8_percent_encode(value, NON_ALPHANUMERIC).to_string()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_sse_frame() {
        let frame = parse_frame("id: 12\nevent: factory\ndata: {\"x\":1}").unwrap();
        assert_eq!(frame.id, Some(12));
        assert_eq!(frame.event, "factory");
        assert_eq!(frame.data, "{\"x\":1}");
    }

    #[test]
    fn rejects_unsafe_token_lengths() {
        let config = FactoryClientConfig {
            base_url: "http://127.0.0.1:5099".into(),
            token: "short".into(),
            role: "operator".into(),
            actor: "test".into(),
            correlation_id: None,
        };
        assert!(FactoryClient::new(config).is_err());
    }
}
