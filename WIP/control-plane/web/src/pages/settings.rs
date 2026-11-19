use leptos::prelude::*;
use leptos::task::spawn_local;
use leptos_router::hooks::use_params_map;

use control_plane_domain::{AuditEntry, UserSummary, short_digest};

use crate::api;
use crate::components::ui::{
    Chip, EmptyBlock, LoadingBlock, Panel, PanelActions, error_view, time_ago,
};
use crate::context::AppContext;

#[component]
pub fn SettingsPage() -> impl IntoView {
    let context = AppContext::provide();
    let params = use_params_map();
    let instance = Signal::derive(move || params.get().get("instance").unwrap_or_default());
    let refresh = context.refresh;

    let settings = LocalResource::new(move || {
        let instance = instance.get();
        let _ = refresh.get();
        async move {
            if !api::is_client() || instance.is_empty() {
                None
            } else {
                Some(api::settings(&instance).await)
            }
        }
    });
    let templates = LocalResource::new(move || {
        let instance = instance.get();
        let _ = refresh.get();
        async move {
            if !api::is_client() || instance.is_empty() {
                None
            } else {
                Some(api::templates(&instance).await)
            }
        }
    });
    let users = LocalResource::new(move || {
        let _role = context.role();
        let _ = refresh.get();
        async move {
            if !api::is_client() || !context.is_admin() {
                None
            } else {
                Some(api::users().await)
            }
        }
    });
    let audit = LocalResource::new(move || {
        let _role = context.role();
        let _ = refresh.get();
        async move {
            if !api::is_client() || !context.can_write() {
                None
            } else {
                Some(api::server_audit(100, 0).await)
            }
        }
    });
    let adapter_audit = LocalResource::new(move || {
        let instance = instance.get();
        let _role = context.role();
        let _ = refresh.get();
        async move {
            if !api::is_client() || instance.is_empty() || !context.can_write() {
                None
            } else {
                Some(api::adapter_audit(&instance, 100, 0).await)
            }
        }
    });

    view! {
        <section class="page">
            <header class="page-header">
                <div>
                    <h1 class="page-title">Settings & audit</h1>
                    <p class="page-subtitle">
                        "Factory configuration is authoritative and mostly immutable in the alpha. Control-plane instance and identity configuration is separate and server-enforced."
                    </p>
                </div>
                <div class="page-actions">
                    <a class="btn" href="/docs" target="_blank" rel="noopener noreferrer">"Deployment docs"</a>
                    <button class="btn" on:click=move |_| context.touch()>"Refresh"</button>
                </div>
            </header>

            <Suspense fallback=move || view! { <LoadingBlock label="Loading factory configuration…"/> }>
                {move || match settings.get().flatten() {
                    None => view! { <LoadingBlock label="Loading factory configuration…"/> }.into_any(),
                    Some(Err(error)) => view! { {error_view(error)} }.into_any(),
                    Some(Ok(settings)) => view! {
                        <div class="split">
                            <div class="stack">
                                <Panel title="Factory instance".to_string()>
                                    <div class="stack">
                                        <div class="row-between"><span class="muted">"Instance"</span><span class="mono">{settings.factory_instance_id.clone()}</span></div>
                                        <div class="row-between"><span class="muted">"Control root"</span><span class="mono tiny">{settings.control_root.clone()}</span></div>
                                        <div class="row-between"><span class="muted">"Target repository"</span><span class="mono tiny">{settings.target_repository.clone()}</span></div>
                                        <div class="row-between"><span class="muted">"Target branch"</span><span class="mono">{settings.target_branch.clone()}</span></div>
                                        <div class="row-between"><span class="muted">"Supported class"</span><span class="mono">{settings.supported_class.clone()}</span></div>
                                        <div class="row-between"><span class="muted">"Implementation runner"</span><span class="mono">{settings.implementation_runner.clone()}</span></div>
                                        <div class="row-between"><span class="muted">"Execution enabled"</span><span>{if settings.execution_enabled { "yes" } else { "no" }}</span></div>
                                        <div class="row-between"><span class="muted">"Model credentials"</span><span>{if settings.model_credentials_configured { "configured (redacted)" } else { "not configured" }}</span></div>
                                    </div>
                                </Panel>

                                <Panel title="Factory settings".to_string()>
                                    <div class="table-wrap">
                                        <table class="data">
                                            <thead><tr><th>"Setting"</th><th>"Value"</th><th>"Source"</th><th>"Mutable"</th></tr></thead>
                                            <tbody>
                                                {settings.settings.into_iter().map(|setting| view! {
                                                    <tr>
                                                        <td class="mono tiny">{setting.key.clone()}</td>
                                                        <td class="mono tiny">{setting.value.clone()}{setting.note.clone().map(|note| view! { <div class="faint">{note}</div> })}</td>
                                                        <td class="tiny">{setting.source.clone()}</td>
                                                        <td>{if setting.mutable { "yes" } else { "no" }}</td>
                                                    </tr>
                                                }).collect_view()}
                                            </tbody>
                                        </table>
                                    </div>
                                    <p class="tiny faint">"Settings are build-time or startup configuration in the factory alpha. The control plane does not pretend to edit values that would require a factory restart or code change."</p>
                                </Panel>

                                <Panel title="Policy versions".to_string()>
                                    <div class="grid grid-2 tiny">
                                        <div><span class="faint">"Mandatory policy"</span><div class="mono">{settings.policy.mandatory_policy.clone()}</div></div>
                                        <div><span class="faint">"Guidelines"</span><div class="mono">{settings.policy.guidelines.clone()}</div></div>
                                        <div><span class="faint">"Profile"</span><div class="mono">{settings.policy.profile.clone()}</div></div>
                                        <div><span class="faint">"Runner"</span><div class="mono">{settings.policy.runner.clone()}</div></div>
                                        <div><span class="faint">"Review checks"</span><div class="mono">{settings.policy.review_checks.clone()}</div></div>
                                        <div><span class="faint">"Security policy"</span><div class="mono">{settings.policy.security_policy.clone()}</div></div>
                                    </div>
                                </Panel>
                            </div>

                            <div class="stack">
                                <InstancePanel/>

                                <TemplatePanel templates=templates instance=instance/>

                                <AdminUsers users=users/>

                                <AccountPassword/>

                                <Panel title="Control-plane audit".to_string()>
                                    <Suspense fallback=move || view! { <LoadingBlock label="Loading audit history…"/> }>
                                        {move || match audit.get().flatten() {
                                            None => view! { <div class="tiny faint">"Audit history is available to operators and administrators."</div> }.into_any(),
                                            Some(Err(error)) => view! { {error_view(error)} }.into_any(),
                                            Some(Ok(page)) => view! { <AuditTable entries=page.items/> }.into_any(),
                                        }}
                                    </Suspense>
                                </Panel>

                                <Panel title="Adapter operator audit".to_string()>
                                    <Suspense fallback=move || view! { <LoadingBlock label="Loading adapter audit…"/> }>
                                        {move || match adapter_audit.get().flatten() {
                                            None => view! { <div class="tiny faint">"Adapter audit is available to operators and administrators."</div> }.into_any(),
                                            Some(Err(error)) => view! { {error_view(error)} }.into_any(),
                                            Some(Ok(page)) => view! { <AuditTable entries=page.items/> }.into_any(),
                                        }}
                                    </Suspense>
                                </Panel>
                            </div>
                        </div>
                    }.into_any(),
                }}
            </Suspense>
        </section>
    }
}

