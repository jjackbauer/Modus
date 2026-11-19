use std::collections::BTreeMap;
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::Mutex;

use anyhow::{Context, Result};
use argon2::Argon2;
use argon2::password_hash::rand_core::OsRng;
use argon2::password_hash::{PasswordHash, PasswordHasher, PasswordVerifier, SaltString};
use base64::Engine;
use chrono::{DateTime, Duration, Utc};
use rand::RngCore;
use serde::{Deserialize, Serialize};

use crate::config::{ServerConfig, restrict_file};

pub const SESSION_COOKIE: &str = "cp_session";
pub const PREAUTH_COOKIE: &str = "cp_preauth";

#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Role {
    Viewer,
    Operator,
    Admin,
}

impl Role {
    pub fn as_str(self) -> &'static str {
        match self {
            Self::Viewer => "viewer",
            Self::Operator => "operator",
            Self::Admin => "admin",
        }
    }

    pub fn can_write(self) -> bool {
        matches!(self, Self::Operator | Self::Admin)
    }

    pub fn is_admin(self) -> bool {
        self == Self::Admin
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct User {
    pub username: String,
    pub display_name: String,
    pub role: Role,
    pub password_hash: String,
    pub active: bool,
    pub created_at: DateTime<Utc>,
    pub updated_at: DateTime<Utc>,
}

#[derive(Debug, Clone)]
pub struct AuthUser {
    pub username: String,
    pub display_name: String,
    pub role: Role,
    pub csrf: String,
    pub session_id: String,
    pub expires_at: DateTime<Utc>,
}

#[derive(Debug, Clone)]
pub struct Session {
    pub username: String,
    pub csrf: String,
    pub created_at: DateTime<Utc>,
    pub expires_at: DateTime<Utc>,
    pub last_seen: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct UserFile {
    pub users: Vec<User>,
}

#[derive(Debug, Clone, Serialize)]
pub struct PublicUser {
    pub username: String,
    pub display_name: String,
    pub role: Role,
    pub active: bool,
    pub created_at: DateTime<Utc>,
    pub updated_at: DateTime<Utc>,
}

#[derive(Debug, thiserror::Error)]
pub enum AuthError {
    #[error("invalid credentials")]
    InvalidCredentials,
    #[error("account is disabled")]
    Disabled,
    #[error("too many failed attempts; try again later")]
    RateLimited,
    #[error("{0}")]
    Invalid(String),
    #[error("internal authentication error")]
    Internal,
}

pub struct AuthService {
    users_path: PathBuf,
    users: Mutex<BTreeMap<String, User>>,
    sessions: Mutex<BTreeMap<String, Session>>,
    attempts: Mutex<BTreeMap<String, AttemptWindow>>,
    session_hours: u64,
    secure_cookies: bool,
}

#[derive(Debug, Clone)]
struct AttemptWindow {
    failures: u32,
    window_started: DateTime<Utc>,
    locked_until: Option<DateTime<Utc>>,
}

impl AuthService {
    pub fn load(config: &ServerConfig) -> Result<(Self, Option<PathBuf>)> {
        let users_path = config.data_dir.join("users.json");
        let users = if users_path.exists() {
            let text = fs::read_to_string(&users_path)
                .with_context(|| format!("reading user store {}", users_path.display()))?;
            let parsed: UserFile = serde_json::from_str(&text).context("parsing user store")?;
            parsed
                .users
                .into_iter()
                .map(|user| (user.username.clone(), user))
                .collect::<BTreeMap<_, _>>()
        } else {
            BTreeMap::new()
        };

        let mut service = Self {
            users_path,
            users: Mutex::new(users),
            sessions: Mutex::new(BTreeMap::new()),
            attempts: Mutex::new(BTreeMap::new()),
            session_hours: config.session_hours,
            secure_cookies: config.secure_cookies,
        };

        let bootstrap_path = if service.users.lock().unwrap().is_empty() {
            let (password, generated) = match config.bootstrap_admin_password.clone() {
                Some(password) => (password, false),
                None => (random_token(18), true),
            };
            service.create_initial_admin(&password)?;
            let path = if generated {
                let path = config.data_dir.join("bootstrap-admin.txt");
                fs::write(
                    &path,
                    format!(
                        "Wip.Factory Control Plane bootstrap administrator\nusername: admin\npassword: {password}\n\nChange this password after signing in. Delete this file after storing the password securely.\n"
                    ),
                )?;
                restrict_file(&path)?;
                Some(path)
            } else {
                None
            };
            path
        } else {
            None
        };

        Ok((service, bootstrap_path))
    }

    fn create_initial_admin(&mut self, password: &str) -> Result<()> {
        if password.len() < 12 {
            anyhow::bail!("bootstrap administrator password must be at least 12 characters");
        }
        let now = Utc::now();
        let user = User {
            username: "admin".to_string(),
            display_name: "Administrator".to_string(),
            role: Role::Admin,
            password_hash: hash_password(password)?,
            active: true,
            created_at: now,
            updated_at: now,
        };
        self.users
            .lock()
            .unwrap()
            .insert(user.username.clone(), user);
        self.persist()
    }

    pub fn user_count(&self) -> usize {
        self.users.lock().unwrap().len()
    }

    pub fn login(
        &self,
        username: &str,
        password: &str,
        remote: &str,
    ) -> Result<(String, Session), AuthError> {
        let now = Utc::now();
        {
            let mut attempts = self.attempts.lock().unwrap();
            let entry = attempts.entry(remote.to_string()).or_insert(AttemptWindow {
                failures: 0,
                window_started: now,
                locked_until: None,
            });
            if let Some(until) = entry.locked_until {
                if now < until {
                    return Err(AuthError::RateLimited);
                }
                entry.locked_until = None;
                entry.failures = 0;
                entry.window_started = now;
            }
            if now - entry.window_started > Duration::minutes(15) {
                entry.failures = 0;
                entry.window_started = now;
            }
        }

        let user = self.users.lock().unwrap().get(username).cloned();
        let verified = match &user {
            Some(user) if user.active => verify_password(password, &user.password_hash),
            Some(_) => return Err(AuthError::Disabled),
            None => {
                // Keep the failure path close to a real verification cost.
                let _ = verify_password(password, DUMMY_HASH);
                false
            }
        };

        if !verified {
            let mut attempts = self.attempts.lock().unwrap();
            let entry = attempts.entry(remote.to_string()).or_insert(AttemptWindow {
                failures: 0,
                window_started: now,
                locked_until: None,
            });
            entry.failures += 1;
            if entry.failures >= 5 {
                entry.locked_until = Some(now + Duration::minutes(15));
            }
            return Err(AuthError::InvalidCredentials);
        }

        self.attempts.lock().unwrap().remove(remote);
        let session = Session {
            username: username.to_string(),
            csrf: random_token(24),
            created_at: now,
            expires_at: now + Duration::hours(self.session_hours as i64),
            last_seen: now,
        };
        let session_id = random_token(32);
        self.sessions
            .lock()
            .unwrap()
            .insert(session_id.clone(), session.clone());
        Ok((session_id, session))
    }

    pub fn authenticate(&self, session_id: Option<&str>) -> Option<AuthUser> {
        let session_id = session_id?;
        let now = Utc::now();
        let mut sessions = self.sessions.lock().unwrap();
        let session = sessions.get_mut(session_id)?;
        if now >= session.expires_at {
            sessions.remove(session_id);
            return None;
        }
        session.last_seen = now;
        let user = self.users.lock().unwrap().get(&session.username).cloned()?;
        if !user.active {
            sessions.remove(session_id);
            return None;
        }
        Some(AuthUser {
            username: user.username,
            display_name: user.display_name,
            role: user.role,
            csrf: session.csrf.clone(),
            session_id: session_id.to_string(),
            expires_at: session.expires_at,
        })
    }

    pub fn logout(&self, session_id: &str) {
        self.sessions.lock().unwrap().remove(session_id);
    }

    pub fn session_cookie(&self, session_id: &str) -> String {
        let max_age = self.session_hours * 3600;
        let secure = if self.secure_cookies { "; Secure" } else { "" };
        format!(
            "{SESSION_COOKIE}={session_id}; HttpOnly; SameSite=Strict; Path=/; Max-Age={max_age}{secure}"
        )
    }

    pub fn clear_session_cookie(&self) -> String {
        let secure = if self.secure_cookies { "; Secure" } else { "" };
        format!("{SESSION_COOKIE}=; HttpOnly; SameSite=Strict; Path=/; Max-Age=0{secure}")
    }

    pub fn preauth_cookie(&self, token: &str) -> String {
        let secure = if self.secure_cookies { "; Secure" } else { "" };
        format!("{PREAUTH_COOKIE}={token}; HttpOnly; SameSite=Strict; Path=/; Max-Age=600{secure}")
    }

    pub fn clear_preauth_cookie(&self) -> String {
        let secure = if self.secure_cookies { "; Secure" } else { "" };
        format!("{PREAUTH_COOKIE}=; HttpOnly; SameSite=Strict; Path=/; Max-Age=0{secure}")
    }

    pub fn list_users(&self) -> Vec<PublicUser> {
        self.users
            .lock()
            .unwrap()
            .values()
            .map(|user| PublicUser {
                username: user.username.clone(),
                display_name: user.display_name.clone(),
                role: user.role,
                active: user.active,
                created_at: user.created_at,
                updated_at: user.updated_at,
            })
            .collect()
    }

    pub fn create_user(
        &self,
        username: &str,
        display_name: &str,
        password: &str,
        role: Role,
    ) -> Result<PublicUser, AuthError> {
        validate_username(username).map_err(AuthError::Invalid)?;
        if password.len() < 12 {
            return Err(AuthError::Invalid(
                "Password must be at least 12 characters.".to_string(),
            ));
        }
        if display_name.trim().is_empty() {
            return Err(AuthError::Invalid("Display name is required.".to_string()));
        }
        let mut users = self.users.lock().unwrap();
        if users.contains_key(username) {
            return Err(AuthError::Invalid(
                "That username already exists.".to_string(),
            ));
        }
        let now = Utc::now();
        let user = User {
            username: username.to_string(),
            display_name: display_name.trim().to_string(),
            role,
            password_hash: hash_password(password).map_err(|_| AuthError::Internal)?,
            active: true,
            created_at: now,
            updated_at: now,
        };
        users.insert(user.username.clone(), user.clone());
        drop(users);
        self.persist().map_err(|_| AuthError::Internal)?;
        Ok(PublicUser {
            username: user.username,
            display_name: user.display_name,
            role: user.role,
            active: user.active,
            created_at: user.created_at,
            updated_at: user.updated_at,
        })
    }

    pub fn update_role(
        &self,
        username: &str,
        role: Role,
        active: bool,
    ) -> Result<PublicUser, AuthError> {
        let mut users = self.users.lock().unwrap();
        if users.get(username).is_none() {
            return Err(AuthError::Invalid("No such user.".to_string()));
        }
        if role != Role::Admin {
            let remaining = users
                .values()
                .filter(|candidate| {
                    candidate.role == Role::Admin
                        && candidate.active
                        && candidate.username != username
                })
                .count();
            if remaining == 0 {
                return Err(AuthError::Invalid(
                    "At least one active administrator must remain.".to_string(),
                ));
            }
        }
        let user = users.get_mut(username).expect("checked above");
        user.role = role;
        user.active = active;
        user.updated_at = Utc::now();
        let public = PublicUser {
            username: user.username.clone(),
            display_name: user.display_name.clone(),
            role: user.role,
            active: user.active,
            created_at: user.created_at,
            updated_at: user.updated_at,
        };
        drop(users);
        self.persist().map_err(|_| AuthError::Internal)?;
        Ok(public)
    }

    pub fn reset_password(&self, username: &str, password: &str) -> Result<(), AuthError> {
        if password.len() < 12 {
            return Err(AuthError::Invalid(
                "Password must be at least 12 characters.".to_string(),
            ));
        }
        let mut users = self.users.lock().unwrap();
        let user = users
            .get_mut(username)
            .ok_or_else(|| AuthError::Invalid("No such user.".to_string()))?;
        user.password_hash = hash_password(password).map_err(|_| AuthError::Internal)?;
        user.updated_at = Utc::now();
        drop(users);
        self.persist().map_err(|_| AuthError::Internal)?;
        Ok(())
    }

    pub fn change_own_password(
        &self,
        username: &str,
        current_password: &str,
        new_password: &str,
    ) -> Result<(), AuthError> {
        if new_password.len() < 12 {
            return Err(AuthError::Invalid(
                "Password must be at least 12 characters.".to_string(),
            ));
        }
        let mut users = self.users.lock().unwrap();
        let user = users
            .get_mut(username)
            .ok_or_else(|| AuthError::Invalid("No such user.".to_string()))?;
        if !verify_password(current_password, &user.password_hash) {
            return Err(AuthError::Invalid(
                "Current password is incorrect.".to_string(),
            ));
        }
        user.password_hash = hash_password(new_password).map_err(|_| AuthError::Internal)?;
        user.updated_at = Utc::now();
        drop(users);
        self.persist().map_err(|_| AuthError::Internal)?;
        Ok(())
    }

    fn persist(&self) -> Result<()> {
        let file = UserFile {
            users: self.users.lock().unwrap().values().cloned().collect(),
        };
        let text = serde_json::to_string_pretty(&file)?;
        let temporary = self.users_path.with_extension("json.tmp");
        fs::write(&temporary, text)?;
        fs::rename(&temporary, &self.users_path)?;
        restrict_file(&self.users_path)?;
        Ok(())
    }
}

const DUMMY_HASH: &str = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHRzb21lc2FsdA$V0ibosVYy1cD1r0xXHpGZ9T1xM4nqY3J0Vn8v3G8Y8A";

pub fn hash_password(password: &str) -> Result<String> {
    let salt = SaltString::generate(&mut OsRng);
    Argon2::default()
        .hash_password(password.as_bytes(), &salt)
        .map(|hash| hash.to_string())
        .map_err(|error| anyhow::anyhow!("password hashing failed: {error}"))
}

pub fn verify_password(password: &str, encoded: &str) -> bool {
    let Ok(parsed) = PasswordHash::new(encoded) else {
        return false;
    };
    Argon2::default()
        .verify_password(password.as_bytes(), &parsed)
        .is_ok()
}

pub fn random_token(bytes: usize) -> String {
    let mut buffer = vec![0u8; bytes];
    rand::rng().fill_bytes(&mut buffer);
    base64::engine::general_purpose::URL_SAFE_NO_PAD.encode(buffer)
}

pub fn constant_time_eq(left: &str, right: &str) -> bool {
    let a = left.as_bytes();
    let b = right.as_bytes();
    if a.len() != b.len() {
        return false;
    }
    let mut difference = 0u8;
    for (x, y) in a.iter().zip(b) {
        difference |= x ^ y;
    }
    difference == 0
}

pub fn cookie_value(headers: &http::HeaderMap, name: &str) -> Option<String> {
    let header = headers.get(http::header::COOKIE)?.to_str().ok()?;
    header.split(';').find_map(|part| {
        let part = part.trim();
        let (key, value) = part.split_once('=')?;
        (key.trim() == name).then(|| value.trim().to_string())
    })
}

fn validate_username(username: &str) -> Result<(), String> {
    if username.len() < 3 || username.len() > 32 {
        return Err("Username must be 3-32 characters.".to_string());
    }
    if !username
        .chars()
        .all(|character| character.is_ascii_alphanumeric() || matches!(character, '.' | '-' | '_'))
    {
        return Err(
            "Username may contain letters, digits, dot, dash and underscore only.".to_string(),
        );
    }
    Ok(())
}

pub fn load_or_create_secret(path: &Path) -> Result<Vec<u8>> {
    if path.exists() {
        return Ok(fs::read(path)?);
    }
    let mut secret = vec![0u8; 32];
    rand::rng().fill_bytes(&mut secret);
    fs::write(path, &secret)?;
    restrict_file(path)?;
    Ok(secret)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn passwords_hash_and_verify_without_revealing_plaintext() {
        let hash = hash_password("correct horse battery staple").unwrap();
        assert!(hash.starts_with("$argon2"));
        assert!(verify_password("correct horse battery staple", &hash));
        assert!(!verify_password("wrong password", &hash));
    }

    #[test]
    fn cookie_parser_finds_named_cookie() {
        let mut headers = http::HeaderMap::new();
        headers.insert(
            http::header::COOKIE,
            "a=1; cp_session=abc; b=2".parse().unwrap(),
        );
        assert_eq!(cookie_value(&headers, "cp_session").as_deref(), Some("abc"));
    }

    #[test]
    fn constant_time_equality_handles_equal_and_unequal_values() {
        assert!(constant_time_eq("abc", "abc"));
        assert!(!constant_time_eq("abc", "abd"));
        assert!(!constant_time_eq("abc", "ab"));
    }

    #[test]
    fn roles_separate_read_only_from_write_and_admin() {
        assert!(!Role::Viewer.can_write());
        assert!(Role::Operator.can_write());
        assert!(Role::Admin.can_write());
        assert!(!Role::Operator.is_admin());
        assert!(Role::Admin.is_admin());
    }

    #[test]
    fn random_tokens_are_url_safe_and_unique() {
        let first = random_token(24);
        let second = random_token(24);
        assert!(first.len() >= 24);
        assert_ne!(first, second);
        assert!(
            first
                .chars()
                .all(|ch| ch.is_ascii_alphanumeric() || ch == '-' || ch == '_')
        );
    }
}
