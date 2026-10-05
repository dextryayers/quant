use crate::embed::Embed;
use crate::index::{Chunk, Symbol};
use serde::Serialize;

#[derive(Debug, Clone, Serialize)]
pub struct Hit {
    pub path: String,
    pub start_line: usize,
    pub end_line: usize,
    pub score: f32,
    pub why: String,
    pub preview: String,
}

pub fn retrieve(
    query: &str,
    chunks: &[Chunk],
    symbols: &[Symbol],
    embed: &Embed,
    open_tabs: &[String],
    top_k: usize,
) -> Vec<Hit> {
    let qv = embed.vec(query);
    let ql = query.to_lowercase();
    let mut scored: Vec<Hit> = Vec::new();

    for c in chunks.iter().take(5000) {
        let cv = embed.vec(&c.text);
        let cos = Embed::cosine(&qv, &cv);
        let bm = bm25(&ql, &c.text.to_lowercase());
        let mut score = cos * 0.6 + bm * 0.4;
        let mut why = format!("cos {cos:.2} bm {bm:.2}");
        if open_tabs.iter().any(|t| t == &c.path) {
            score += 0.3;
            why.push_str(" +open-tab");
        }
        if c.path.to_lowercase().contains(&ql_query_file(&ql)) {
            score += 0.2;
            why.push_str(" +path");
        }
        if score > 0.05 {
            scored.push(Hit {
                path: c.path.clone(),
                start_line: c.start_line,
                end_line: c.end_line,
                score,
                why,
                preview: c.text.lines().take(6).collect::<Vec<_>>().join("\n"),
            });
        }
    }
    // Symbol exact boost
    for s in symbols.iter().take(500) {
        if s.name.to_lowercase().contains(&ql) && ql.len() >= 3 {
            scored.push(Hit {
                path: s.path.clone(),
                start_line: s.line,
                end_line: s.line,
                score: 0.9,
                why: format!("symbol {}", s.kind),
                preview: s.signature.clone(),
            });
        }
    }
    scored.sort_by(|a, b| b.score.partial_cmp(&a.score).unwrap_or(std::cmp::Ordering::Equal));
    scored.truncate(top_k);
    scored
}

fn bm25(query: &str, doc: &str) -> f32 {
    // Simplified BM25-ish: term overlap normalized.
    let terms: Vec<&str> = query.split_whitespace().filter(|w| w.len() >= 3).collect();
    if terms.is_empty() {
        return 0.0;
    }
    let mut hit = 0;
    for t in &terms {
        if doc.contains(t) {
            hit += 1;
        }
    }
    (hit as f32 / terms.len() as f32).min(1.0)
}

fn ql_query_file(ql: &str) -> String {
    ql.split_whitespace().next().unwrap_or("").to_string()
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::index::{chunk_text, symbols_for};
    #[test]
    fn retrieval_hits_expected_file() {
        let e = Embed::new();
        let a = chunk_text("src/auth.rs", "fn login user password session token auth");
        let b = chunk_text("src/pizza.rs", "cheese dough oven italian recipe");
        let mut all = vec![];
        all.extend(a);
        all.extend(b);
        let syms = symbols_for("src/auth.rs", "fn login() {}");
        let hits = retrieve("login auth", &all, &syms, &e, &[], 3);
        assert!(!hits.is_empty());
        assert_eq!(hits[0].path, "src/auth.rs");
    }
}