#[component]
fn InstancePanel() -> impl IntoView {
    let context = AppContext::provide();
    let id = RwSignal::new(String::new());
    let label = RwSignal::new(String::new());
    let url = RwSignal::new("http://127.0.0.1:5099".to_string());
    let token = RwSignal::new(String::new());
    let target = RwSignal::new(String::new());
    let busy = RwSignal::new(false);
    let error = RwSignal::new(None::<api::UiError>);

    view! {
        <PanelActions
            title="Factory instances".to_string()
            actions=view! { <Chip label=format!("{} configured", context.session.get().map(|session| session.instances.len()).unwrap_or(0)) tone="neutral"/> }.into_any()
        >
            <div class="stack">
                {move || context.session.get().map(|session| view! {
                    <div class="stack">
                        {session.instances.into_iter().map(|instance| view! {
                            <div class="row-between instance-row">
                                <div>
                                    <strong>{instance.label.clone()}</strong>
                                    <div class="tiny mono faint">{instance.id.clone()}" · "{instance.adapter_url.clone()}</div>
                                </div>
                                <Chip label=if instance.healthy { "HEALTHY".to_string() } else { "UNREACHABLE".to_string() } tone=if instance.healthy { "success" } else { "danger" }/>
                            </div>
                        }).collect_view()}
                    </div>
                })}
                <p class="tiny faint">"Each instance is a separate adapter bound to one factory control root and target repository. The control plane never claims cross-repository scheduling."</p>
                {move || context.is_admin().then(|| view! {
                    <details class="intake-advanced">
                        <summary>"Register another adapter instance"</summary>
                        <div class="form-grid" style="margin-top:12px">
                            <label class="field">"Id"<input class="input" placeholder="local-two" prop:value=move || id.get() on:input=move |event| id.set(event_target_value(&event))/></label>
                            <label class="field">"Label"<input class="input" prop:value=move || label.get() on:input=move |event| label.set(event_target_value(&event))/></label>
                            <label class="field span-2">"Adapter URL"<input class="input mono" prop:value=move || url.get() on:input=move |event| url.set(event_target_value(&event))/></label>
                            <label class="field span-2">"Adapter service token"<input class="input mono" type="password" autocomplete="off" prop:value=move || token.get() on:input=move |event| token.set(event_target_value(&event))/></label>
                            <label class="field span-2">"Target repository (display only)"<input class="input mono" prop:value=move || target.get() on:input=move |event| target.set(event_target_value(&event))/></label>
                        </div>
                        {move || error.get().map(|error| view! { <div class="form-error">{error.message}</div> })}
                        <button class="btn btn-primary" disabled=move || busy.get() on:click=move |_| {
                            let csrf = context.csrf.get();
                            let request = control_plane_domain::RegisterInstanceRequest {
                                id: id.get(),
                                label: label.get(),
                                adapter_url: url.get(),
                                token: token.get(),
                                target_repository: (!target.get().is_empty()).then(|| target.get()),
                            };
                            busy.set(true);
                            error.set(None);
                            spawn_local(async move {
                                match api::register_instance(&csrf, &request).await {
                                    Ok(_) => {
                                        context.notify_success("Factory instance registered. Reloading to refresh the instance selector.");
                                        busy.set(false);
                                        #[cfg(target_arch = "wasm32")]
                                        if let Some(window) = web_sys::window() {
                                            let _ = window.location().reload();
                                        }
                                    }
                                    Err(issue) => {
                                        busy.set(false);
                                        error.set(Some(issue));
                                    }
                                }
                            });
                        }>{move || if busy.get() { "Registering…" } else { "Register instance" }}</button>
                    </details>
                })}
            </div>
        </PanelActions>
    }
}

