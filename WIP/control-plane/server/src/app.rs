use std::sync::Arc;

use anyhow::Result;
use axum::extract::{DefaultBodyLimit, Request, State};
use axum::http::{HeaderMap, HeaderValue, Method, StatusCode, header};
use axum::middleware::{self, Next};
use axum::response::{IntoResponse, Redirect, Response};
use axum::routing::{get, post};
use axum::{Extension, Form, Router};
use chrono::Utc;
use leptos::prelude::*;
use leptos_axum::{LeptosRoutes, generate_route_list};
use serde::Deserialize;
use tower_http::services::ServeDir;

use control_plane_domain::escape_html;
use control_plane_web::App as LeptosApp;

use crate::api;
use crate::audit::NewAuditEntry;
use crate::auth::{AuthUser, PREAUTH_COOKIE, SESSION_COOKIE, cookie_value};
use crate::state::AppState;

pub fn build(state: AppState, routes: Vec<leptos_axum::AxumRouteListing>) -> Router {
    let static_dir = state.config.static_dir.clone();
    let pkg_dir = static_dir.join("pkg");

    let app = Router::new()
        .route("/", get(root))
        .route("/login", get(login_page))
        .route("/api/auth/login", post(login_submit))
        .route("/api/logout", post(logout))
        .route("/bootstrap.js", get(bootstrap_js))
        .route("/assets/app.css", get(app_css))
        .route("/docs", get(docs))
        .route("/favicon.svg", get(favicon))
        .nest_service("/pkg", ServeDir::new(pkg_dir))
        .nest("/api", api::router())
        .leptos_routes(&state, routes, {
            let options = state.leptos_options.clone();
            move || shell(options.clone())
        })
        .fallback(not_found)
        .layer(DefaultBodyLimit::max(2 * 1024 * 1024))
        .with_state(state.clone())
        .layer(middleware::from_fn_with_state(
            state.clone(),
            security_headers,
        ))
        .layer(middleware::from_fn_with_state(state.clone(), csrf_guard))
        .layer(middleware::from_fn_with_state(state, require_auth));

    app
}

fn shell(options: LeptosOptions) -> impl IntoView {
    view! {
        <html lang="en" data-theme="dark">
            <head>
                <meta charset="utf-8"/>
                <meta name="viewport" content="width=device-width, initial-scale=1"/>
                <meta name="color-scheme" content="dark light"/>
                <title>"Wip.Factory Control Plane"</title>
                <link rel="stylesheet" href="/assets/app.css"/>
                <link rel="icon" href="/favicon.svg" type="image/svg+xml"/>
                <script type="module" src="/bootstrap.js"></script>
            </head>
            <body>
                <LeptosApp/>
            </body>
        </html>
    }
}

async fn root(State(state): State<AppState>) -> Response {
    let instances = state.instances.all().await;
    match instances.first() {
        Some(instance) => Redirect::to(&format!("/i/{}/overview", instance.id)).into_response(),
        None => (
            StatusCode::SERVICE_UNAVAILABLE,
            "No factory instance is configured.",
        )
            .into_response(),
    }
}

async fn not_found() -> Response {
    (
        StatusCode::NOT_FOUND,
        [(header::CONTENT_TYPE, "text/html; charset=utf-8")],
        r#"<!doctype html>
<html lang="en" data-theme="dark">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="color-scheme" content="dark light">
<title>Not found · Wip.Factory Control Plane</title>
<script>
  try { if (localStorage.getItem('cp_theme') === 'light') document.documentElement.dataset.theme = 'light'; } catch (_) {}
</script>
<link rel="stylesheet" href="/assets/app.css">
</head>
<body>
<main class="content">
  <h1>Not found</h1>
  <p>This control-plane route does not exist.</p>
  <a class="btn" href="/">Back to the factory</a>
</main>
</body>
</html>"#,
    )
        .into_response()
}

async fn bootstrap_js() -> Response {
    static_asset(
        "text/javascript; charset=utf-8",
        include_str!("../static/bootstrap.js"),
        "no-cache",
    )
}

async fn app_css() -> Response {
    static_asset(
        "text/css; charset=utf-8",
        include_str!("../static/app.css"),
        "public, max-age=300",
    )
}

async fn favicon() -> Response {
    static_asset(
        "image/svg+xml",
        include_str!("../static/favicon.svg"),
        "public, max-age=86400",
    )
}

async fn docs() -> Response {
    static_asset(
        "text/html; charset=utf-8",
        include_str!("../static/docs.html"),
        "no-cache",
    )
}

