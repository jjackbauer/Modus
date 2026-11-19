//! Cross-crate contract and transport tests for the Rust control plane.
//!
//! These tests do not stand in for the .NET adapter integration suite. They verify that the Rust client
//! speaks the versioned HTTP contract and fails closed on adapter errors.

use std::sync::{Arc, Mutex};

use axum::extract::State;
use axum::http::{HeaderMap, StatusCode};
use axum::response::IntoResponse;
use axum::routing::get;
use axum::{Json, Router};
use control_plane_factory_client::{ClientError, FactoryClient, FactoryClientConfig};

#[derive(Clone, Default)]
struct Seen(Arc<Mutex<Vec<HeaderMap>>>);

async fn health(State(seen): State<Seen>, headers: HeaderMap) -> impl IntoResponse {
    seen.0.lock().unwrap().push(headers);
    Json(serde_json::json!({
        "healthy": true,
        "storeReadable": true,
        "factoryInstanceId": "factory-test",
        "workItemCount": 3,
        "eventCount": 42,
        "latestEventSequence": 41,
        "dispatchRunning": false,
        "implementationRunner": "none",
        "executionEnabled": false,
        "tornRecordCount": 0,
        "checkedAtUtc": "2026-11-19T12:00:00Z",
        "detail": null
    }))
}

async fn forbidden() -> impl IntoResponse {
    (
        StatusCode::FORBIDDEN,
        Json(serde_json::json!({
            "error": {
                "category": "forbidden",
                "code": "read-only-role",
                "message": "viewer may not mutate",
                "retryable": false
            }
        })),
    )
}

async fn client_for(addr: std::net::SocketAddr) -> FactoryClient {
    FactoryClient::new(FactoryClientConfig {
        base_url: format!("http://{addr}"),
        token: "test-service-token-0123456789abcdef".to_string(),
        role: "operator".to_string(),
        actor: "contracttest".to_string(),
        correlation_id: Some("corr-test-1".to_string()),
    })
    .expect("client configuration")
}

#[tokio::test]
async fn client_sends_service_identity_and_parses_health() {
    let seen = Seen::default();
    let app = Router::new()
        .route("/v1/health", get(health))
        .with_state(seen.clone());

    let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
    let addr = listener.local_addr().unwrap();
    tokio::spawn(async move {
        axum::serve(listener, app).await.unwrap();
    });

    let client = client_for(addr).await;
    let health = client.health().await.expect("health response");
    assert!(health.healthy);
    assert_eq!(health.work_item_count, 3);
    assert_eq!(health.latest_event_sequence, 41);

    let headers = seen
        .0
        .lock()
        .unwrap()
        .first()
        .cloned()
        .expect("captured request");
    assert_eq!(
        headers
            .get("authorization")
            .and_then(|value| value.to_str().ok()),
        Some("Bearer test-service-token-0123456789abcdef")
    );
    assert_eq!(
        headers
            .get("x-control-plane-role")
            .and_then(|value| value.to_str().ok()),
        Some("operator")
    );
    assert_eq!(
        headers
            .get("x-control-plane-actor")
            .and_then(|value| value.to_str().ok()),
        Some("contracttest")
    );
    assert_eq!(
        headers
            .get("x-correlation-id")
            .and_then(|value| value.to_str().ok()),
        Some("corr-test-1")
    );
}

#[tokio::test]
async fn client_preserves_typed_adapter_errors() {
    let app = Router::new().route("/v1/factory", get(forbidden));
    let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
    let addr = listener.local_addr().unwrap();
    tokio::spawn(async move {
        axum::serve(listener, app).await.unwrap();
    });

    let client = client_for(addr).await;
    let error = client.factory().await.expect_err("forbidden response");
    match error {
        ClientError::Status {
            status,
            code,
            category,
            retryable,
            ..
        } => {
            assert_eq!(status, 403);
            assert_eq!(code, "read-only-role");
            assert_eq!(category, "forbidden");
            assert!(!retryable);
        }
        other => panic!("unexpected error: {other}"),
    }
}