#[component]
fn AdminUsers(
    users: LocalResource<Option<Result<Vec<UserSummary>, api::UiError>>>,
) -> impl IntoView {
    let context = AppContext::provide();
    view! {
        <Panel title="Operator identities".to_string()>
            {move || if !context.is_admin() {
                view! { <div class="tiny faint">"Only administrators may inspect or change operator identities."</div> }.into_any()
            } else {
                view! {
                    <Suspense fallback=move || view! { <LoadingBlock label="Loading operator identities…"/> }>
                        {move || match users.get().flatten() {
                            None => view! { <LoadingBlock label="Loading operator identities…"/> }.into_any(),
                            Some(Err(error)) => view! { {error_view(error)} }.into_any(),
                            Some(Ok(users)) => view! { <UserAdminTable users=users/> }.into_any(),
                        }}
                    </Suspense>
                }.into_any()
            }}
        </Panel>
    }
}

#[component]
fn UserAdminTable(users: Vec<UserSummary>) -> impl IntoView {
    let context = AppContext::provide();
    let username = RwSignal::new(String::new());
    let display_name = RwSignal::new(String::new());
    let password = RwSignal::new(String::new());
    let role = RwSignal::new("viewer".to_string());
    let busy = RwSignal::new(false);
    let error = RwSignal::new(None::<api::UiError>);

    view! {
        <div class="stack">
            <div class="table-wrap">
                <table class="data">
                    <thead><tr><th>"User"</th><th>"Role"</th><th>"Active"</th><th>"Updated"</th></tr></thead>
                    <tbody>
                        {users.into_iter().map(|user| {
                            let username = user.username.clone();
                            let role_value = user.role.clone();
                            let active = user.active;
                            view! {
                                <tr>
                                    <td><strong>{user.display_name.clone()}</strong><div class="tiny faint mono">{user.username.clone()}</div></td>
                                    <td>
                                        <select class="select" on:change=move |event| {
                                            let csrf = context.csrf.get();
                                            let request = control_plane_domain::UpdateUserRequest { role: event_target_value(&event), active };
                                            let username = username.clone();
                                            spawn_local(async move {
                                                match api::update_user(&username, &csrf, &request).await {
                                                    Ok(_) => {
                                                        context.notify_success("User role updated.");
                                                        context.touch();
                                                    }
                                                    Err(error) => context.notify_error(error),
                                                }
                                            });
                                        }>
                                            <option value="viewer" selected=role_value == "viewer">"Viewer"</option>
                                            <option value="operator" selected=role_value == "operator">"Operator"</option>
                                            <option value="admin" selected=role_value == "admin">"Administrator"</option>
                                        </select>
                                    </td>
                                    <td>{if active { "yes" } else { "no" }}</td>
                                    <td class="tiny">{time_ago(Some(&user.updated_at))}</td>
                                </tr>
                            }
                        }).collect_view()}
                    </tbody>
                </table>
            </div>
            <details class="intake-advanced">
                <summary>"Create operator account"</summary>
                <div class="form-grid" style="margin-top:12px">
                    <label class="field">"Username"<input class="input" prop:value=move || username.get() on:input=move |event| username.set(event_target_value(&event))/></label>
                    <label class="field">"Display name"<input class="input" prop:value=move || display_name.get() on:input=move |event| display_name.set(event_target_value(&event))/></label>
                    <label class="field">"Temporary password (12+ characters)"<input class="input" type="password" autocomplete="new-password" prop:value=move || password.get() on:input=move |event| password.set(event_target_value(&event))/></label>
                    <label class="field">"Role"<select class="select" on:change=move |event| role.set(event_target_value(&event))><option value="viewer">"Viewer"</option><option value="operator">"Operator"</option><option value="admin">"Administrator"</option></select></label>
                </div>
                {move || error.get().map(|error| view! { <div class="form-error">{error.message}</div> })}
                <button class="btn btn-primary" disabled=move || busy.get() on:click=move |_| {
                    let csrf = context.csrf.get();
                    let request = control_plane_domain::CreateUserRequest {
                        username: username.get(),
                        display_name: display_name.get(),
                        password: password.get(),
                        role: role.get(),
                    };
                    busy.set(true);
                    error.set(None);
                    spawn_local(async move {
                        match api::create_user(&csrf, &request).await {
                            Ok(_) => {
                                busy.set(false);
                                context.notify_success("Operator account created.");
                                context.touch();
                            }
                            Err(issue) => {
                                busy.set(false);
                                error.set(Some(issue));
                            }
                        }
                    });
                }>{move || if busy.get() { "Creating…" } else { "Create account" }}</button>
            </details>
        </div>
    }
}

