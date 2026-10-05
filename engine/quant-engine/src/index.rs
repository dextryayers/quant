use serde::{Deserialize, Serialize};
use std::path::PathBuf;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Chunk {
    pub id: String,
    pub path: String,
    pub start_line: usize,
    pub end_line: usize,
    pub hash: String,
    pub text: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Symbol {
    pub path: String,
    pub name: String,
    pub kind: String,
    pub line: usize,
    pub signature: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FileRow {
    pub path: String,
    pub hash: String,
    pub size: u64,
    pub lang: String,
}

pub fn chunk_text(path: &str, text: &str) -> Vec<Chunk> {
    // 800 tokens ~ 3200 chars with 120 overlap for code. Docs smaller via caller.
    chunk_by(text, path, 3200, 480)
}

pub fn chunk_by(text: &str, path: &str, size: usize, overlap: usize) -> Vec<Chunk> {
    let lines: Vec<&str> = text.lines().collect();
    let mut out = Vec::new();
    let mut start = 0usize;
    let mut buf = String::new();
    let mut start_line = 1usize;
    for (i, l) in lines.iter().enumerate() {
        buf.push_str(l);
        buf.push('\n');
        if buf.len() >= size || i == lines.len() - 1 {
            let id = format!("{:x}", fnv(&buf));
            out.push(Chunk {
                id: id.clone(),
                path: path.to_string(),
                start_line,
                end_line: i + 1,
                hash: id,
                text: buf.clone(),
            });
            // overlap: rewind by ~overlap chars worth of lines
            let keep = buf.len().saturating_sub(overlap);
            let cut = buf.len() - keep;
            buf = buf[cut..].to_string();
            start = i + 1;
            start_line = start + 1 - buf.lines().count();
            if out.len() >= 200 {
                break;
            }
        }
    }
    let _ = start;
    out
}

pub fn symbols_for(path: &str, text: &str) -> Vec<Symbol> {
    let mut out = Vec::new();
    for (i, l) in text.lines().enumerate() {
        let t = l.trim();
        if let Some(name) = parse_symbol(t) {
            out.push(Symbol {
                path: path.to_string(),
                name: name.1,
                kind: name.0,
                line: i + 1,
                signature: t.to_string(),
            });
            if out.len() >= 300 {
                break;
            }
        }
    }
    out
}

fn parse_symbol(t: &str) -> Option<(String, String)> {
    // class/struct/interface/enum, fn/func/def, method-ish
    for kw in ["class ", "struct ", "interface ", "enum "] {
        let rest: Option<String> = t
            .strip_prefix(kw)
            .map(|s| s.to_string())
            .or_else(|| strip_pub(t, kw));
        if let Some(rest) = rest {
            let name: String = rest.split(|c: char| !c.is_alphanumeric() && c != '_').next().unwrap_or("").into();
            if !name.is_empty() {
                let kind = kw.trim().to_string();
                return Some((kind, name));
            }
        }
    }
    for kw in ["fn ", "func ", "function ", "def "] {
        if let Some(rest) = t.strip_prefix(kw) {
            let name: String = rest.split('(').next().unwrap_or("").trim().into();
            if !name.is_empty() {
                return Some(("function".into(), name));
            }
        }
    }
    None
}

fn strip_pub(t: &str, kw: &str) -> Option<String> {
    t.strip_prefix("pub ").and_then(|r| r.strip_prefix(kw)).map(|s| s.to_string())
}

fn fnv(s: &str) -> u64 {
    let mut h: u64 = 0xcbf29ce484222325;
    for b in s.bytes() {
        h ^= b as u64;
        h = h.wrapping_mul(0x100000001b3);
    }
    h
}

pub fn index_dir() -> PathBuf {
    PathBuf::from("./index")
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn chunk_overlap_sane() {
        let text = (0..500).map(|i| format!("line {i}")).collect::<Vec<_>>().join("\n");
        let c = chunk_by(&text, "a.rs", 1000, 200);
        assert!(c.len() >= 2);
        assert!(c[0].start_line == 1);
    }
    #[test]
    fn symbols_found() {
        let s = symbols_for("a.rs", "fn main() {}\nclass Foo {}");
        assert!(s.iter().any(|x| x.name == "main"));
        assert!(s.iter().any(|x| x.name == "Foo"));
    }
}
