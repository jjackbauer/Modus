use std::collections::BTreeMap;
use std::fs;
use std::path::{Path, PathBuf};

use anyhow::{Context, Result};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct InstanceFileConfig {
    pub id: String,
    pub label: String,
    pub adapter_url: String,
    #[serde(default)]
    pub token: Option<String>,
    #[serde(default)]
    pub token_env: Option<String>,
    #[serde(default)]
    pub target_repository: Option<String>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ServerFileConfig {
    #[serde(default = "default_bind")]
    pub bind: String,
    #[serde(default = "default_data_dir")]
    pub data_dir: String,
    #[serde(default)]
    pub static_dir: Option<String>,
    #[serde(default)]
    pub secure_cookies: bool,
    #[serde(default = "default_session_hours")]
    pub session_hours: u64,
    #[serde(default)]
    pub instances: Vec<InstanceFileConfig>,
}

fn default_bind() -> String {
    "127.0.0.1:3000".to_string()
}

fn default_data_dir() -> String {
    "control-plane-data/runtime".to_string()
}

fn default_session_hours() -> u64 {
    12
}

impl Default for ServerFileConfig {
    fn default() -> Self {
        Self {
            bind: default_bind(),
            data_dir: default_data_dir(),
            static_dir: None,
            secure_cookies: false,
            session_hours: 12,
            instances: Vec::new(),
        }
    }
}

#[derive(Debug, Clone)]
pub struct InstanceConfig {
    pub id: String,
    pub label: String,
    pub adapter_url: String,
    pub token: String,
    pub target_repository: Option<String>,
}

#[derive(Debug, Clone)]
pub struct ServerConfig {
    pub bind: String,
    pub data_dir: PathBuf,
    pub static_dir: PathBuf,
    pub secure_cookies: bool,
    pub session_hours: u64,
    pub instances: BTreeMap<String, InstanceConfig>,
    pub bootstrap_admin_password: Option<String>,
    pub public_origin: Option<String>,
    pub allow_remote_factory: bool,
}

impl ServerConfig {
    pub fn load() -> Result<Self> {
        let explicit = std::env::var("CONTROL_PLANE_CONFIG")
            .ok()
            .map(PathBuf::from);
        let path = explicit.unwrap_or_else(|| PathBuf::from("control-plane.toml"));
        let mut file_config = if path.exists() {
            let text = fs::read_to_string(&path).with_context(|| {
                format!("reading control-plane configuration {}", path.display())
            })?;
            toml::from_str::<ServerFileConfig>(&text).with_context(|| {
                format!("parsing control-plane configuration {}", path.display())
            })?
        } else {
            ServerFileConfig::default()
        };

        if let Ok(bind) = std::env::var("CONTROL_PLANE_BIND") {
            file_config.bind = bind;
        }
        if let Ok(data) = std::env::var("CONTROL_PLANE_DATA") {
            file_config.data_dir = data;
        }
        if let Ok(static_dir) = std::env::var("CONTROL_PLANE_STATIC") {
            file_config.static_dir = Some(static_dir);
        }
        if let Ok(secure) = std::env::var("CONTROL_PLANE_SECURE_COOKIES") {
            file_config.secure_cookies = matches!(
                secure.trim().to_ascii_lowercase().as_str(),
                "1" | "true" | "yes"
            );
        }
        if let Ok(hours) = std::env::var("CONTROL_PLANE_SESSION_HOURS") {
            if let Ok(parsed) = hours.parse::<u64>() {
                file_config.session_hours = parsed.clamp(1, 168);
            }
        }

        let data_dir = PathBuf::from(&file_config.data_dir);
        fs::create_dir_all(&data_dir)
            .with_context(|| format!("creating data directory {}", data_dir.display()))?;
        restrict_directory(&data_dir)?;

        // Runtime-added instances live separately so an operator-edited base file is not rewritten.
        let local_path = data_dir.join("instances.local.toml");
        let mut instances = BTreeMap::new();
        if let Ok(text) = fs::read_to_string(&local_path) {
            let local = toml::from_str::<LocalInstances>(&text).unwrap_or_default();
            for instance in local.instances {
                let resolved = resolve_instance(instance)?;
                instances.insert(resolved.id.clone(), resolved);
            }
        }

        if file_config.instances.is_empty() {
            if let Some(instance) = from_environment()? {
                instances.insert(instance.id.clone(), instance);
            }
        } else {
            for instance in file_config.instances {
                let resolved = resolve_instance(instance)?;
                instances.insert(resolved.id.clone(), resolved);
            }
        }

        if instances.is_empty() {
            anyhow::bail!(
                "no factory instance configured. Set CONTROL_PLANE_ADAPTER_URL and CONTROL_PLANE_ADAPTER_TOKEN, or add [[instances]] to control-plane.toml."
            );
        }

        let static_dir = file_config
            .static_dir
            .map(PathBuf::from)
            .unwrap_or_else(|| Path::new(env!("CARGO_MANIFEST_DIR")).join("static"));
        if !static_dir.exists() {
            anyhow::bail!("static directory {} does not exist", static_dir.display());
        }

        let allow_remote = std::env::var("CONTROL_PLANE_ALLOW_REMOTE_FACTORY")
            .ok()
            .is_some_and(|value| {
                matches!(
                    value.trim().to_ascii_lowercase().as_str(),
                    "1" | "true" | "yes"
                )
            });
        for instance in instances.values() {
            validate_adapter_url(&instance.adapter_url, allow_remote)?;
        }

        let bootstrap_admin_password = std::env::var("CONTROL_PLANE_ADMIN_PASSWORD")
            .ok()
            .filter(|value| !value.is_empty());
        if let Some(password) = &bootstrap_admin_password {
            if password.len() < 12 {
                anyhow::bail!(
                    "CONTROL_PLANE_ADMIN_PASSWORD must be at least 12 characters for local bootstrap"
                );
            }
        }

        Ok(Self {
            bind: file_config.bind,
            data_dir,
            static_dir,
            secure_cookies: file_config.secure_cookies,
            session_hours: file_config.session_hours,
            instances,
            bootstrap_admin_password,
            public_origin: std::env::var("CONTROL_PLANE_PUBLIC_ORIGIN").ok(),
            allow_remote_factory: allow_remote,
        })
    }

    pub fn local_instances_path(&self) -> PathBuf {
        self.data_dir.join("instances.local.toml")
    }
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
struct LocalInstances {
    #[serde(default)]
    instances: Vec<InstanceFileConfig>,
}

impl LocalInstances {
    fn load(path: &Path) -> Self {
        fs::read_to_string(path)
            .ok()
            .and_then(|text| toml::from_str(&text).ok())
            .unwrap_or_default()
    }

    fn save(&self, path: &Path) -> Result<()> {
        let text = toml::to_string_pretty(self).context("serialising instance registry")?;
        let temporary = path.with_extension("toml.tmp");
        fs::write(&temporary, text)?;
        fs::rename(temporary, path)?;
        Ok(())
    }
}

pub fn save_local_instance(data_dir: &Path, instance: &InstanceConfig) -> Result<()> {
    let path = data_dir.join("instances.local.toml");
    let mut local = LocalInstances::load(&path);
    local
        .instances
        .retain(|candidate| candidate.id != instance.id);
    local.instances.push(InstanceFileConfig {
        id: instance.id.clone(),
        label: instance.label.clone(),
        adapter_url: instance.adapter_url.clone(),
        token: Some(instance.token.clone()),
        token_env: None,
        target_repository: instance.target_repository.clone(),
    });
    local.save(&path)?;
    restrict_file(&path)?;
    Ok(())
}

fn from_environment() -> Result<Option<InstanceConfig>> {
    let url = std::env::var("CONTROL_PLANE_ADAPTER_URL")
        .unwrap_or_else(|_| "http://127.0.0.1:5099".to_string());
    let token = std::env::var("CONTROL_PLANE_ADAPTER_TOKEN").context(
        "CONTROL_PLANE_ADAPTER_TOKEN is required for the default local factory instance",
    )?;
    let label = std::env::var("CONTROL_PLANE_INSTANCE_LABEL")
        .unwrap_or_else(|_| "Local factory".to_string());
    let id = std::env::var("CONTROL_PLANE_INSTANCE_ID").unwrap_or_else(|_| "local".to_string());
    Ok(Some(resolve_instance(InstanceFileConfig {
        id,
        label,
        adapter_url: url,
        token: Some(token),
        token_env: None,
        target_repository: std::env::var("CONTROL_PLANE_TARGET_REPOSITORY").ok(),
    })?))
}

fn resolve_instance(instance: InstanceFileConfig) -> Result<InstanceConfig> {
    if instance.id.len() < 2
        || instance.id.len() > 40
        || !instance.id.chars().all(|character| {
            character.is_ascii_lowercase() || character.is_ascii_digit() || character == '-'
        })
    {
        anyhow::bail!(
            "instance id '{}' must be 2-40 lowercase letters, digits or hyphens",
            instance.id
        );
    }

    let token = match (instance.token, instance.token_env) {
        (Some(token), _) if !token.is_empty() => token,
        (_, Some(variable)) => std::env::var(&variable)
            .with_context(|| format!("adapter token environment variable {variable} is not set"))?,
        _ => anyhow::bail!("instance '{}' has no adapter token", instance.id),
    };

    if token.len() < 24 {
        anyhow::bail!(
            "instance '{}' adapter token must be at least 24 characters",
            instance.id
        );
    }

    let adapter_url = instance.adapter_url.trim_end_matches('/').to_string();
    Ok(InstanceConfig {
        id: instance.id,
        label: instance.label,
        adapter_url,
        token,
        target_repository: instance.target_repository,
    })
}

fn validate_adapter_url(url: &str, allow_remote: bool) -> Result<()> {
    let parsed = url::Url::parse(url).with_context(|| format!("invalid adapter URL '{url}'"))?;
    match parsed.scheme() {
        "http" => {
            let host = parsed.host_str().unwrap_or_default();
            let loopback = host == "127.0.0.1" || host == "localhost" || host == "::1";
            if !loopback && !allow_remote {
                anyhow::bail!(
                    "adapter URL '{url}' is not loopback. Set CONTROL_PLANE_ALLOW_REMOTE_FACTORY=1 and use authenticated transport (prefer https)."
                );
            }
        }
        "https" => {}
        other => anyhow::bail!(
            "adapter URL scheme '{other}' is unsupported; use http on loopback or https"
        ),
    }
    if parsed.path() != "/" && !parsed.path().is_empty() {
        anyhow::bail!("adapter URL must be an origin without a path");
    }
    Ok(())
}

fn restrict_directory(path: &Path) -> Result<()> {
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        fs::set_permissions(path, fs::Permissions::from_mode(0o700))?;
    }
    #[cfg(not(unix))]
    {
        let _ = path;
    }
    Ok(())
}

pub fn restrict_file(path: &Path) -> Result<()> {
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        fs::set_permissions(path, fs::Permissions::from_mode(0o600))?;
    }
    #[cfg(not(unix))]
    {
        let _ = path;
    }
    Ok(())
}
