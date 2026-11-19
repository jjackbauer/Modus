//! Serialization-stable control-plane view models shared by the server and the Leptos client.
//!
//! These types mirror the versioned adapter contract (`control-api/v1`). The adapter and the Rust
//! contract tests are the source of truth; a field that is absent from the adapter must never be
//! invented by the client.

use std::collections::BTreeMap;

use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Page<T> {
    pub items: Vec<T>,
    pub limit: u32,
    pub offset: u32,
    pub total: u32,
    pub has_more: bool,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ApiError {
    pub error: ApiErrorBody,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ApiErrorBody {
    pub code: String,
    pub message: String,
    pub category: String,
    pub retryable: bool,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Capabilities {
    pub work_intake: bool,
    pub contract_approval: bool,
    pub contract_rejection: bool,
    pub dispatch: bool,
    pub merge_approval: bool,
    pub serve_supervision: bool,
    pub templates: bool,
    pub agent_profiles: bool,
    pub analytics: bool,
    pub audit: bool,
    pub cancellation: bool,
    pub pause_resume: bool,
    pub scheduling: bool,
    pub retry: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PolicyVersions {
    pub mandatory_policy: String,
    pub guidelines: String,
    pub profile: String,
    pub runner: String,
    pub review_checks: String,
    pub architecture_policy: String,
    pub security_policy: String,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Meta {
    pub api_version: String,
    pub adapter_version: String,
    pub protocol_version: String,
    pub factory_instance_id: String,
    pub implementation_runner: String,
    pub execution_enabled: bool,
    pub capabilities: Capabilities,
    pub policy: PolicyVersions,
    pub server_time_utc: DateTime<Utc>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Health {
    pub healthy: bool,
    pub store_readable: bool,
    pub factory_instance_id: String,
    pub work_item_count: u32,
    pub event_count: u32,
    pub latest_event_sequence: i64,
    pub dispatch_running: bool,
    pub implementation_runner: String,
    pub torn_record_count: u32,
    pub checked_at_utc: DateTime<Utc>,
    pub detail: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Factory {
    pub instance_id: String,
    pub label: String,
    pub control_root: String,
    pub target_repository: String,
    pub target_branch: String,
    pub supported_class: String,
    pub implementation_runner: String,
    pub execution_enabled: bool,
    pub capabilities: Capabilities,
    pub policy: PolicyVersions,
    pub observed_at_utc: DateTime<Utc>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Planning {
    pub priority: Option<String>,
    pub deadline_utc: Option<String>,
    pub tags: Vec<String>,
    pub links: Vec<String>,
    pub notes: Option<String>,
    pub updated_by: Option<String>,
    pub updated_at_utc: Option<String>,
    pub authoritative: bool,
}

impl Default for Planning {
    fn default() -> Self {
        Self {
            priority: None,
            deadline_utc: None,
            tags: Vec::new(),
            links: Vec::new(),
            notes: None,
            updated_by: None,
            updated_at_utc: None,
            authoritative: false,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Requirement {
    pub title: String,
    pub supported_class: String,
    pub digest: String,
    pub text: String,
    pub expectations: Vec<String>,
    pub acceptance_contract_reference: Option<String>,
    pub template_reference: Option<String>,
    pub template_parameters_json: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ContractScenario {
    pub id: String,
    pub kind: String,
    pub path: String,
    pub expectation: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Contract {
    pub proposal_digest: Option<String>,
    pub requirement_digest: Option<String>,
    pub executable: bool,
    pub scenarios: Vec<ContractScenario>,
    pub unresolved_questions: Vec<String>,
    pub frozen_digest: Option<String>,
    pub accepted_contract_digest: Option<String>,
    pub approved_at_utc: Option<String>,
    pub is_frozen: bool,
    pub awaiting_approval: bool,
    pub rejected_digest: Option<String>,
    pub rejected_at_utc: Option<String>,
    pub rejection_reason: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FactoryEvent {
    pub sequence: i64,
    pub id: String,
    pub r#type: String,
    pub source: String,
    pub subject: String,
    pub time_utc: DateTime<Utc>,
    pub correlation_id: Option<String>,
    pub causation_id: Option<String>,
    pub work_item_id: Option<String>,
    pub stage: Option<String>,
    pub agent_definition_id: Option<String>,
    pub session_id: Option<String>,
    pub attempt_id: Option<String>,
    pub operation_id: Option<String>,
    pub summary: Option<String>,
    pub data: BTreeMap<String, String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct StageEvidence {
    pub role: String,
    pub session_id: Option<String>,
    pub attempt_id: Option<String>,
    pub artifact_digest: Option<String>,
    pub summary: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Stage {
    pub id: String,
    pub label: String,
    pub state: String,
    pub entered_at_utc: Option<String>,
    pub exited_at_utc: Option<String>,
    pub duration_seconds: Option<i64>,
    pub actor: Option<String>,
    pub attempts: i32,
    pub decision: Option<String>,
    pub failure_category: Option<String>,
    pub input_artifacts: Vec<String>,
    pub output_artifacts: Vec<String>,
    pub event_ids: Vec<String>,
    pub evidence: Vec<StageEvidence>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Attempt {
    pub attempt_id: String,
    pub work_item_id: String,
    pub attempt_number: i32,
    pub session_id: String,
    pub outcome: String,
    pub failure_category: Option<String>,
    pub failure_role: Option<String>,
    pub detail: Option<String>,
    pub worktree_path: Option<String>,
    pub base_sha: Option<String>,
    pub head_sha: Option<String>,
    pub previous_attempt_id: Option<String>,
    pub started_at_utc: DateTime<Utc>,
    pub finished_at_utc: DateTime<Utc>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ReviewExecution {
    pub kind: String,
    pub check_id: String,
    pub executed_by: String,
    pub executed_version: String,
    pub reviewed_sha: String,
    pub policy_version: String,
    pub outcome: String,
    pub passed: bool,
    pub subject_digest: String,
    pub artifact_digest: String,
    pub executed_at_utc: DateTime<Utc>,
    pub detail: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ReviewHistory {
    pub review_id: String,
    pub review_digest: Option<String>,
    pub head_sha: Option<String>,
    pub base_sha: Option<String>,
    pub contract_digest: Option<String>,
    pub evidence_digest: Option<String>,
    pub outcome: String,
    pub reason: Option<String>,
    pub checks_summary: Option<String>,
    pub recorded_at_utc: Option<String>,
    pub executions: Vec<ReviewExecution>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ReleaseVerification {
    pub work_item_id: String,
    pub merge_operation_id: String,
    pub approved_head_sha: String,
    pub base_sha: String,
    pub merged_sha: String,
    pub merged_tree_sha: Option<String>,
    pub contract_digest: String,
    pub build_evidence_digest: String,
    pub regression_evidence_digest: String,
    pub contract_evidence_digest: String,
    pub scenario_summary: String,
    pub verifier_version: String,
    pub outcome: String,
    pub verified: bool,
    pub detail: String,
    pub verified_at_utc: DateTime<Utc>,
    pub record_digest: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Serving {
    pub work_item_id: Option<String>,
    pub merged_sha: Option<String>,
    pub status: Option<String>,
    pub process_id: Option<i32>,
    pub restart_count: Option<i32>,
    pub detail: Option<String>,
    pub recorded_at_utc: Option<DateTime<Utc>>,
    pub active: bool,
    pub url: Option<String>,
    pub job_id: Option<String>,
    pub supervised_until_utc: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Release {
    pub work_item_id: String,
    pub merged_sha: Option<String>,
    pub merged_tree_sha: Option<String>,
    pub approved_head_sha: Option<String>,
    pub base_sha: Option<String>,
    pub contract_digest: Option<String>,
    pub post_merge_detail: Option<String>,
    pub scenarios: Option<String>,
    pub mutation_mode: Option<String>,
    pub operation_id: Option<String>,
    pub verified_at_utc: Option<String>,
    pub verification: Option<ReleaseVerification>,
    pub serving: Serving,
    pub release_recorded: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct NextAction {
    pub action: String,
    pub label: String,
    pub reason: String,
    pub consequence: String,
    pub available: bool,
    pub required_role: String,
    pub endpoint: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Attention {
    pub id: String,
    pub severity: String,
    pub title: String,
    pub what_happened: String,
    pub why_it_matters: String,
    pub safe_action: String,
    pub consequence: String,
    pub work_item_id: Option<String>,
    pub action: Option<String>,
    pub endpoint: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct WorkItemSummary {
    pub work_item_id: String,
    pub state: String,
    pub version: i32,
    pub title: String,
    pub repository: String,
    pub requirement_digest: String,
    pub proposal_digest: Option<String>,
    pub frozen_contract_digest: Option<String>,
    pub review_digest: Option<String>,
    pub last_failure: Option<String>,
    pub event_count: i32,
    pub created_at_utc: Option<String>,
    pub updated_at_utc: Option<String>,
    pub awaiting_contract_approval: bool,
    pub awaiting_merge_approval: bool,
    pub derived_indicator: Option<String>,
    pub planning: Planning,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct WorkItemDetail {
    pub summary: WorkItemSummary,
    pub factory_instance_id: String,
    pub requirement: Option<Requirement>,
    pub planning: Planning,
    pub contract: Contract,
    pub events: Vec<FactoryEvent>,
    pub stages: Vec<Stage>,
    pub attempts: Vec<Attempt>,
    pub reviews: Vec<ReviewHistory>,
    pub releases: Vec<Release>,
    pub release: Option<Release>,
    pub serving: Serving,
    pub attention: Vec<Attention>,
    pub next_actions: Vec<NextAction>,
    pub legal_state_targets: Vec<String>,
    pub execution_health: Option<String>,
    pub active_session_id: Option<String>,
    pub dispatch_active: bool,
    pub event_total: i32,
    pub latest_event_sequence: Option<i64>,
    pub generated_at_utc: DateTime<Utc>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct LineageNode {
    pub id: String,
    pub kind: String,
    pub label: String,
    pub detail: Option<String>,
    pub work_item_id: Option<String>,
    pub parent_id: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GraphEdge {
    pub from: String,
    pub to: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Lineage {
    pub work_item_id: String,
    pub nodes: Vec<LineageNode>,
    pub edges: Vec<GraphEdge>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Preview {
    pub valid: bool,
    pub errors: Vec<String>,
    pub title: Option<String>,
    pub supported_class: Option<String>,
    pub expectations: Vec<String>,
    pub requirement_digest: Option<String>,
    pub contract_digest: Option<String>,
    pub normalized_requirement: Option<String>,
    pub contract_scenarios: Vec<ContractScenario>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PreviewRequest {
    pub requirement: String,
    pub title: Option<String>,
    pub acceptance_contract_json: Option<String>,
}

#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PlanningInput {
    pub priority: Option<String>,
    pub deadline_utc: Option<String>,
    pub tags: Option<Vec<String>>,
    pub links: Option<Vec<String>>,
    pub notes: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SubmitRequest {
    pub requirement: String,
    pub title: Option<String>,
    pub acceptance_contract_json: Option<String>,
    pub planning: Option<PlanningInput>,
    pub explicitly_confirmed: Option<bool>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SubmitResponse {
    pub accepted: bool,
    pub work_item_id: String,
    pub state: String,
    pub requirement_digest: String,
    pub duplicate: bool,
    pub planning: Planning,
    pub next_action: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ContractApproveRequest {
    pub proposal_digest: String,
    pub confirmed: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ContractRejectRequest {
    pub proposal_digest: String,
    pub reason: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MergeApprovalRequest {
    pub review_digest: String,
    pub confirmation: String,
}

#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PlanningUpdateRequest {
    pub priority: Option<String>,
    pub deadline_utc: Option<String>,
    pub tags: Option<Vec<String>>,
    pub links: Option<Vec<String>>,
    pub notes: Option<String>,
    pub expected_version: Option<i32>,
}

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DispatchRequest {
    pub max_passes: Option<i32>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DispatchJob {
    pub job_id: String,
    pub state: String,
    pub stage: Option<String>,
    pub work_item_id: Option<String>,
    pub started_at_utc: DateTime<Utc>,
    pub finished_at_utc: Option<DateTime<Utc>>,
    pub claimed: i32,
    pub dispatched: i32,
    pub blocked: i32,
    pub failed: i32,
    pub passes: i32,
    pub error: Option<String>,
    pub requested_by: Option<String>,
    pub exclusive_lock_held: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DispatchStatus {
    pub running: bool,
    pub current: Option<DispatchJob>,
    pub recent: Vec<DispatchJob>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ServeRequest {
    pub duration_seconds: i32,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ServeJob {
    pub job_id: String,
    pub work_item_id: String,
    pub state: String,
    pub started_at_utc: DateTime<Utc>,
    pub finished_at_utc: Option<DateTime<Utc>>,
    pub process_id: Option<i32>,
    pub url: Option<String>,
    pub restarts: i32,
    pub error: Option<String>,
    pub merged_sha: Option<String>,
    pub requested_by: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ServeStatus {
    pub job: Option<ServeJob>,
    pub serving: Serving,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MutationResult {
    pub ok: bool,
    pub action: String,
    pub subject: String,
    pub detail: Option<String>,
    pub previous_version: Option<i32>,
    pub resulting_version: Option<i32>,
    pub job: Option<DispatchJob>,
    pub serve_job: Option<ServeJob>,
    pub recorded_at_utc: DateTime<Utc>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DiffFile {
    pub path: String,
    pub status: String,
    pub previous_path: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Diff {
    pub work_item_id: String,
    pub base_sha: String,
    pub head_sha: String,
    pub files: Vec<DiffFile>,
    pub patch: String,
    pub truncated: bool,
    pub max_bytes: i32,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FileContent {
    pub path: String,
    pub sha: String,
    pub content: String,
    pub truncated: bool,
    pub max_bytes: i32,
    pub media_type: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Profile {
    pub profile_id: String,
    pub definition_id: String,
    pub version: i32,
    pub parent_version: Option<i32>,
    pub change_reason: String,
    pub role: String,
    pub objective: String,
    pub model_provider: String,
    pub preferred_model: String,
    pub skills: Vec<String>,
    pub tools: Vec<String>,
    pub max_steps: i32,
    pub timeout_seconds: i32,
    pub max_total_tokens: i32,
    pub max_consecutive_tool_failures: i32,
    pub requested_permissions: Vec<String>,
    pub policy_ref: String,
    pub hard_gates: Vec<String>,
    pub evaluation_policy: String,
    pub candidate_rollout: String,
    pub content_digest: String,
    pub published_at_utc: DateTime<Utc>,
    pub active: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ToolInvocation {
    pub tool_name: String,
    pub arguments_digest: String,
    pub arguments_excerpt: String,
    pub failed: bool,
    pub result_excerpt: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AgentSession {
    pub session_id: String,
    pub work_item_id: String,
    pub agent_definition_id: String,
    pub profile_version: i32,
    pub model_id: String,
    pub input_tokens: i32,
    pub output_tokens: i32,
    pub outcome: String,
    pub failure_category: Option<String>,
    pub tool_names: Vec<String>,
    pub summary: String,
    pub recorded_at_utc: DateTime<Utc>,
    pub measurement_source: String,
    pub tokens_measured: bool,
    #[serde(default)]
    pub tool_invocations: Vec<ToolInvocation>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AgentSummary {
    pub definition_id: String,
    pub role: String,
    pub objective: String,
    pub responsibilities: Vec<String>,
    pub permission_ceiling: Vec<String>,
    pub required_capabilities: Vec<String>,
    pub active_profile_id: Option<String>,
    pub active_profile_version: Option<i32>,
    pub active_profile_digest: Option<String>,
    pub published_profile_count: i32,
    pub session_count: i32,
    pub attempt_count: i32,
    pub success_count: i32,
    pub failure_count: i32,
    pub input_tokens: i32,
    pub output_tokens: i32,
    pub tokens_measured: bool,
    pub implemented_by: Vec<String>,
    pub last_activity_utc: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AgentDetail {
    pub summary: AgentSummary,
    pub profiles: Vec<Profile>,
    pub sessions: Vec<AgentSession>,
    pub attempts: Vec<Attempt>,
    pub improvement_events: Vec<FactoryEvent>,
    pub metrics: Vec<Metric>,
    pub constraints: Vec<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ApprovalItem {
    pub id: String,
    pub kind: String,
    pub work_item_id: String,
    pub title: String,
    pub summary: String,
    pub digest: String,
    pub source_revision: Option<String>,
    pub binding_conditions: Vec<String>,
    pub effect_of_approval: Vec<String>,
    pub required_role: String,
    pub approve_action: String,
    pub reject_action: Option<String>,
    pub endpoint: String,
    pub stale: bool,
    pub stale_reason: Option<String>,
    pub created_at_utc: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ApprovalInbox {
    pub items: Vec<ApprovalItem>,
    pub contract_count: i32,
    pub merge_count: i32,
    pub template_count: i32,
    pub other_count: i32,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct WorkReview {
    pub work_item_id: String,
    pub title: String,
    pub state: String,
    pub verdict: String,
    pub review_digest: Option<String>,
    pub reviewed_sha: Option<String>,
    pub contract_digest: Option<String>,
    pub recorded_at_utc: Option<String>,
    pub executions: Vec<ReviewExecution>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Template {
    pub template_id: String,
    pub template_version: i32,
    pub schema_version: i32,
    pub supported_class: String,
    pub manifest_digest: String,
    pub parameters: Vec<String>,
    pub scenarios_json: String,
    pub approved: bool,
    pub revoked: bool,
    pub approved_at_utc: Option<String>,
    pub approved_by: Option<String>,
    pub error: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ReleaseListItem {
    pub work_item_id: String,
    pub title: String,
    pub state: String,
    pub merged_sha: Option<String>,
    pub contract_digest: Option<String>,
    pub verified_at_utc: Option<String>,
    pub verified: bool,
    pub serving_status: String,
    pub serving_url: Option<String>,
    pub merged_at_utc: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ReleaseList {
    pub items: Vec<ReleaseListItem>,
    pub total: i32,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AnalyticsWindow {
    pub from_utc: String,
    pub to_utc: String,
    pub work_items_created: i32,
    pub work_items_released: i32,
    pub work_items_failed: i32,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MetricDefinition {
    pub key: String,
    pub label: String,
    pub definition: String,
    pub unit: String,
    pub notes: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Metric {
    pub key: String,
    pub label: String,
    pub unit: String,
    pub status: String,
    pub value: Option<f64>,
    pub basis: String,
    pub detail: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SeriesPoint {
    pub label: String,
    pub value: f64,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Analytics {
    pub window: AnalyticsWindow,
    pub metrics: Vec<Metric>,
    pub throughput_by_day: Vec<SeriesPoint>,
    pub lead_time_by_state: Vec<SeriesPoint>,
    pub failure_categories: Vec<SeriesPoint>,
    pub token_usage_by_model: Vec<SeriesPoint>,
    pub stage_durations: Vec<SeriesPoint>,
    pub definitions: Vec<MetricDefinition>,
    pub not_measured: Vec<String>,
    pub generated_at_utc: DateTime<Utc>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PipelineItem {
    pub work_item_id: String,
    pub title: String,
    pub state: String,
    pub stage: String,
    pub stage_label: String,
    pub entered_stage_at_utc: String,
    pub waiting_seconds: i64,
    pub last_failure: Option<String>,
    pub needs_attention: bool,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PipelineStage {
    pub id: String,
    pub label: String,
    pub queue_depth: i32,
    pub in_flight: i32,
    pub waiting_for_authorization: i32,
    pub failed: i32,
    pub average_processing_seconds: Option<f64>,
    pub average_waiting_seconds: Option<f64>,
    pub completed_in_window: i32,
    pub work_item_ids: Vec<String>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Pipeline {
    pub at_utc: Option<String>,
    pub is_replay: bool,
    pub generated_at_utc: DateTime<Utc>,
    pub stages: Vec<PipelineStage>,
    pub items: Vec<PipelineItem>,
    pub legal_edges: Vec<GraphEdge>,
    pub playback_note: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Setting {
    pub key: String,
    pub value: String,
    pub source: String,
    pub mutable: bool,
    pub note: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Settings {
    pub factory_instance_id: String,
    pub control_root: String,
    pub target_repository: String,
    pub target_branch: String,
    pub supported_class: String,
    pub implementation_runner: String,
    pub execution_enabled: bool,
    pub model_credentials_configured: bool,
    pub model_provider: String,
    pub policy: PolicyVersions,
    pub capabilities: Capabilities,
    pub settings: Vec<Setting>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AuditEntry {
    pub audit_id: String,
    pub timestamp_utc: String,
    pub actor: String,
    pub role: String,
    pub action: String,
    pub subject: String,
    pub result: String,
    pub detail: Option<String>,
    pub previous_version: Option<String>,
    pub resulting_version: Option<String>,
    pub correlation_id: Option<String>,
    pub operation_id: Option<String>,
    pub idempotency_key: Option<String>,
    pub remote_address: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct EventStreamEnvelope {
    pub sequence: i64,
    pub factory_event_id: String,
    pub factory_instance_id: String,
    pub work_item_id: Option<String>,
    pub agent_definition_id: Option<String>,
    pub session_id: Option<String>,
    pub attempt_id: Option<String>,
    pub operation_id: Option<String>,
    pub correlation_id: Option<String>,
    pub causation_id: Option<String>,
    pub r#type: String,
    pub stage: Option<String>,
    pub time_utc: DateTime<Utc>,
    pub summary: Option<String>,
    pub data: BTreeMap<String, String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct StreamResync {
    pub reason: String,
    pub latest_sequence: i64,
    pub at_utc: DateTime<Utc>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Heartbeat {
    pub at_utc: DateTime<Utc>,
    pub latest_sequence: i64,
    pub healthy: bool,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct InstanceSummary {
    pub id: String,
    pub label: String,
    pub adapter_url: String,
    pub target_repository: Option<String>,
    pub healthy: bool,
    pub detail: Option<String>,
    pub implementation_runner: Option<String>,
    pub execution_enabled: Option<bool>,
    pub capabilities: Option<Capabilities>,
    pub checked_at_utc: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct UserSummary {
    pub username: String,
    pub display_name: String,
    pub role: String,
    pub active: bool,
    pub created_at: String,
    pub updated_at: String,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SessionInfo {
    pub username: String,
    pub display_name: String,
    pub role: String,
    pub csrf_token: String,
    pub expires_at_utc: String,
    pub instances: Vec<InstanceSummary>,
    pub server_time_utc: String,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TokenSummary {
    pub measured: bool,
    pub input_tokens: i64,
    pub output_tokens: i64,
    pub models: Vec<SeriesPoint>,
    pub basis: String,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Overview {
    pub meta: Option<Meta>,
    pub health: Option<Health>,
    pub factory: Option<Factory>,
    pub dispatch: Option<DispatchStatus>,
    pub approvals: Option<ApprovalInbox>,
    pub releases: Option<ReleaseList>,
    pub agents: Vec<AgentSummary>,
    pub work_counts: BTreeMap<String, i32>,
    pub total_work_items: i32,
    pub failed_work_items: i32,
    pub recent_events: Vec<EventStreamEnvelope>,
    pub attention: Vec<Attention>,
    pub tokens: TokenSummary,
    pub errors: Vec<String>,
    pub stale: bool,
    pub generated_at_utc: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct LoginRequest {
    pub username: String,
    pub password: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CreateUserRequest {
    pub username: String,
    pub display_name: String,
    pub password: String,
    pub role: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct UpdateUserRequest {
    pub role: String,
    pub active: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ResetPasswordRequest {
    pub password: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ChangePasswordRequest {
    pub current_password: String,
    pub new_password: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RegisterInstanceRequest {
    pub id: String,
    pub label: String,
    pub adapter_url: String,
    pub token: String,
    pub target_repository: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ProfileDraftRequest {
    pub version: i32,
    pub change_reason: String,
    pub preferred_model: Option<String>,
    pub max_steps: Option<i32>,
    pub timeout_seconds: Option<i32>,
    pub max_total_tokens: Option<i32>,
    pub max_consecutive_tool_failures: Option<i32>,
    pub tools: Option<Vec<String>>,
    pub requested_permissions: Option<Vec<String>>,
}

/// A work-state value as published by the factory. Kept as a string so a future factory state is
/// visible instead of silently rejected by an older control-plane build.
pub type WorkState = String;

/// Returns true when the state is one of the states declared by the factory alpha state machine.
pub fn is_known_state(state: &str) -> bool {
    matches!(
        state,
        "Proposed"
            | "RequirementsAligned"
            | "ArchitectureRefined"
            | "ReadyForImplementation"
            | "InImplementation"
            | "InReview"
            | "ReadyForDelivery"
            | "DeliveryValidation"
            | "ReleaseCandidate"
            | "Released"
            | "OperationallyValidated"
            | "Blocked"
            | "NeedsRework"
            | "Failed"
            | "Cancelled"
            | "Closed"
    )
}
