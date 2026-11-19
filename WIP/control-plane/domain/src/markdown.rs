//! A deliberately small, escaping Markdown renderer for untrusted operator text.
//!
//! The control plane never passes raw Markdown HTML into the DOM. Only this renderer's escaped
//! output is allowed through `inner_html`, and link targets are restricted to HTTP(S) or
//! same-origin paths.

/// Renders a safe HTML fragment from a Markdown subset: headings, paragraphs, unordered and
/// ordered lists, blockquotes, fenced code, inline code, emphasis, strikethrough and links.
pub fn render_markdown(source: &str) -> String {
    let normalized = source.replace("\r\n", "\n").replace('\r', "\n");
    let lines: Vec<&str> = normalized.split('\n').collect();
    let mut out = String::new();
    let mut index = 0;
    let mut paragraph: Vec<String> = Vec::new();
    let mut list: Option<ListKind> = None;

    while index < lines.len() {
        let line = lines[index];
        let trimmed = line.trim();

        if let Some(fence) = code_fence(trimmed) {
            flush_paragraph(&mut paragraph, &mut out);
            close_list(&mut list, &mut out);
            let mut code = String::new();
            index += 1;
            while index < lines.len() && !lines[index].trim().starts_with(&fence) {
                code.push_str(lines[index]);
                code.push('\n');
                index += 1;
            }
            index += 1;
            out.push_str("<pre class=\"md-code\"><code>");
            out.push_str(&escape_html(&code));
            out.push_str("</code></pre>");
            continue;
        }

        if trimmed.is_empty() {
            flush_paragraph(&mut paragraph, &mut out);
            close_list(&mut list, &mut out);
            index += 1;
            continue;
        }

        if let Some((level, text)) = heading(trimmed) {
            flush_paragraph(&mut paragraph, &mut out);
            close_list(&mut list, &mut out);
            out.push_str(&format!(
                "<h{0} class=\"md-h{0}\">{1}</h{0}>",
                level,
                render_inline(text)
            ));
            index += 1;
            continue;
        }

        if is_horizontal_rule(trimmed) {
            flush_paragraph(&mut paragraph, &mut out);
            close_list(&mut list, &mut out);
            out.push_str("<hr class=\"md-rule\"/>");
            index += 1;
            continue;
        }

        if let Some(quote) = trimmed.strip_prefix('>') {
            flush_paragraph(&mut paragraph, &mut out);
            close_list(&mut list, &mut out);
            out.push_str("<blockquote class=\"md-quote\">");
            out.push_str(&render_inline(quote.trim()));
            out.push_str("</blockquote>");
            index += 1;
            continue;
        }

        if let Some(item) = unordered_item(trimmed) {
            flush_paragraph(&mut paragraph, &mut out);
            ensure_list(&mut list, ListKind::Unordered, &mut out);
            out.push_str("<li>");
            out.push_str(&render_inline(item));
            out.push_str("</li>");
            index += 1;
            continue;
        }

        if let Some(item) = ordered_item(trimmed) {
            flush_paragraph(&mut paragraph, &mut out);
            ensure_list(&mut list, ListKind::Ordered, &mut out);
            out.push_str("<li>");
            out.push_str(&render_inline(item));
            out.push_str("</li>");
            index += 1;
            continue;
        }

        close_list(&mut list, &mut out);
        paragraph.push(trimmed.to_string());
        index += 1;
    }

    flush_paragraph(&mut paragraph, &mut out);
    close_list(&mut list, &mut out);
    out
}

#[derive(Clone, Copy, PartialEq, Eq)]
enum ListKind {
    Unordered,
    Ordered,
}

fn ensure_list(list: &mut Option<ListKind>, kind: ListKind, out: &mut String) {
    match list {
        Some(current) if *current == kind => {}
        Some(_) => {
            close_list(list, out);
            open_list(kind, out);
        }
        None => open_list(kind, out),
    }
}

fn open_list(kind: ListKind, out: &mut String) {
    match kind {
        ListKind::Unordered => out.push_str("<ul class=\"md-list\">"),
        ListKind::Ordered => out.push_str("<ol class=\"md-list\">"),
    }
}

fn close_list(list: &mut Option<ListKind>, out: &mut String) {
    if let Some(kind) = list.take() {
        match kind {
            ListKind::Unordered => out.push_str("</ul>"),
            ListKind::Ordered => out.push_str("</ol>"),
        }
    }
}

fn flush_paragraph(paragraph: &mut Vec<String>, out: &mut String) {
    if paragraph.is_empty() {
        return;
    }

    out.push_str("<p>");
    for (index, line) in paragraph.iter().enumerate() {
        if index > 0 {
            out.push_str("<br/>");
        }
        out.push_str(&render_inline(line));
    }
    out.push_str("</p>");
    paragraph.clear();
}

fn code_fence(line: &str) -> Option<String> {
    if line.starts_with("```") {
        let language = line.trim_matches('`').trim();
        let _ = language;
        Some("```".to_string())
    } else {
        None
    }
}

fn heading(line: &str) -> Option<(usize, &str)> {
    let hashes = line
        .chars()
        .take_while(|character| *character == '#')
        .count();
    if !(1..=6).contains(&hashes) {
        return None;
    }

    let rest = line[hashes..].trim_start();
    if line.len() == hashes || !rest.starts_with(' ') {
        return None;
    }

    Some((hashes, rest.trim_start()))
}

fn is_horizontal_rule(line: &str) -> bool {
    let stripped: String = line.chars().filter(|character| *character != ' ').collect();
    stripped.len() >= 3
        && (stripped.chars().all(|character| character == '-')
            || stripped.chars().all(|character| character == '*')
            || stripped.chars().all(|character| character == '_'))
}

