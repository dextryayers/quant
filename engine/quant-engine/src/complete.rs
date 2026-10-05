use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::sync::{Arc, Mutex};
use std::time::Instant;

#[derive(Debug, Clone, Serialize)]
pub struct Suggestion {
    pub text: String,
    pub score: f32,
    pub why: String,
}

#[derive(Clone)]
pub struct Completer {
    cache: Arc<Mutex<HashMap<String, (Vec<Suggestion>, Instant)>>>,
}

impl Completer {
    pub fn new() -> Self {
        Self { cache: Arc::new(Mutex::new(HashMap::new())) }
    }

    pub fn complete(
        &self,
        prefix: &str,
        suffix: &str,
        file: &str,
        hints: &[String],
    ) -> (Vec<Suggestion>, bool, u64) {
        let t0 = Instant::now();
        let tail = prefix.chars().rev().take(120).collect::<String>();
        let key = format!("{:x}:{:x}", fnv(&format!("{file}:{tail}")), fnv(suffix));
        if let Some((cached, t)) = self.cache.lock().unwrap().get(&key).cloned() {
            if t.elapsed().as_secs() < 30 {
                return (cached, true, t0.elapsed().as_millis() as u64);
            }
        }
        let mut out = suggest(prefix, suffix, hints);
        out.truncate(3);
        self.cache.lock().unwrap().insert(key, (out.clone(), Instant::now()));
        // cap cache
        let mut c = self.cache.lock().unwrap();
        if c.len() > 200 {
            c.clear();
        }
        (out, false, t0.elapsed().as_millis() as u64)
    }
}

fn suggest(prefix: &str, suffix: &str, hints: &[String]) -> Vec<Suggestion> {
    let mut out = Vec::new();
    let line_start = prefix.rfind('\n').map(|i| i + 1).unwrap_or(0);
    let cur = prefix[line_start..].to_string();
    let indent: String = cur.chars().take_while(|c| *c == ' ' || *c == '\t').collect();
    let trimmed = cur.trim_start();

    // 1. bracket closer: if suffix starts with closer and prefix opens, suggest closer
    let opens = prefix.chars().filter(|c| *c == '{').count();
    let closes = prefix.chars().filter(|c| *c == '}').count();
    if opens > closes && (suffix.trim_start().starts_with('}') || suffix.trim().is_empty()) {
        out.push(Suggestion {
            text: format!("\n{indent}}}"),
            score: 0.9,
            why: "brace balance".into(),
        });
    }
    // 2. hint lines: best matching line from RAG hints that extends current prefix
    let mut best: Option<(String, f32)> = None;
    for h in hints.iter().take(20) {
        for line in h.lines() {
            let t = line.trim();
            if t.len() < trimmed.len() + 2 || t.len() > 200 {
                continue;
            }
            if t.starts_with(trimmed) && !trimmed.is_empty() {
                let rest = t[trimmed.len()..].to_string();
                let score = 0.5 + (trimmed.len() as f32 / t.len() as f32) * 0.4;
                if best.as_ref().map(|b| score > b.1).unwrap_or(true) {
                    best = Some((rest, score));
                }
            }
        }
    }
    if let Some((rest, score)) = best {
        out.push(Suggestion { text: rest, score, why: "rag hint".into() });
    }
    // 3. keyword continuation for common patterns
    if trimmed.starts_with("if ") && !trimmed.contains('{') {
        out.push(Suggestion { text: " {".into(), score: 0.6, why: "pattern".into() });
    } else if trimmed.starts_with("for ") && !trimmed.contains('{') {
        out.push(Suggestion { text: " {".into(), score: 0.6, why: "pattern".into() });
    } else if trimmed.starts_with("fn ") && !trimmed.contains('{') && !trimmed.contains('}') {
        out.push(Suggestion { text: " {}".into(), score: 0.55, why: "pattern".into() });
    }
    // 4. fallback: complete word from suffix head
    if out.is_empty() {
        let head: String = suffix.lines().next().unwrap_or("").chars().take(60).collect();
        if !head.trim().is_empty() {
            out.push(Suggestion { text: head, score: 0.3, why: "suffix".into() });
        }
    }
    out.sort_by(|a, b| b.score.partial_cmp(&a.score).unwrap_or(std::cmp::Ordering::Equal));
    out
}

fn fnv(s: &str) -> u64 {
    let mut h: u64 = 0xcbf29ce484222325;
    for b in s.bytes() {
        h ^= b as u64;
        h = h.wrapping_mul(0x100000001b3);
    }
    h
}

#[derive(Debug, Deserialize)]
pub struct CompleteReq {
    pub prefix: String,
    #[serde(default)]
    pub suffix: String,
    #[serde(default)]
    pub file: String,
    #[serde(default = "d_top")]
    pub top_k: usize,
    #[serde(default)]
    pub hints: Vec<String>,
}

fn d_top() -> usize {
    3
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn brace_suggest() {
        let c = Completer::new();
        let (s, cached, ms) = c.complete("fn a() {", "", "a.rs", &[]);
        assert!(!s.is_empty());
        assert!(!cached);
        assert!(ms < 600);
        let (s2, cached2, _) = c.complete("fn a() {", "", "a.rs", &[]);
        assert!(cached2);
        assert_eq!(s.len(), s2.len());
    }
    #[test]
    fn hint_extends_prefix() {
        let c = Completer::new();
        let hints = vec!["    println!(\"hello quant\");".to_string()];
        let (s, _, _) = c.complete("    println!(\"hel", "", "a.rs", &hints);
        assert!(s.iter().any(|x| x.text.contains("lo")));
    }
}
