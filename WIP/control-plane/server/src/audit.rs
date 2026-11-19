use std::fs::{self, OpenOptions};
use std::io::Write;
use std::path::PathBuf;
use std::sync::Mutex;

use anyhow::Result;
use chrono::Utc;
use serde::{Deserialize, Serialize};

use control_plane_domain::AuditEntry;

use crate::config::restrict_file;

/// Append-only server audit. The factory event log remains authoritative for work; this records the
/// authenticated operator request and the adapter operation that carried it out.
pub struct AuditLog {
    path: PathBuf,
    gate: Mutex<()>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct NewAuditEntry {
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

impl AuditLog {
    pub fn open(data_dir: &std::path::Path) -> Result<Self> {
        fs::create_dir_all(data_dir)?;
        let path = data_dir.join("audit.jsonl");
        if !path.exists() {
            fs::write(&path, b"")?;
            restrict_file(&path)?;
        }
        Ok(Self {
            path,
            gate: Mutex::new(()),
        })
    }

    pub fn append(&self, entry: NewAuditEntry) -> Result<AuditEntry> {
        let now = Utc::now();
        let audit = AuditEntry {
            audit_id: format!("srv-{}", crate::auth::random_token(12)),
            timestamp_utc: now.to_rfc3339(),
            actor: entry.actor,
            role: entry.role,
            action: entry.action,
            subject: entry.subject,
            result: entry.result,
            detail: entry.detail.map(|detail| redact(&detail)),
            previous_version: entry.previous_version,
            resulting_version: entry.resulting_version,
            correlation_id: entry.correlation_id,
            operation_id: entry.operation_id,
            idempotency_key: entry.idempotency_key,
            remote_address: entry.remote_address,
        };

        let line = serde_json::to_string(&audit)?;
        let _guard = self.gate.lock().unwrap();
        let mut file = OpenOptions::new()
            .append(true)
            .create(true)
            .open(&self.path)?;
        writeln!(file, "{line}")?;
        file.flush()?;
        Ok(audit)
    }

    pub fn read(&self, limit: u32, offset: u32) -> Result<(Vec<AuditEntry>, u32)> {
        let text = fs::read_to_string(&self.path).unwrap_or_default();
        let entries: Vec<AuditEntry> = text
            .lines()
            .filter(|line| !line.trim().is_empty())
            .filter_map(|line| serde_json::from_str::<AuditEntry>(line).ok())
            .collect();
        let total = entries.len() as u32;
        let page = entries
            .into_iter()
            .rev()
            .skip(offset as usize)
            .take(limit as usize)
            .collect();
        Ok((page, total))
    }
}

fn redact(value: &str) -> String {
    let mut output = String::with_capacity(value.len());
    for token in value.split_whitespace() {
        let lower = token.to_ascii_lowercase();
        if lower.contains("token=") || lower.contains("authorization:") || lower.contains("bearer")
        {
            output.push_str("[redacted] ");
        } else {
            output.push_str(token);
            output.push(' ');
        }
    }
    let output = output.trim_end();
    output.chars().take(4000).collect()
}
