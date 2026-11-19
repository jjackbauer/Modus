//! Evidence hierarchy primitives: verdict, then check, then exact identity, then raw output.
//!
//! An operator must be able to read the conclusion without losing the ability to audit it. The
//! raw payload stays one disclosure away instead of being the first thing shown or being removed.

use leptos::prelude::*;

use control_plane_domain::DiffFile;

use crate::components::status::StatusIndicator;
use crate::components::ui::copy_text;

/// One executed check behind a verdict.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct EvidenceCheck {
    pub label: String,
    pub outcome: String,
    pub tone: &'static str,
    pub detail: Option<String>,
}

#[component]
pub fn EvidenceSummary(
    /// The conclusion in operator words ("Post-merge verification passed").
    verdict: String,
    tone: &'static str,
    /// Exact identifiers the verdict is bound to, in display order.
    identity: Vec<(String, String)>,
    checks: Vec<EvidenceCheck>,
    #[prop(optional_no_strip)] raw: Option<String>,
) -> impl IntoView {
    let check_count = checks.len();
    view! {
        <div class="evidence-summary">
            <div class="evidence-verdict">
                <span class="label">"Verdict"</span>
                <StatusIndicator label=verdict tone=tone/>
            </div>

            <section class="evidence-block" aria-labelledby="evidence-checks-title">
                <h3 id="evidence-checks-title">
                    {if check_count == 1 { "1 check".to_string() } else { format!("{check_count} checks") }}
                </h3>
                {if checks.is_empty() {
                    view! {
                        <p class="muted small">
                            "No check was executed for this verdict. An empty check list is evidence of an incomplete run, not of success."
                        </p>
                    }
                    .into_any()
                } else {
                    view! {
                        <ul class="evidence-checks">
                            {checks
                                .into_iter()
                                .map(|check| view! {
                                    <li>
                                        <StatusIndicator label=check.outcome tone=check.tone/>
                                        <span class="evidence-check-label">{check.label}</span>
                                        {check.detail.map(|detail| view! {
                                            <span class="evidence-check-detail muted small">{detail}</span>
                                        })}
                                    </li>
                                })
                                .collect_view()}
                        </ul>
                    }
                    .into_any()
                }}
            </section>

            <section class="evidence-block" aria-labelledby="evidence-identity-title">
                <h3 id="evidence-identity-title">"Exact identity"</h3>
                <dl class="metadata-list">
                    {identity
                        .into_iter()
                        .map(|(label, value)| view! {
                            <div>
                                <dt>{label}</dt>
                                <dd><code class="mono wrap">{value}</code></dd>
                            </div>
                        })
                        .collect_view()}
                </dl>
            </section>

            {raw.map(|raw| view! {
                <details class="evidence-raw">
                    <summary>"Raw evidence"</summary>
                    <pre tabindex="0" role="region" aria-label="Raw evidence output">{raw}</pre>
                </details>
            })}
        </div>
    }
}

/// A reviewed diff. The legend explains the marks, the file list is the keyboard route through the
/// change, and the content region is focusable so the arrow keys scroll it instead of the page.
#[component]
pub fn DiffViewer(
    #[prop(into)] base_sha: String,
    #[prop(into)] head_sha: String,
    files: Vec<DiffFile>,
    truncated: bool,
    selected: RwSignal<Option<String>>,
    children: Children,
) -> impl IntoView {
    let first_path = files.first().map(|file| file.path.clone());
    let copied = RwSignal::new(false);
    let first_for_copy = first_path.clone();
    let copy_target = move || {
        selected
            .get()
            .or_else(|| first_for_copy.clone())
            .unwrap_or_default()
    };

    view! {
        <div class="diff-viewer">
            <div class="diff-head">
                <span class="mono tiny">
                    {format!("{} → {}", short(&base_sha), short(&head_sha))}
                </span>
                <ul class="diff-legend" aria-label="Diff legend">
                    <li><span class="diff-swatch diff-swatch-add" aria-hidden="true"></span>"added"</li>
                    <li><span class="diff-swatch diff-swatch-del" aria-hidden="true"></span>"removed"</li>
                    <li><span class="diff-swatch diff-swatch-hunk" aria-hidden="true"></span>"location"</li>
                </ul>
            </div>

            <div class="diff-shell">
                <div class="diff-files" role="group" aria-label="Changed files">
                    {files
                        .into_iter()
                        .map(|file| {
                            let path = file.path.clone();
                            let path_for_active = path.clone();
                            let first = first_path.clone();
                            view! {
                                <div class="diff-file-row">
                                    <button
                                        type="button"
                                        class="diff-file"
                                        aria-pressed=move || {
                                            let active = selected.get().or_else(|| first.clone());
                                            active.as_deref() == Some(path_for_active.as_str())
                                        }
                                        on:click={
                                            let path = path.clone();
                                            move |_| selected.set(Some(path.clone()))
                                        }
                                    >
                                        <span class="diff-file-status" title=file.status.clone()>
                                            {file.status.chars().next().unwrap_or('?')}
                                        </span>
                                        <span class="mono">{path.clone()}</span>
                                    </button>
                                    <button
                                        class="btn btn-quiet btn-xs"
                                        type="button"
                                        aria-label=format!("Copy path {path} to clipboard")
                                        on:click={
                                            let path = path.clone();
                                            move |_| copy_text(&path, copied)
                                        }
                                    >
                                        <span aria-live="polite">
                                            {move || if copied.get() { "Copied" } else { "Copy path" }}
                                        </span>
                                    </button>
                                </div>
                            }
                        })
                        .collect_view()}
                </div>
                <div
                    class="diff-content"
                    tabindex="0"
                    role="region"
                    aria-label=move || {
                        format!(
                            "Reviewed change for {}. Focused, the arrow keys scroll this region.",
                            copy_target()
                        )
                    }
                >
                    {children()}
                </div>
            </div>

            {truncated.then(|| view! {
                <p class="form-error small">
                    "The patch is truncated at the adapter byte bound. Inspect individual changed files; the record is not complete."
                </p>
            })}
            <p class="tiny faint">
                "Only files changed by the reviewed revision are addressable. This is not unrestricted filesystem browsing."
            </p>
        </div>
    }
}

fn short(sha: &str) -> String {
    sha.chars().take(10).collect()
}
