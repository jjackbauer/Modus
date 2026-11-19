use std::collections::BTreeMap;
use std::path::PathBuf;
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use anyhow::{Context, Result};
use serde::Serialize;
use tokio::sync::RwLock;

use control_plane_domain::{Capabilities, Health, Meta};
use control_plane_factory_client::{AdapterRole, FactoryClient, FactoryClientConfig};

use crate::config::{InstanceConfig, ServerConfig, save_local_instance};

#[derive(Debug, Clone)]
pub struct CachedHealth {
    pub checked_at: Instant,
    pub healthy: bool,
    pub detail: Option<String>,
    pub meta: Option<Meta>,
    pub health: Option<Health>,
}

#[derive(Debug, Clone, Serialize)]
pub struct PublicInstance {
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

pub struct InstanceRegistry {
    config: Arc<ServerConfig>,
    instances: RwLock<BTreeMap<String, InstanceConfig>>,
    health: Mutex<BTreeMap<String, CachedHealth>>,
    data_dir: PathBuf,
}

impl InstanceRegistry {
    pub fn new(config: Arc<ServerConfig>) -> Self {
        let data_dir = config.data_dir.clone();
        Self {
            instances: RwLock::new(config.instances.clone()),
            config,
            health: Mutex::new(BTreeMap::new()),
            data_dir,
        }
    }

    pub async fn get(&self, id: &str) -> Option<InstanceConfig> {
        self.instances.read().await.get(id).cloned()
    }

    pub async fn all(&self) -> Vec<InstanceConfig> {
        self.instances.read().await.values().cloned().collect()
    }

    pub async fn client(
        &self,
        id: &str,
        role: AdapterRole,
        actor: &str,
        correlation_id: Option<String>,
    ) -> Result<FactoryClient, crate::api::ApiFailure> {
        let instance = self.get(id).await.ok_or_else(|| {
            crate::api::ApiFailure::not_found(
                "instance-not-found",
                "No configured factory instance has that id.",
            )
        })?;
        FactoryClient::new(FactoryClientConfig {
            base_url: instance.adapter_url.clone(),
            token: instance.token.clone(),
            role: role.as_str().to_string(),
            actor: actor.to_string(),
            correlation_id,
        })
        .map_err(|error| {
            crate::api::ApiFailure::bad_gateway("factory-client-config", error.to_string())
        })
    }

    pub async fn probe(&self, id: &str) -> Option<PublicInstance> {
        let instance = self.get(id).await?;
        let cached = {
            let health = self.health.lock().unwrap();
            health.get(id).cloned()
        };
        let cached = cached.filter(|cached| cached.checked_at.elapsed() < Duration::from_secs(5));
        let Some(cached) = cached else {
            let client = match self
                .client(id, AdapterRole::Viewer, "control-plane-health", None)
                .await
            {
                Ok(client) => client,
                Err(error) => {
                    return Some(PublicInstance {
                        id: instance.id,
                        label: instance.label,
                        adapter_url: instance.adapter_url,
                        target_repository: instance.target_repository,
                        healthy: false,
                        detail: Some(error.message),
                        implementation_runner: None,
                        execution_enabled: None,
                        capabilities: None,
                        checked_at_utc: Some(chrono::Utc::now().to_rfc3339()),
                    });
                }
            };

            let meta = client.meta().await.ok();
            let health = client.health().await.ok();
            let healthy = health.as_ref().is_some_and(|health| health.healthy);
            let detail = health
                .as_ref()
                .and_then(|health| health.detail.clone())
                .or_else(|| {
                    meta.is_none()
                        .then(|| "adapter metadata unavailable".to_string())
                });
            let mut cache = self.health.lock().unwrap();
            cache.insert(
                id.to_string(),
                CachedHealth {
                    checked_at: Instant::now(),
                    healthy,
                    detail: detail.clone(),
                    meta: meta.clone(),
                    health: health.clone(),
                },
            );
            return Some(PublicInstance {
                id: instance.id,
                label: instance.label,
                adapter_url: instance.adapter_url,
                target_repository: instance.target_repository,
                healthy,
                detail,
                implementation_runner: meta.as_ref().map(|meta| meta.implementation_runner.clone()),
                execution_enabled: meta.as_ref().map(|meta| meta.execution_enabled),
                capabilities: meta.as_ref().map(|meta| meta.capabilities.clone()),
                checked_at_utc: Some(chrono::Utc::now().to_rfc3339()),
            });
        };

        let (healthy, detail, meta, _health, _checked) = (
            cached.healthy,
            cached.detail,
            cached.meta,
            cached.health,
            cached.checked_at,
        );
        Some(PublicInstance {
            id: instance.id,
            label: instance.label,
            adapter_url: instance.adapter_url,
            target_repository: instance.target_repository,
            healthy,
            detail,
            implementation_runner: meta.as_ref().map(|meta| meta.implementation_runner.clone()),
            execution_enabled: meta.as_ref().map(|meta| meta.execution_enabled),
            capabilities: meta.as_ref().map(|meta| meta.capabilities.clone()),
            checked_at_utc: Some(chrono::Utc::now().to_rfc3339()),
        })
    }

    pub async fn list_public(&self) -> Vec<PublicInstance> {
        let ids: Vec<String> = self.instances.read().await.keys().cloned().collect();
        let mut output = Vec::with_capacity(ids.len());
        for id in ids {
            if let Some(instance) = self.probe(&id).await {
                output.push(instance);
            }
        }
        output
    }

    pub async fn add(&self, instance: InstanceConfig) -> Result<PublicInstance> {
        let client = FactoryClient::new(FactoryClientConfig {
            base_url: instance.adapter_url.clone(),
            token: instance.token.clone(),
            role: AdapterRole::Admin.as_str().to_string(),
            actor: "control-plane-registration".to_string(),
            correlation_id: None,
        })
        .map_err(|error| anyhow::anyhow!(error.to_string()))?;

        let meta = client
            .meta()
            .await
            .map_err(|error| anyhow::anyhow!("adapter registration probe failed: {error}"))?;
        let health = client
            .health()
            .await
            .context("adapter health probe failed during registration")?;
        if !health.healthy {
            anyhow::bail!(
                "adapter is reachable but not healthy: {}",
                health.detail.unwrap_or_else(|| "no detail".to_string())
            );
        }

        let _ = meta;
        {
            let mut instances = self.instances.write().await;
            instances.insert(instance.id.clone(), instance.clone());
        }
        save_local_instance(&self.data_dir, &instance)?;
        self.probe(&instance.id)
            .await
            .ok_or_else(|| anyhow::anyhow!("instance disappeared after registration"))
    }

    pub fn config(&self) -> &ServerConfig {
        &self.config
    }
}
