//! One data table primitive: caption, column scope, sort contract, a labelled keyboard-scrollable
//! region with a visible hint, and explicit populated/empty/loading/error states.
//!
//! The table does not fetch and does not sort: the caller owns the data and the ordering, because
//! the ordering must follow a definition the operator can read. This primitive only makes the
//! contract legible and keyboard-operable.

use leptos::prelude::*;

use crate::components::feedback::EmptyState;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SortDirection {
    Ascending,
    Descending,
}

impl SortDirection {
    pub fn aria_value(self) -> &'static str {
        match self {
            SortDirection::Ascending => "ascending",
            SortDirection::Descending => "descending",
        }
    }
}

/// The ordering the table currently shows. `key` is the column's `sort_key`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Sort {
    pub key: &'static str,
    pub direction: SortDirection,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Column {
    pub label: String,
    /// Present when the column can be sorted; the value is what `Sort::key` compares against.
    pub sort_key: Option<&'static str>,
    pub numeric: bool,
}

impl Column {
    pub fn text(label: impl Into<String>) -> Self {
        Self {
            label: label.into(),
            sort_key: None,
            numeric: false,
        }
    }

    pub fn sortable(label: impl Into<String>, key: &'static str) -> Self {
        Self {
            label: label.into(),
            sort_key: Some(key),
            numeric: false,
        }
    }

    pub fn numeric(label: impl Into<String>, key: Option<&'static str>) -> Self {
        Self {
            label: label.into(),
            sort_key: key,
            numeric: true,
        }
    }
}

/// `aria-sort` for one column: absent when the column is not sortable, `none` when it is sortable
/// but not the current ordering, and the direction otherwise. A sort control that does not say
/// what it currently does is a false affordance.
pub fn aria_sort(column: &Column, sort: Option<Sort>) -> Option<&'static str> {
    let key = column.sort_key?;
    Some(match sort {
        Some(sort) if sort.key == key => sort.direction.aria_value(),
        _ => "none",
    })
}

#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub enum TableState {
    #[default]
    Populated,
    Empty,
    Loading,
    Error(String),
}

fn next_table_id() -> u32 {
    use std::sync::atomic::{AtomicU32, Ordering};
    static NEXT: AtomicU32 = AtomicU32::new(0);
    NEXT.fetch_add(1, Ordering::Relaxed)
}

/// A labelled, focusable scroll region. Focus is what makes the arrow keys work, so the region
/// carries `tabindex="0"` and the hint text is attached with `aria-describedby`: a keyboard user
/// is told that the table scrolls instead of discovering it by accident.
#[component]
pub fn DataTable(
    /// Visible caption and accessible name of the table.
    caption: String,
    columns: Vec<Column>,
    /// Rows the caller rendered, used to choose the empty state.
    rows: usize,
    #[prop(optional)] state: TableState,
    #[prop(optional_no_strip)] sort: Option<Sort>,
    #[prop(optional_no_strip)] on_sort: Option<Callback<&'static str>>,
    #[prop(optional_no_strip)] on_retry: Option<Callback<()>>,
    #[prop(optional_no_strip)] empty: Option<AnyView>,
    children: Children,
) -> impl IntoView {
    let table_id = format!("data-table-{}", next_table_id());
    let hint_id = format!("{table_id}-hint");
    let caption_id = format!("{table_id}-caption");
    let hint = "This table scrolls horizontally when it is wider than the screen. The region is focusable: use the arrow keys to scroll it.";

    let headers = columns
        .iter()
        .map(|column| {
            let label = column.label.clone();
            let numeric = column.numeric;
            let sort_attr = aria_sort(column, sort);
            let content: AnyView = match (column.sort_key, on_sort) {
                (Some(key), Some(callback)) => view! {
                    <button
                        class="th-sort"
                        type="button"
                        on:click=move |_| callback.run(key)
                    >
                        {label.clone()}
                        <span class="th-sort-mark" aria-hidden="true">
                            {move || match sort {
                                Some(sort) if sort.key == key => match sort.direction {
                                    SortDirection::Ascending => "▲",
                                    SortDirection::Descending => "▼",
                                },
                                _ => "↕",
                            }}
                        </span>
                    </button>
                }
                .into_any(),
                _ => view! { <span>{label.clone()}</span> }.into_any(),
            };
            match sort_attr {
                Some(value) => view! {
                    <th scope="col" class=if numeric { "numeric" } else { "" } aria-sort=value>{content}</th>
                }
                .into_any(),
                None => view! {
                    <th scope="col" class=if numeric { "numeric" } else { "" }>{content}</th>
                }
                .into_any(),
            }
        })
        .collect_view();

    let body: AnyView = match state {
        TableState::Loading => view! {
            <div class="table-state" role="status" aria-live="polite" aria-busy="true">
                <span class="spinner" aria-hidden="true"></span>
                <span>{format!("Loading {caption}…")}</span>
            </div>
        }
        .into_any(),
        TableState::Error(message) => view! {
            <div class="error-state" role="alert">
                <strong>"This table could not be loaded."</strong>
                <p>{message}</p>
                {on_retry.map(|retry| view! {
                    <button class="btn btn-sm" type="button" on:click=move |_| retry.run(())>"Retry"</button>
                })}
            </div>
        }
        .into_any(),
        _ if rows == 0 => match empty {
            Some(empty) => empty,
            None => view! {
                <EmptyState
                    title="Nothing to show yet"
                    detail="This table has no rows for the current filters."
                />
            }
            .into_any(),
        },
        _ => view! {
            <div class="table-frame">
                <div
                    class="table-scroll"
                    tabindex="0"
                    role="region"
                    aria-labelledby=caption_id.clone()
                    aria-describedby=hint_id.clone()
                >
                    <table class="data">
                        <caption id=caption_id.clone() class="table-caption">{caption.clone()}</caption>
                        <thead>
                            <tr>{headers}</tr>
                        </thead>
                        <tbody>{children()}</tbody>
                    </table>
                </div>
                <p class="table-hint" id=hint_id.clone()>{hint}</p>
            </div>
        }
        .into_any(),
    };

    view! { <div class="data-table">{body}</div> }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn column(key: Option<&'static str>) -> Column {
        Column {
            label: "Column".to_string(),
            sort_key: key,
            numeric: false,
        }
    }

    #[test]
    fn aria_sort_describes_the_current_ordering_and_nothing_else() {
        let sortable = Column::sortable("Updated", "updated");
        assert_eq!(aria_sort(&column(None), None), None);
        assert_eq!(aria_sort(&sortable, None), Some("none"));
        assert_eq!(
            aria_sort(
                &sortable,
                Some(Sort {
                    key: "priority",
                    direction: SortDirection::Descending
                })
            ),
            Some("none")
        );
        assert_eq!(
            aria_sort(
                &sortable,
                Some(Sort {
                    key: "updated",
                    direction: SortDirection::Ascending
                })
            ),
            Some("ascending")
        );
        assert_eq!(
            aria_sort(
                &sortable,
                Some(Sort {
                    key: "updated",
                    direction: SortDirection::Descending
                })
            ),
            Some("descending")
        );
    }

    #[test]
    fn numeric_columns_can_carry_a_sort_key() {
        let numeric = Column::numeric("Priority", Some("priority"));
        assert!(numeric.numeric);
        assert_eq!(numeric.sort_key, Some("priority"));
        assert_eq!(Column::text("Title").sort_key, None);
    }

    #[test]
    fn table_state_defaults_to_populated_so_a_caller_must_choose_empty_explicitly() {
        assert_eq!(TableState::default(), TableState::Populated);
    }
}
