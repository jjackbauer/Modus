//! Operator-authored requirement drafts and local validation.
//!
//! Local validation is immediate feedback only. The authoritative parse is performed by the factory
//! adapter's `/v1/requirement/preview` endpoint, which calls the actual requirement parser.

use serde::{Deserialize, Serialize};
use serde_json::Value;

pub const SUPPORTED_CLASS: &str = "aspnetcore-http-net10";

#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
pub struct RequirementDraft {
    pub title: String,
    pub description: String,
    #[serde(default)]
    pub functional_requirements: Vec<String>,
    #[serde(default)]
    pub acceptance_expectations: Vec<String>,
    #[serde(default)]
    pub constraints: Vec<String>,
    #[serde(default)]
    pub non_goals: Vec<String>,
    pub supported_class: String,
    pub template_reference: Option<String>,
    pub template_parameters_json: Option<String>,
    pub acceptance_contract_json: Option<String>,
    pub priority: Option<String>,
    pub deadline_utc: Option<String>,
    #[serde(default)]
    pub tags: Vec<String>,
    #[serde(default)]
    pub links: Vec<String>,
    pub notes: Option<String>,
}

impl RequirementDraft {
    pub fn new() -> Self {
        Self {
            supported_class: SUPPORTED_CLASS.to_string(),
            ..Default::default()
        }
    }

    /// Required local checks. These are not a substitute for the factory parser.
    pub fn validation_errors(&self) -> Vec<String> {
        let mut errors = Vec::new();
        if self.title.trim().is_empty() {
            errors.push("A title is required.".to_string());
        }

        if self.description.trim().is_empty() {
            errors.push("A description is required.".to_string());
        }

        if self
            .acceptance_expectations
            .iter()
            .all(|line| line.trim().is_empty())
        {
            errors.push(
                "At least one observable acceptance expectation is required; the factory refuses a requirement without one."
                    .to_string(),
            );
        }

        if self.supported_class.trim() != SUPPORTED_CLASS {
            errors.push(format!(
                "The supported application class is '{}'; '{}' is not accepted by the factory alpha.",
                SUPPORTED_CLASS,
                self.supported_class.trim()
            ));
        }

        if let Some(json) = self.acceptance_contract_json.as_deref() {
            if !json.trim().is_empty() {
                if let Err(message) = validate_acceptance_contract(json) {
                    errors.push(message);
                }
            }
        }

        errors
    }

    /// Warnings are shown to the operator but do not block preview; the factory proposal may flag
    /// unresolved questions and require them to be resolved before approval.
    pub fn validation_warnings(&self) -> Vec<String> {
        let mut warnings = Vec::new();
        for expectation in self
            .acceptance_expectations
            .iter()
            .filter(|line| !line.trim().is_empty())
        {
            if !has_observable_cue(expectation) {
                warnings.push(format!(
                    "This expectation has no observable HTTP cue and may become an unresolved proposal question: {expectation}"
                ));
            }
        }

        if self
            .functional_requirements
            .iter()
            .any(|line| !line.trim().is_empty())
            && self
                .acceptance_contract_json
                .as_deref()
                .unwrap_or("")
                .trim()
                .is_empty()
        {
            warnings.push(
                "Without an explicit acceptance contract, the proposal's scenarios may not be executable. Inspect the proposal before approving."
                    .to_string(),
            );
        }

        warnings
    }

    /// Produces the exact requirement Markdown sent to the adapter. Only acceptance expectations
    /// are emitted as bullet lines, because the factory parser treats every bullet as an expectation.
    pub fn to_markdown(&self) -> String {
        let mut out = String::new();
        out.push_str("---\n");
        out.push_str(&format!("title: {}\n", single_line(&self.title)));
        out.push_str(&format!(
            "supportedClass: {}\n",
            single_line(&self.supported_class)
        ));
        if let Some(template) = self
            .template_reference
            .as_deref()
            .map(str::trim)
            .filter(|value| !value.is_empty())
        {
            out.push_str(&format!("template: {}\n", single_line(template)));
        }
        if let Some(parameters) = self
            .template_parameters_json
            .as_deref()
            .map(str::trim)
            .filter(|value| !value.is_empty())
        {
            out.push_str(&format!("parameters: {}\n", single_line(parameters)));
        }
        out.push_str("---\n\n");
        out.push_str(self.description.trim());
        out.push_str("\n");

        append_numbered(
            &mut out,
            "Functional requirements",
            &self.functional_requirements,
        );
        append_bullets(
            &mut out,
            "Acceptance expectations",
            &self.acceptance_expectations,
        );
        append_numbered(&mut out, "Constraints", &self.constraints);
        append_numbered(&mut out, "Non-goals", &self.non_goals);
        out
    }

    pub fn planning_input(&self) -> crate::api::PlanningInput {
        crate::api::PlanningInput {
            priority: self
                .priority
                .clone()
                .filter(|value| !value.trim().is_empty()),
            deadline_utc: self
                .deadline_utc
                .clone()
                .filter(|value| !value.trim().is_empty()),
            tags: Some(self.tags.clone()).filter(|values| !values.is_empty()),
            links: Some(self.links.clone()).filter(|values| !values.is_empty()),
            notes: self.notes.clone().filter(|value| !value.trim().is_empty()),
        }
    }
}

