//! Empty, error, validation and confirmation primitives.
//!
//! These are the states an operator meets when something is missing, refused or unclear. They
//! share two rules: the next safe action is always visible, and no error is reported without
//! saying what it applies to.

use leptos::prelude::*;

/// Nothing exists here yet, or nothing matches. An empty state always answers "what could I do",
/// which is why the action slot is part of the primitive rather than page decoration.
#[component]
pub fn EmptyState(
    #[prop(into)] title: String,
    #[prop(into)] detail: String,
    #[prop(optional_no_strip)] action: Option<AnyView>,
) -> impl IntoView {
    view! {
        <div class="empty-state">
            <h2>{title}</h2>
            <p>{detail}</p>
            {action}
        </div>
    }
}

/// A failed request with a safe retry. It renders its children only while there is no error, so a
/// caller cannot accidentally show stale content under a failure banner.
#[component]
pub fn ErrorBoundary(
    error: Option<String>,
    #[prop(optional_no_strip)] detail: Option<String>,
    #[prop(optional_no_strip)] on_retry: Option<Callback<()>>,
    children: Children,
) -> impl IntoView {
    match error {
        None => children().into_any(),
        Some(message) => view! {
            <div class="error-state" role="alert">
                <strong>"The control plane could not complete this request."</strong>
                <p>{message}</p>
                {detail.map(|detail| view! { <pre>{detail}</pre> })}
                {on_retry.map(|retry| view! {
                    <button class="btn btn-sm" type="button" on:click=move |_| retry.run(())>
                        "Retry"
                    </button>
                })}
            </div>
        }
        .into_any(),
    }
}

/// One field-level problem. `field_id` must be the id of the control the message belongs to, so
/// the summary can link to it and the control can point back with `aria-describedby`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct FieldError {
    pub field_id: String,
    pub label: String,
    pub message: String,
}

impl FieldError {
    pub fn new(
        field_id: impl Into<String>,
        label: impl Into<String>,
        message: impl Into<String>,
    ) -> Self {
        Self {
            field_id: field_id.into(),
            label: label.into(),
            message: message.into(),
        }
    }

    pub fn href(&self) -> String {
        format!("#{}", self.field_id)
    }

    pub fn error_id(&self) -> String {
        format!("{}-error", self.field_id)
    }
}

/// Pluralisation lives next to the summary so every form says it the same way.
pub fn validation_summary_title(count: usize) -> String {
    match count {
        0 => "No problems to fix".to_string(),
        1 => "1 problem needs attention".to_string(),
        count => format!("{count} problems need attention"),
    }
}

/// One summary for a form, with a link to every field it describes. A per-field message alone
/// hides how many problems exist and where they are.
#[component]
pub fn InlineValidation(errors: Vec<FieldError>) -> impl IntoView {
    let count = errors.len();
    if count == 0 {
        return view! { <span class="hidden" aria-hidden="true"></span> }.into_any();
    }
    view! {
        <div class="validation-summary" role="alert" tabindex="-1">
            <strong>{validation_summary_title(count)}</strong>
            <ul>
                {errors
                    .into_iter()
                    .map(|error| {
                        let href = error.href();
                        let label = error.label.clone();
                        view! {
                            <li>
                                <a href=href>{label}</a>
                                <span>{format!(": {}", error.message)}</span>
                            </li>
                        }
                    })
                    .collect_view()}
            </ul>
        </div>
    }
    .into_any()
}

/// The message attached to one control. Rendered next to the field and referenced by its
/// `aria-describedby`, so it is not only a colour change.
#[component]
pub fn ValidationMessage(field_id: String, message: String) -> impl IntoView {
    view! {
        <p class="field-error" id=format!("{field_id}-error") role="alert">
            {message}
        </p>
    }
}

/// An explicit assertion the operator makes before a governed mutation. The assertion names what
/// was shown; the optional typed confirmation binds the exact digest the server will compare.
#[derive(Clone)]
pub struct TypedConfirmation {
    pub label: String,
    pub expected: String,
    pub value: RwSignal<String>,
}

/// Whether the operator's typed value matches the digest that was displayed. The server compares
/// again; this only prevents an obviously misleading confirm button.
pub fn typed_confirmation_matches(expected: &str, typed: &str) -> bool {
    !expected.trim().is_empty() && typed.trim() == expected.trim()
}

/// Confirmation is the assertion plus, when required, the exact digest match. Both must hold.
pub fn confirmation_satisfied(asserted: bool, typed_matches: Option<bool>) -> bool {
    asserted && typed_matches.unwrap_or(true)
}

#[component]
pub fn Confirmation(
    /// The sentence the operator agrees to. It must describe what was actually rendered.
    #[prop(into)]
    assertion: String,
    checked: RwSignal<bool>,
    #[prop(optional_no_strip)] typed: Option<TypedConfirmation>,
) -> impl IntoView {
    view! {
        <div class="confirmation">
            <label class="check">
                <input
                    type="checkbox"
                    prop:checked=move || checked.get()
                    on:change=move |event| checked.set(event_target_checked(&event))
                />
                <span>{assertion}</span>
            </label>
            {typed.map(|typed| {
                let TypedConfirmation { label, expected, value } = typed;
                let matches = Signal::derive({
                    let expected = expected.clone();
                    move || typed_confirmation_matches(&expected, &value.get())
                });
                let field_id = "confirmation-digest".to_string();
                view! {
                    <label class="field">
                        <span class="label">{label}</span>
                        <input
                            class="input mono wrap"
                            id=field_id.clone()
                            autocomplete="off"
                            spellcheck="false"
                            prop:value=move || value.get()
                            on:input=move |event| value.set(event_target_value(&event))
                            aria-describedby=format!("{field_id}-state")
                        />
                    </label>
                    <p class="tiny" id=format!("{field_id}-state") aria-live="polite">
                        {move || if matches.get() {
                            "Matches the digest shown above."
                        } else {
                            "Does not match the digest shown above yet."
                        }}
                    </p>
                    <p class="tiny faint">
                        "The adapter compares the digest independently on submit and refuses a mismatch."
                    </p>
                }
            })}
        </div>
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_summary_says_how_many_problems_exist() {
        assert_eq!(validation_summary_title(0), "No problems to fix");
        assert_eq!(validation_summary_title(1), "1 problem needs attention");
        assert_eq!(validation_summary_title(3), "3 problems need attention");
    }

    #[test]
    fn a_field_error_links_to_its_control_and_names_its_own_message() {
        let error = FieldError::new("title", "Title", "is required");
        assert_eq!(error.href(), "#title");
        assert_eq!(error.error_id(), "title-error");
    }

    #[test]
    fn a_typed_confirmation_must_match_after_trimming_and_cannot_be_empty() {
        assert!(typed_confirmation_matches(" sha256:abc ", "sha256:abc"));
        assert!(!typed_confirmation_matches("sha256:abc", "sha256:abd"));
        assert!(!typed_confirmation_matches("", ""));
        assert!(!typed_confirmation_matches("sha256:abc", ""));
    }

    #[test]
    fn confirmation_needs_the_assertion_and_the_typed_digest_when_one_is_required() {
        assert!(confirmation_satisfied(true, None));
        assert!(!confirmation_satisfied(false, None));
        assert!(confirmation_satisfied(true, Some(true)));
        assert!(!confirmation_satisfied(true, Some(false)));
        assert!(!confirmation_satisfied(false, Some(true)));
    }
}