#[component]
fn AccountPassword() -> impl IntoView {
    let context = AppContext::provide();
    let current = RwSignal::new(String::new());
    let new_password = RwSignal::new(String::new());
    let busy = RwSignal::new(false);
    let message = RwSignal::new(None::<String>);

    view! {
        <Panel title="Your password".to_string()>
            <div class="stack">
                <label class="field">"Current password"<input class="input" type="password" autocomplete="current-password" prop:value=move || current.get() on:input=move |event| current.set(event_target_value(&event))/></label>
                <label class="field">"New password (12+ characters)"<input class="input" type="password" autocomplete="new-password" prop:value=move || new_password.get() on:input=move |event| new_password.set(event_target_value(&event))/></label>
                {move || message.get().map(|message| view! { <div class="tiny muted">{message}</div> })}
                <button class="btn" disabled=move || busy.get() on:click=move |_| {
                    let csrf = context.csrf.get();
                    let current_value = current.get();
                    let new_value = new_password.get();
                    busy.set(true);
                    message.set(None);
                    spawn_local(async move {
                        let result = api::change_password(&csrf, &current_value, &new_value).await;
                        busy.set(false);
                        match result {
                            Ok(_) => {
                                message.set(Some("Password changed. Existing sessions remain valid until they expire.".to_string()));
                                context.notify_success("Password changed.");
                            }
                            Err(error) => message.set(Some(error.message)),
                        }
                    });
                }>{move || if busy.get() { "Changing…" } else { "Change password" }}</button>
            </div>
        </Panel>
    }
}