fn append_numbered(out: &mut String, heading: &str, items: &[String]) {
    let items: Vec<&String> = items
        .iter()
        .filter(|item| !item.trim().is_empty())
        .collect();
    if items.is_empty() {
        return;
    }
    out.push_str("\n## ");
    out.push_str(heading);
    out.push_str("\n\n");
    for (index, item) in items.iter().enumerate() {
        out.push_str(&format!("{}. {}\n", index + 1, item.trim()));
    }
}

fn append_bullets(out: &mut String, heading: &str, items: &[String]) {
    let items: Vec<&String> = items
        .iter()
        .filter(|item| !item.trim().is_empty())
        .collect();
    if items.is_empty() {
        return;
    }
    out.push_str("\n## ");
    out.push_str(heading);
    out.push_str("\n\n");
    for item in items {
        out.push_str("- ");
        out.push_str(item.trim());
        out.push('\n');
    }
}

fn single_line(value: &str) -> String {
    value
        .replace(['\r', '\n'], " ")
        .trim()
        .chars()
        .take(300)
        .collect()
}

pub fn has_observable_cue(expectation: &str) -> bool {
    let lower = expectation.to_ascii_lowercase();
    [
        "returns", "status", "responds", "get ", "post ", "put ", "patch ", "delete ", "head ",
        "options ",
    ]
    .iter()
    .any(|cue| lower.contains(cue))
        || (100..600).any(|status| lower.contains(&status.to_string()))
}

/// Structural validation for the advanced contract editor. The authoritative parser is still the
/// adapter; this catches mistakes before a round trip.
pub fn validate_acceptance_contract(json: &str) -> Result<(), String> {
    let value: Value = serde_json::from_str(json)
        .map_err(|error| format!("Acceptance contract is not valid JSON: {error}"))?;
    let object = value
        .as_object()
        .ok_or_else(|| "Acceptance contract root must be an object.".to_string())?;

    if object
        .get("schemaVersion")
        .and_then(Value::as_i64)
        .is_none()
    {
        return Err("Acceptance contract requires a numeric schemaVersion.".to_string());
    }

    match object.get("supportedClass").and_then(Value::as_str) {
        Some(SUPPORTED_CLASS) => {}
        Some(other) => {
            return Err(format!(
                "Acceptance contract supportedClass must be '{SUPPORTED_CLASS}', not '{other}'."
            ));
        }
        None => return Err("Acceptance contract requires supportedClass.".to_string()),
    }

    let scenarios = object
        .get("scenarios")
        .and_then(Value::as_array)
        .filter(|scenarios| !scenarios.is_empty())
        .ok_or_else(|| "Acceptance contract requires at least one scenario.".to_string())?;

    let mut ids = std::collections::BTreeSet::new();
    for (index, scenario) in scenarios.iter().enumerate() {
        let scenario = scenario
            .as_object()
            .ok_or_else(|| format!("Scenario {} must be an object.", index + 1))?;
        let id = scenario
            .get("id")
            .and_then(Value::as_str)
            .filter(|id| !id.trim().is_empty())
            .ok_or_else(|| format!("Scenario {} requires a non-empty id.", index + 1))?;
        if !ids.insert(id.to_string()) {
            return Err(format!("Duplicate scenario id '{id}'."));
        }
        for key in ["method", "path"] {
            if scenario
                .get(key)
                .and_then(Value::as_str)
                .is_none_or(str::is_empty)
            {
                return Err(format!("Scenario '{id}' requires {key}."));
            }
        }
        let status = scenario
            .get("expect")
            .and_then(Value::as_object)
            .and_then(|expect| expect.get("status"))
            .and_then(Value::as_i64);
        if !matches!(status, Some(100..=599)) {
            return Err(format!(
                "Scenario '{id}' requires an expected HTTP status between 100 and 599."
            ));
        }
    }

    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn only_acceptance_expectations_become_bullets() {
        let draft = RequirementDraft {
            title: "Health".into(),
            description: "Build a health endpoint.".into(),
            functional_requirements: vec!["Expose readiness".into()],
            acceptance_expectations: vec!["GET /health returns 200".into()],
            constraints: vec!["No database".into()],
            supported_class: SUPPORTED_CLASS.into(),
            ..Default::default()
        };

        let markdown = draft.to_markdown();
        assert!(markdown.contains("- GET /health returns 200"));
        assert!(markdown.contains("1. Expose readiness"));
        assert!(markdown.contains("1. No database"));
        assert!(!markdown.contains("- Expose readiness"));
    }

    #[test]
    fn contract_validation_rejects_missing_status() {
        let error = validate_acceptance_contract(
            r#"{"schemaVersion":1,"supportedClass":"aspnetcore-http-net10","scenarios":[{"id":"AC-1","method":"GET","path":"/health","expect":{}}]}"#,
        )
        .unwrap_err();
        assert!(error.contains("status"));
    }

    #[test]
    fn contract_validation_accepts_well_formed_contract() {
        validate_acceptance_contract(
            r#"{"schemaVersion":1,"supportedClass":"aspnetcore-http-net10","scenarios":[{"id":"AC-1","method":"GET","path":"/health","expect":{"status":200}}]}"#,
        )
        .unwrap();
    }
}