fn static_asset(content_type: &'static str, body: &'static str, cache: &'static str) -> Response {
    (
        StatusCode::OK,
        [
            (header::CONTENT_TYPE, HeaderValue::from_static(content_type)),
            (header::CACHE_CONTROL, HeaderValue::from_static(cache)),
        ],
        body,
    )
        .into_response()
}

// ---------------------------------------------------------------------- auth middleware

async fn require_auth(State(state): State<AppState>, mut request: Request, next: Next) -> Response {
    let path = request.uri().path().to_string();
    if is_public_path(&path) {
        return next.run(request).await;
    }

    let session_id = cookie_value(request.headers(), SESSION_COOKIE);
    let user = session_id
        .as_deref()
        .and_then(|session| state.auth.authenticate(Some(session)));

    let Some(user) = user else {
        if path.starts_with("/api/") {
            return (
                StatusCode::UNAUTHORIZED,
                axum::Json(serde_json::json!({
                    "error": {
                        "code": "authentication-required",
                        "message": "Sign in to use the control plane.",
                        "category": "unauthenticated",
                        "retryable": false
                    }
                })),
            )
                .into_response();
        }

        let next = percent_encode_path(&path);
        return Redirect::to(&format!("/login?next={next}")).into_response();
    };

    request.extensions_mut().insert(user);
    next.run(request).await
}

async fn csrf_guard(State(state): State<AppState>, request: Request, next: Next) -> Response {
    let path = request.uri().path().to_string();
    let method = request.method().clone();

    if !is_mutation(&method) || !path.starts_with("/api/") || path == "/api/auth/login" {
        return next.run(request).await;
    }

    let Some(user) = request.extensions().get::<AuthUser>().cloned() else {
        return ApiCsrfRejection::new("authentication-required", "Sign in first.").into_response();
    };

    if !origin_allowed(&state, request.headers()) {
        return ApiCsrfRejection::new("origin-rejected", "The request origin is not trusted.")
            .into_response();
    }

    let header = request
        .headers()
        .get("x-csrf-token")
        .and_then(|value| value.to_str().ok())
        .unwrap_or_default();
    if !crate::auth::constant_time_eq(header, &user.csrf) {
        return ApiCsrfRejection::new(
            "csrf-token-invalid",
            "The CSRF token is missing or does not match this session.",
        )
        .into_response();
    }

    next.run(request).await
}

struct ApiCsrfRejection {
    code: &'static str,
    message: &'static str,
}

impl ApiCsrfRejection {
    fn new(code: &'static str, message: &'static str) -> Self {
        Self { code, message }
    }
}

impl IntoResponse for ApiCsrfRejection {
    fn into_response(self) -> Response {
        (
            StatusCode::FORBIDDEN,
            axum::Json(serde_json::json!({
                "error": {
                    "code": self.code,
                    "message": self.message,
                    "category": "forbidden",
                    "retryable": false
                }
            })),
        )
            .into_response()
    }
}

fn is_public_path(path: &str) -> bool {
    path == "/login"
        || path == "/api/auth/login"
        || path == "/api/live"
        || path == "/bootstrap.js"
        || path == "/assets/app.css"
        || path == "/favicon.svg"
        || path.starts_with("/pkg/")
}

fn is_mutation(method: &Method) -> bool {
    matches!(
        *method,
        Method::POST | Method::PUT | Method::PATCH | Method::DELETE
    )
}

fn origin_allowed(state: &AppState, headers: &HeaderMap) -> bool {
    let fetch_site = headers
        .get("sec-fetch-site")
        .and_then(|value| value.to_str().ok());
    if fetch_site.is_some_and(|value| value.eq_ignore_ascii_case("cross-site")) {
        tracing::warn!(origin = ?headers.get(header::ORIGIN), "rejected cross-site mutation");
        return false;
    }

    // Browsers compute Sec-Fetch-Site; a proxy may rewrite Host without changing what the
    // browser considers same-origin. Trust the browser's same-origin assertion, then fall back
    // to the explicit Origin/Host comparison for clients that do not send it.
    if fetch_site.is_some_and(|value| {
        value.eq_ignore_ascii_case("same-origin") || value.eq_ignore_ascii_case("none")
    }) {
        return true;
    }

    let Some(origin) = headers
        .get(header::ORIGIN)
        .and_then(|value| value.to_str().ok())
    else {
        return true;
    };
    let origin = origin.trim_end_matches('/');

    if let Some(configured) = &state.config.public_origin {
        if configured
            .split(',')
            .map(str::trim)
            .filter(|candidate| !candidate.is_empty())
            .any(|candidate| origin.eq_ignore_ascii_case(candidate.trim_end_matches('/')))
        {
            return true;
        }
    }

    let Some(host) = headers
        .get(header::HOST)
        .and_then(|value| value.to_str().ok())
    else {
        tracing::warn!(%origin, fetch_site = ?fetch_site, "rejected mutation without Host");
        return false;
    };
    let expected_http = format!("http://{host}");
    let expected_https = format!("https://{host}");
    if origin.eq_ignore_ascii_case(&expected_http) || origin.eq_ignore_ascii_case(&expected_https) {
        return true;
    }

    tracing::warn!(
        %origin,
        %host,
        fetch_site = ?fetch_site,
        "rejected untrusted mutation origin"
    );
    false
}

