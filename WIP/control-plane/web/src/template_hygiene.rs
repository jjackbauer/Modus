//! Static guards for Leptos templates.
//!
//! `view!` treats unquoted Rust tokens as literal text nodes, so writing Rust control flow
//! directly inside the macro ships the source tokens to the operator's screen. This module
//! fails the test suite if that pattern reappears anywhere under `src/`.

#[cfg(test)]
mod tests {
    use std::fs;
    use std::path::{Path, PathBuf};

    fn rust_sources(root: &Path, found: &mut Vec<PathBuf>) {
        let Ok(entries) = fs::read_dir(root) else {
            return;
        };
        for entry in entries.flatten() {
            let path = entry.path();
            if path.is_dir() {
                rust_sources(&path, found);
            } else if path.extension().and_then(|value| value.to_str()) == Some("rs") {
                found.push(path);
            }
        }
    }

    #[test]
    fn view_macros_do_not_embed_rust_control_flow() {
        let root = Path::new(env!("CARGO_MANIFEST_DIR")).join("src");
        let mut files = Vec::new();
        rust_sources(&root, &mut files);
        assert!(!files.is_empty(), "no source files found under {root:?}");

        let mut offenders = Vec::new();
        for path in files {
            let contents = fs::read_to_string(&path).expect("source file is UTF-8");
            let lines: Vec<&str> = contents.lines().collect();
            for (index, line) in lines.iter().enumerate() {
                let trimmed = line.trim_end();
                if !(trimmed.ends_with("view! {") || trimmed.ends_with("view!{")) {
                    continue;
                }
                let Some(next) = lines.get(index + 1) else {
                    continue;
                };
                let head = next.trim_start();
                if head.starts_with("if ") || head.starts_with("for ") || head.starts_with("match ")
                {
                    offenders.push(format!("{}:{} {}", path.display(), index + 2, head));
                }
            }
        }

        assert!(
            offenders.is_empty(),
            "Rust control flow was written inside a view! macro and renders as literal text. \
             Prepare the view outside the macro (or use a {{move || ...}} closure) instead:\n{}",
            offenders.join("\n")
        );
    }
}