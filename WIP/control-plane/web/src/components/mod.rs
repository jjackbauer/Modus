//! Shared, compositional interaction primitives.
//!
//! Each module owns one concern and exposes Leptos components plus the small amount of pure logic
//! they rely on (which is unit-tested). Pages compose them; no page owns a second version of the
//! same dialog, table, status or tab contract.

pub mod evidence;
pub mod feedback;
pub mod layout;
pub mod modal;
pub mod planning;
pub mod shell;
pub mod status;
pub mod table;
pub mod tabs;
pub mod ui;

pub use evidence::{DiffViewer, EvidenceCheck, EvidenceSummary};
pub use feedback::{
    Confirmation, EmptyState, ErrorBoundary, FieldError, InlineValidation, TypedConfirmation,
    ValidationMessage,
};
pub use layout::{ActionToolbar, FilterBar, PageHeader, Section};
pub use status::{
    EntityIdentity, FreshnessIndicator, HealthState, MetadataList, MetadataRow, StatusIndicator,
};
pub use table::{Column, DataTable, Sort, SortDirection, TableState};
pub use tabs::{TabItem, TabPanel, Tabs};