// ---------------------------------------------------------------------- security headers

async fn security_headers(State(state): State<AppState>, request: Request, next: Next) -> Response {
    let secure = state.config.secure_cookies;
    let path = request.uri().path().to_string();
    let mut response = next.run(request).await;
    let headers = response.headers_mut();
    headers.insert(
        header::CONTENT_SECURITY_POLICY,
        HeaderValue::from_static(
            "default-src 'self'; script-src 'self' 'wasm-unsafe-eval' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; font-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'",
        ),
    );
    headers.insert(header::X_FRAME_OPTIONS, HeaderValue::from_static("DENY"));
    headers.insert(
        header::X_CONTENT_TYPE_OPTIONS,
        HeaderValue::from_static("nosniff"),
    );
    headers.insert(
        header::REFERRER_POLICY,
        HeaderValue::from_static("no-referrer"),
    );
    headers.insert(
        header::HeaderName::from_static("permissions-policy"),
        HeaderValue::from_static("camera=(), microphone=(), geolocation=(), payment=()"),
    );
    if path.starts_with("/api/") {
        headers.insert(
            header::CACHE_CONTROL,
            HeaderValue::from_static("no-store, max-age=0"),
        );
    } else if !path.starts_with("/assets/") && !path.starts_with("/pkg/") {
        headers.insert(
            header::CACHE_CONTROL,
            HeaderValue::from_static("no-store, max-age=0"),
        );
    }
    if secure {
        headers.insert(
            header::STRICT_TRANSPORT_SECURITY,
            HeaderValue::from_static("max-age=31536000; includeSubDomains"),
        );
    }
    response
}

// ---------------------------------------------------------------------- login

#[derive(Debug, Deserialize)]
struct LoginForm {
    username: String,
    password: String,
    next: Option<String>,
    #[serde(rename = "_csrf")]
    csrf: String,
}

async fn login_page(
    State(state): State<AppState>,
    headers: HeaderMap,
    axum::extract::Query(query): axum::extract::Query<LoginQuery>,
) -> Response {
    let existing_session = cookie_value(&headers, SESSION_COOKIE)
        .and_then(|session| state.auth.authenticate(Some(&session)));
    if existing_session.is_some() {
        return Redirect::to("/").into_response();
    }

    let token = crate::auth::random_token(24);
    let error = query.error.as_deref().unwrap_or_default();
    let next = sanitize_next(query.next.as_deref()).unwrap_or_default();
    let next_attr = escape_html(&next);
    let error_html = if error.is_empty() {
        String::new()
    } else {
        format!(
            "<div class=\"login-error\" role=\"alert\">{}</div>",
            escape_html(error)
        )
    };

    let body = format!(
        r#"<!doctype html>
<html lang="en" data-theme="dark">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Sign in · Wip.Factory Control Plane</title>
<link rel="stylesheet" href="/assets/app.css">
<link rel="icon" href="/favicon.svg" type="image/svg+xml">
</head>
<body class="login-body">
<main class="login-shell">
  <section class="login-brand">
    <div class="brand-mark large">WF</div>
    <h1>Wip.Factory Control Plane</h1>
    <p>Operator access to the factory. The execution engine remains authoritative; this application only uses its governed interfaces.</p>
    <dl class="login-facts">
      <div><dt>Local-first</dt><dd>Bound to loopback unless explicitly configured otherwise.</dd></div>
      <div><dt>Governed</dt><dd>Contract and merge approvals bind to exact digests.</dd></div>
      <div><dt>Auditable</dt><dd>Every mutation is recorded with actor and correlation.</dd></div>
    </dl>
  </section>
  <section class="login-card">
    <h2>Sign in</h2>
    <p class="muted">Use an operator or administrator account.</p>
    {error_html}
    <form method="post" action="/api/auth/login" autocomplete="on">
      <input type="hidden" name="_csrf" value="{token}">
      <input type="hidden" name="next" value="{next_attr}">
      <label>Username<input name="username" autocomplete="username" required autofocus></label>
      <label>Password<input name="password" type="password" autocomplete="current-password" required></label>
      <button class="btn btn-primary" type="submit">Sign in</button>
    </form>
    <p class="login-note">The initial administrator password is written to <code>control-plane-data/runtime/bootstrap-admin.txt</code> on first start.</p>
  </section>
</main>
</body>
</html>"#
    );

    (
        StatusCode::OK,
        [
            (
                header::CONTENT_TYPE,
                HeaderValue::from_static("text/html; charset=utf-8"),
            ),
            (
                header::SET_COOKIE,
                HeaderValue::from_str(&state.auth.preauth_cookie(&token)).unwrap(),
            ),
        ],
        body,
    )
        .into_response()
}