#[component]
fn AuditTable(entries: Vec<AuditEntry>) -> impl IntoView {
    view! {
        {if entries.is_empty() {
            view! { <EmptyBlock title="No audit records" detail="Every operator mutation is recorded here with actor, subject, result and correlation."/> }.into_any()
        } else {
            view! {
                <div class="table-wrap audit-table">
                    <table class="data">
                        <thead><tr><th>"Time"</th><th>"Actor"</th><th>"Action"</th><th>"Subject"</th><th>"Result"</th><th>"Operation"</th></tr></thead>
                        <tbody>
                            {entries.into_iter().map(|entry| view! {
                                <tr>
                                    <td class="tiny" title=entry.timestamp_utc.clone()>{time_ago(Some(&entry.timestamp_utc))}</td>
                                    <td class="tiny">{entry.actor.clone()}<div class="faint">{entry.role.clone()}</div></td>
                                    <td class="tiny mono">{entry.action.clone()}</td>
                                    <td class="tiny mono">{entry.subject.clone()}</td>
                                    <td><Chip label=entry.result.to_uppercase() tone=if entry.result == "succeeded" { "success" } else { "danger" }/></td>
                                    <td class="tiny mono">{entry.operation_id.clone().unwrap_or_else(|| "—".to_string())}</td>
                                </tr>
                            }).collect_view()}
                        </tbody>
                    </table>
                </div>
            }.into_any()
        }}
    }
}