fn unordered_item(line: &str) -> Option<&str> {
    for marker in ["- ", "* ", "+ "] {
        if let Some(rest) = line.strip_prefix(marker) {
            return Some(rest.trim());
        }
    }
    None
}

fn ordered_item(line: &str) -> Option<&str> {
    let digits = line.chars().take_while(char::is_ascii_digit).count();
    if digits == 0 {
        return None;
    }

    let rest = &line[digits..];
    if let Some(text) = rest.strip_prefix(". ") {
        return Some(text.trim());
    }
    if let Some(text) = rest.strip_prefix(") ") {
        return Some(text.trim());
    }
    None
}

/// Escapes text and applies the safe inline subset. The returned HTML is the only trusted output.
pub fn render_inline(source: &str) -> String {
    let mut out = String::new();
    let bytes: Vec<char> = source.chars().collect();
    let mut index = 0;

    while index < bytes.len() {
        let character = bytes[index];

        if character == '`' {
            if let Some(end) = find_char(&bytes, index + 1, '`') {
                let code: String = bytes[index + 1..end].iter().collect();
                out.push_str("<code>");
                out.push_str(&escape_html(&code));
                out.push_str("</code>");
                index = end + 1;
                continue;
            }
        }

        if character == '*' && index + 1 < bytes.len() && bytes[index + 1] == '*' {
            if let Some(end) = find_pair(&bytes, index + 2, '*', '*') {
                let text: String = bytes[index + 2..end].iter().collect();
                out.push_str("<strong>");
                out.push_str(&render_inline(&text));
                out.push_str("</strong>");
                index = end + 2;
                continue;
            }
        }

        if character == '*' {
            if let Some(end) = find_char(&bytes, index + 1, '*') {
                if end > index + 1 {
                    let text: String = bytes[index + 1..end].iter().collect();
                    out.push_str("<em>");
                    out.push_str(&render_inline(&text));
                    out.push_str("</em>");
                    index = end + 1;
                    continue;
                }
            }
        }

        if character == '~' && index + 1 < bytes.len() && bytes[index + 1] == '~' {
            if let Some(end) = find_pair(&bytes, index + 2, '~', '~') {
                let text: String = bytes[index + 2..end].iter().collect();
                out.push_str("<del>");
                out.push_str(&render_inline(&text));
                out.push_str("</del>");
                index = end + 2;
                continue;
            }
        }

        if character == '[' {
            if let Some((label, url, next)) = parse_link(&bytes, index) {
                if is_safe_url(&url) {
                    out.push_str("<a href=\"");
                    out.push_str(&escape_attr(&url));
                    out.push_str("\" rel=\"noopener noreferrer\" target=\"_blank\">");
                    out.push_str(&render_inline(&label));
                    out.push_str("</a>");
                } else {
                    out.push_str(&escape_html(&label));
                    out.push_str(" (");
                    out.push_str(&escape_html(&url));
                    out.push_str(")");
                }
                index = next;
                continue;
            }
        }

        out.push_str(&escape_html(&character.to_string()));
        index += 1;
    }

    out
}

fn find_char(chars: &[char], start: usize, needle: char) -> Option<usize> {
    (start..chars.len()).find(|index| chars[*index] == needle)
}

fn find_pair(chars: &[char], start: usize, first: char, second: char) -> Option<usize> {
    if start >= chars.len() {
        return None;
    }
    (start..chars.len().saturating_sub(1))
        .find(|index| chars[*index] == first && chars[index + 1] == second)
}

fn parse_link(chars: &[char], start: usize) -> Option<(String, String, usize)> {
    let close = find_char(chars, start + 1, ']')?;
    if close + 1 >= chars.len() || chars[close + 1] != '(' {
        return None;
    }
    let end = find_char(chars, close + 2, ')')?;
    let label: String = chars[start + 1..close].iter().collect();
    let url: String = chars[close + 2..end].iter().collect();
    Some((label, url.trim().to_string(), end + 1))
}

fn is_safe_url(url: &str) -> bool {
    let lower = url.trim().to_ascii_lowercase();
    lower.starts_with("https://") || lower.starts_with("http://") || lower.starts_with('/')
}

pub fn escape_html(value: &str) -> String {
    let mut out = String::with_capacity(value.len());
    for character in value.chars() {
        match character {
            '&' => out.push_str("&amp;"),
            '<' => out.push_str("&lt;"),
            '>' => out.push_str("&gt;"),
            '"' => out.push_str("&quot;"),
            '\'' => out.push_str("&#39;"),
            _ => out.push(character),
        }
    }
    out
}

fn escape_attr(value: &str) -> String {
    escape_html(value)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn raw_html_is_escaped() {
        let html = render_markdown("<script>alert('x')</script>");
        assert!(!html.contains("<script>"));
        assert!(html.contains("&lt;script&gt;"));
    }

    #[test]
    fn javascript_urls_are_not_links() {
        let html = render_markdown("[click](javascript:alert(1))");
        assert!(!html.contains("href=\"javascript"));
        assert!(html.contains("javascript:alert(1)"));
    }

    #[test]
    fn https_links_are_allowed_and_escaped() {
        let html = render_markdown("[docs](https://example.test/a?b=1&c=2)");
        assert!(html.contains("href=\"https://example.test/a?b=1&amp;c=2\""));
        assert!(html.contains("rel=\"noopener noreferrer\""));
    }

    #[test]
    fn lists_and_code_fences_render() {
        let html = render_markdown("- one\n- two\n\n```\n<x>\n```");
        assert!(html.contains("<ul class=\"md-list\">"));
        assert!(html.contains("<li>one</li>"));
        assert!(html.contains("&lt;x&gt;"));
    }
}