#[derive(Debug, Deserialize)]
struct LoginQuery {
    error: Option<String>,
    next: Option<String>,
}

async fn login_submit(
    State(state): State<AppState>,
    headers: HeaderMap,
    Form(form): Form<LoginForm>,
) -> Response {
    let next = sanitize_next(form.next.as_deref()).unwrap_or_else(|| "/".to_string());
    let preauth = cookie_value(&headers, PREAUTH_COOKIE).unwrap_or_default();
    if !crate::auth::constant_time_eq(&preauth, &form.csrf) {
        return redirect_login_error("Your sign-in form expired. Try again.", &next);
    }

    if !origin_allowed(&state, &headers) {
        return (StatusCode::FORBIDDEN, "The request origin is not trusted.").into_response();
    }

    let remote = headers
        .get("x-forwarded-for")
        .and_then(|value| value.to_str().ok())
        .and_then(|value| value.split(',').next())
        .unwrap_or("local")
        .trim()
        .to_string();

    match state
        .auth
        .login(form.username.trim(), &form.password, &remote)
    {
        Ok((session_id, session)) => {
            let _ = state.audit.append(NewAuditEntry {
                actor: session.username.clone(),
                role: "authenticated".to_string(),
                action: "auth.login".to_string(),
                subject: session.username,
                result: "succeeded".to_string(),
                detail: None,
                previous_version: None,
                resulting_version: None,
                correlation_id: None,
                operation_id: None,
                idempotency_key: None,
                remote_address: Some(remote),
            });
            let mut response = (
                StatusCode::SEE_OTHER,
                [(header::LOCATION, HeaderValue::from_str(&next).unwrap())],
                "",
            )
                .into_response();
            response.headers_mut().append(
                header::SET_COOKIE,
                HeaderValue::from_str(&state.auth.session_cookie(&session_id)).unwrap(),
            );
            response.headers_mut().append(
                header::SET_COOKIE,
                HeaderValue::from_str(&state.auth.clear_preauth_cookie()).unwrap(),
            );
            response
        }
        Err(error) => {
            let _ = state.audit.append(NewAuditEntry {
                actor: form.username.trim().to_string(),
                role: "anonymous".to_string(),
                action: "auth.login".to_string(),
                subject: form.username.trim().to_string(),
                result: "refused".to_string(),
                detail: Some(error.to_string()),
                previous_version: None,
                resulting_version: None,
                correlation_id: None,
                operation_id: None,
                idempotency_key: None,
                remote_address: Some(remote),
            });
            redirect_login_error(&error.to_string(), &next)
        }
    }
}

fn redirect_login_error(message: &str, next: &str) -> Response {
    let encoded = percent_encode_path(message);
    let next_encoded = percent_encode_path(next);
    Redirect::to(&format!("/login?error={encoded}&next={next_encoded}")).into_response()
}

async fn logout(
    State(state): State<AppState>,
    Extension(user): Extension<AuthUser>,
    headers: HeaderMap,
) -> Response {
    state.auth.logout(&user.session_id);
    let _ = state.audit.append(NewAuditEntry {
        actor: user.username.clone(),
        role: user.role.as_str().to_string(),
        action: "auth.logout".to_string(),
        subject: user.username,
        result: "succeeded".to_string(),
        detail: None,
        previous_version: None,
        resulting_version: None,
        correlation_id: None,
        operation_id: None,
        idempotency_key: headers
            .get("idempotency-key")
            .and_then(|value| value.to_str().ok())
            .map(str::to_string),
        remote_address: Some("local".to_string()),
    });
    (
        StatusCode::NO_CONTENT,
        [(
            header::SET_COOKIE,
            HeaderValue::from_str(&state.auth.clear_session_cookie()).unwrap(),
        )],
    )
        .into_response()
}

fn sanitize_next(next: Option<&str>) -> Option<String> {
    let next = next?.trim();
    if next.starts_with("/i/") || next == "/" || next == "/login" {
        if next.contains("://") || next.contains("\\") {
            return None;
        }
        Some(next.to_string())
    } else {
        None
    }
}

fn percent_encode_path(value: &str) -> String {
    percent_encoding::utf8_percent_encode(value, percent_encoding::NON_ALPHANUMERIC).to_string()
}