#[component]
fn TemplatePanel(
    templates: LocalResource<Option<Result<Vec<control_plane_domain::Template>, api::UiError>>>,
    instance: Signal<String>,
) -> impl IntoView {
    let context = AppContext::provide();
    let manifest = RwSignal::new(String::new());
    let busy = RwSignal::new(false);
    let error = RwSignal::new(None::<api::UiError>);

    view! {
        <Panel title="Approved templates".to_string()>
            <div class="stack">
                <Suspense fallback=move || view! { <LoadingBlock label="Loading template registry…"/> }>
                    {move || match templates.get().flatten() {
                        None => view! { <LoadingBlock label="Loading template registry…"/> }.into_any(),
                        Some(Err(error)) => view! { {error_view(error)} }.into_any(),
                        Some(Ok(templates)) => {
                            if templates.is_empty() {
                                view! { <EmptyBlock title="No templates registered" detail="Register an immutable template manifest; approval is a separate exact-digest action."/> }.into_any()
                            } else {
                                view! {
                                    <div class="stack">
                                        {templates.into_iter().map(|template| view! { <TemplateRow template=template instance=instance.get()/> }).collect_view()}
                                    </div>
                                }.into_any()
                            }
                        }
                    }}
                </Suspense>
                {move || context.is_admin().then(|| view! {
                    <details class="intake-advanced">
                        <summary>"Register a template manifest"</summary>
                        <div class="stack" style="margin-top:12px">
                            <label class="field">"Template manifest JSON"
                                <textarea class="textarea code-area" placeholder=r#"{"templateId":"ping-service","templateVersion":1,"schemaVersion":1,"supportedClass":"aspnetcore-http-net10","parameters":{},"scenarios":[...]}"# prop:value=move || manifest.get() on:input=move |event| manifest.set(event_target_value(&event))></textarea>
                            </label>
                            {move || error.get().map(|error| view! { <div class="form-error">{error.message}</div> })}
                            <button class="btn btn-primary" disabled=move || busy.get() on:click=move |_| {
                                let instance = instance.get();
                                let csrf = context.csrf.get();
                                let json = manifest.get();
                                busy.set(true);
                                error.set(None);
                                spawn_local(async move {
                                    let result = api::register_template(&instance, &csrf, &json).await;
                                    busy.set(false);
                                    match result {
                                        Ok(_) => {
                                            context.notify_success("Template registered. It is not approved until its exact digest is authorized.");
                                            context.touch();
                                        }
                                        Err(issue) => error.set(Some(issue)),
                                    }
                                });
                            }>{move || if busy.get() { "Registering…" } else { "Register template" }}</button>
                        </div>
                    </details>
                })}
            </div>
        </Panel>
    }
}

#[component]
fn TemplateRow(template: control_plane_domain::Template, instance: String) -> impl IntoView {
    let context = AppContext::provide();
    let busy = RwSignal::new(false);
    let confirming = RwSignal::new(false);
    let version = template.template_version;
    let template_id = template.template_id.clone();
    let digest = template.manifest_digest.clone();
    let approved = template.approved;
    let revoked = template.revoked;
    let id_for_approve = template_id.clone();
    let digest_for_approve = digest.clone();

    view! {
        <div class="stack" style="gap:4px">
            <div class="row-between">
                <div>
                    <strong class="mono">{template.template_id.clone()}"@"{template.template_version}</strong>
                    <div class="tiny faint">{template.supported_class.clone()}" · "{template.parameters.join(", ")}</div>
                </div>
                <div class="row">
                    <Chip label=if revoked { "REVOKED".to_string() } else if approved { "APPROVED".to_string() } else { "PENDING".to_string() } tone=if revoked { "danger" } else if approved { "success" } else { "waiting" }/>
                    {if !approved && !revoked && context.is_admin() {
                        view! {
                            <button class="btn btn-sm" disabled=move || busy.get() on:click=move |_| {
                                if confirming.get() {
                                    let instance = instance.clone();
                                    let csrf = context.csrf.get();
                                    let id = id_for_approve.clone();
                                    let digest = digest_for_approve.clone();
                                    busy.set(true);
                                    spawn_local(async move {
                                        let result = api::approve_template(&instance, &csrf, &id, version, &digest, true).await;
                                        busy.set(false);
                                        match result {
                                            Ok(_) => {
                                                context.notify_success("Template policy approved at the exact manifest digest.");
                                                context.touch();
                                            }
                                            Err(error) => context.notify_error(error),
                                        }
                                    });
                                } else {
                                    confirming.set(true);
                                }
                            }>{move || if confirming.get() { "Confirm digest" } else { "Approve" }}</button>
                        }.into_any()
                    } else {
                        view! { <span></span> }.into_any()
                    }}
                </div>
            </div>
            <div class="tiny mono faint">{short_digest(Some(&template.manifest_digest))}</div>
            {template.error.map(|error| view! { <div class="tiny form-error">{error}</div> })}
        </div>
    }
}
