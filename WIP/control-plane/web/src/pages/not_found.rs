use leptos::prelude::*;

#[component]
pub fn NotFound() -> impl IntoView {
    view! {
        <section class="page">
            <h1 class="page-title">"Not found"</h1>
            <p class="page-subtitle">"This control-plane route does not exist."</p>
            <a class="btn" href="/">"Back to the factory"</a>
        </section>
    }
}
