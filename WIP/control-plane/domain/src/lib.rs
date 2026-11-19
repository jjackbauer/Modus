//! Shared domain layer for the Wip.Factory control plane.
//!
//! The factory remains the execution authority. This crate contains only serialization-stable view
//! models, local draft validation and safe rendering helpers shared by the Axum server and the
//! Leptos client.

pub mod api;
pub mod draft;
pub mod labels;
pub mod markdown;

pub use api::*;
pub use draft::{RequirementDraft, SUPPORTED_CLASS};
pub use labels::*;
pub use markdown::{escape_html, render_inline, render_markdown};

pub fn version() -> &'static str {
    env!("CARGO_PKG_VERSION")
}
