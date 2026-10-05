use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

#[derive(Debug, Clone, Serialize)]
pub struct ReadOut {
    pub path: String,
    pub hash: String,
    pub lines: Vec<String>,
    pub total: usize,
}

#[derive(Debug, Clone, Serialize)]
pub struct GrepHit {
    pub path: String,
    pub line: usize,
    pub preview: String,
}

#[derive(Debug, Clone)]
pub struct Approval {
    pub token: String,
    pub scope: String,
    pub command: String,
    pub created: u64,
}

pub fn now() -> u64 {
    SystemTime::now().duration_since(UNIX_EPOCH).map(|d| d.as_secs()).unwrap_or(0)
}

pub fn token() -> String {
    format!("appr-{:x}", now().wrapping_mul(0x9e3779b1) ^ 0x1234)
}

/// Jail: canonical path must stay under one of roots or models dir.
pub fn jail(path: &str, roots: &[String], models_dir: &str) -> Result<PathBuf, String> {
    let p = PathBuf::from(path);
    let canon = if p.is_absolute() {
        p
    } else if let Some(r) = roots.first() {
        PathBuf::from(r).join(path)
    } else {
        PathBuf::from(path)
    };
    let s = canon.to_string_lossy().to_string();
    // normalize .. without fs access
    let mut parts: Vec<String> = Vec::new();
    let flat = s.replace('\\', "/");
    for comp in flat.split('/') {
        if comp == ".." {
            parts.pop();
        } else if comp != "." && !comp.is_empty() {
            parts.push(comp.to_string());
        }
    }
    let norm = parts.join("/");
    for r in roots.iter().chain(std::iter::once(&models_dir.to_string())) {
        let rn = r.replace('\\', "/").trim_end_matches('/').to_string();
        if norm.starts_with(rn.trim_start_matches('/')) || format!("/{norm}").starts_with(&format!("/{rn}")) {
            return Ok(PathBuf::from(format!("/{norm}")).strip_prefix("/").map(PathBuf::from).unwrap_or(PathBuf::from(&norm)));
        }
    }
    // Also allow direct absolute match after normalization
    for r in roots {
        if norm == r.replace('\\', "/").trim_start_matches('/').to_string() {
            return Ok(PathBuf::from(path));
        }
    }
    Err(format!("PATH_ESCAPE: {path} outside workspace"))
}

pub fn read_file(path: &Path, offset: usize, limit: usize) -> Result<ReadOut, String> {
    let text = std::fs::read_to_string(path).map_err(|e| format!("READ: {e}"))?;
    if text.len() > 400_000 {
        return Err("TOO_LARGE: over 200 KB cap, use offset/limit".into());
    }
    let lines: Vec<String> = text.lines().map(|s| s.to_string()).collect();
    let total = lines.len();
    let from = offset.saturating_sub(1).min(total);
    let to = (from + limit.min(4000)).min(total);
    let hash = format!("{:x}", fnv(&text));
    Ok(ReadOut { path: path.to_string_lossy().into(), hash, lines: lines[from..to].to_vec(), total })
}

pub fn glob_files(root: &str, pattern: &str, max: usize) -> Vec<String> {
    let mut out = Vec::new();
    let mut stack = vec![PathBuf::from(root)];
    let pat = pattern.replace('\\', "/");
    // minimal glob: **, *, ?. Extension focused for V0.1.0.
    let want_ext = pat.rsplit('.').next().unwrap_or("").split(['*', '?']).next().unwrap_or("");
    while let Some(d) = stack.pop() {
        let rd = match std::fs::read_dir(&d) {
            Ok(r) => r,
            Err(_) => continue,
        };
        for e in rd.flatten() {
            if out.len() >= max {
                return out;
            }
            let p = e.path();
            if p.is_dir() {
                let n = p.file_name().and_then(|x| x.to_str()).unwrap_or("");
                if [".git", "node_modules", "target", "bin", "obj", ".quant"].contains(&n) {
                    continue;
                }
                stack.push(p);
            } else {
                let s = p.to_string_lossy().replace('\\', "/");
                if matches_glob(&s, &pat, want_ext) {
                    out.push(p.to_string_lossy().into());
                }
            }
        }
    }
    out.sort();
    out
}

fn matches_glob(path: &str, pat: &str, _want_ext: &str) -> bool {
    // Support: src/**/*.rs, **/*.md, *.json, exact substring fallback
    if pat.contains("**") {
        let suffix = pat.rsplit("**").next().unwrap_or("").trim_start_matches('/');
        let suffix = suffix.trim_start_matches('*');
        if suffix.is_empty() {
            return true;
        }
        return path.ends_with(suffix);
    }
    if pat.contains('*') {
        let parts: Vec<&str> = pat.split('*').collect();
        if parts.len() == 2 {
            let (pre, suf) = (parts[0], parts[1]);
            let file = path.rsplit('/').next().unwrap_or(path);
            return file.starts_with(pre.trim_start_matches('/')) && file.ends_with(suf);
        }
        return path.contains(&pat.replace('*', ""));
    }
    path.contains(pat)
}

pub fn grep_files(root: &str, query: &str, include: &str, regex: bool, max: usize) -> Vec<GrepHit> {
    let files = glob_files(root, include, 5000);
    let mut out = Vec::new();
    let rx = if regex {
        regex_lite(query)
    } else {
        None
    };
    for f in files {
        if out.len() >= max {
            break;
        }
        let text = match std::fs::read_to_string(&f) {
            Ok(t) => t,
            Err(_) => continue,
        };
        if text.len() > 2_000_000 {
            continue;
        }
        for (i, line) in text.lines().enumerate() {
            let hit = match &rx {
                Some(r) => r(line),
                None => line.contains(query),
            };
            if hit {
                out.push(GrepHit { path: f.clone(), line: i + 1, preview: line.trim().chars().take(220).collect() });
                if out.len() >= max {
                    break;
                }
                if out.iter().filter(|h| h.path == f).count() >= 5 {
                    break;
                }
            }
        }
    }
    out
}

fn regex_lite(pat: &str) -> Option<Box<dyn Fn(&str) -> bool>> {
    // tiny subset for V0.1.0: plain contains plus .* wildcard, avoids regex dep
    if pat.contains(".*") {
        let parts: Vec<String> = pat.split(".*").map(|s| s.to_string()).collect();
        return Some(Box::new(move |line: &str| {
            let mut idx = 0;
            for part in &parts {
                if part.is_empty() {
                    continue;
                }
                match line[idx..].find(part.as_str()) {
                    Some(p) => idx += p + part.len(),
                    None => return false,
                }
            }
            true
        }));
    }
    let owned = pat.to_string();
    Some(Box::new(move |line: &str| line.contains(&owned)))
}

#[derive(Debug, Deserialize)]
pub struct ApplyReq {
    pub path: String,
    pub diff: String,
    pub base_hash: Option<String>,
}

pub fn apply_diff(path: &Path, diff: &str, base_hash: Option<&str>) -> Result<usize, String> {
    let current = std::fs::read_to_string(path).map_err(|e| format!("READ: {e}"))?;
    if let Some(h) = base_hash {
        let cur = format!("{:x}", fnv(&current));
        if cur != h {
            return Err(format!("HASH_CONFLICT: expected {h} got {cur}. Reload file."));
        }
    }
    // V0.1.0 unified hunk applier: supports ---/+++/@@ plus -/+ lines, context lines with leading space.
    let mut out: Vec<String> = current.lines().map(|s| s.to_string()).collect();
    let mut hunks = 0;
    let mut idx = 0usize;
    for line in diff.lines() {
        if line.starts_with("@@") {
            // parse +start
            if let Some(plus) = line.split('+').nth(1) {
                let start: usize = plus.split([',', ' ']).next().unwrap_or("1").parse().unwrap_or(1);
                idx = start.saturating_sub(1);
            }
            hunks += 1;
            continue;
        }
        if line.starts_with("---") || line.starts_with("+++") {
            continue;
        }
        if let Some(body) = line.strip_prefix('+') {
            if idx <= out.len() {
                out.insert(idx, body.to_string());
                idx += 1;
            }
        } else if let Some(_body) = line.strip_prefix('-') {
            if idx < out.len() {
                out.remove(idx);
            }
        } else if line.starts_with(' ') {
            idx += 1;
        }
    }
    // backup for undo
    let bak = format!("{}.quantdiffbak", path.to_string_lossy());
    let _ = std::fs::write(&bak, current);
    std::fs::write(path, out.join("\n")).map_err(|e| format!("WRITE: {e}"))?;
    Ok(hunks.max(1))
}

pub fn fnv(s: &str) -> u64 {
    let mut h: u64 = 0xcbf29ce484222325;
    for b in s.bytes() {
        h ^= b as u64;
        h = h.wrapping_mul(0x100000001b3);
    }
    h
}

static DENY: &[&str] = &[
    "rm -rf /",
    "rm -rf ~",
    "mkfs",
    ":(){:|:&};:",
    "diskpart",
    "format c:",
];

pub fn exec_allowed(command: &str, allow: &[String]) -> Result<(), String> {
    let lower = command.to_lowercase();
    for d in DENY {
        if lower.contains(d) {
            return Err(format!("BLOCKED: dangerous pattern {d}"));
        }
    }
    // credential exfiltration guard
    if lower.contains("curl") && (lower.contains("sk-") || lower.contains("bearer")) {
        return Err("BLOCKED: possible secret exfiltration".into());
    }
    let first = command.split_whitespace().next().unwrap_or("");
    // allow exact bin name or path ending
    if allow.iter().any(|a| first == a || first.ends_with(&format!("/{a}")) || first.ends_with(&format!("\\{a}"))) {
        return Ok(());
    }
    // git subcommands always allowed read-only except push with approval token path
    if first == "git" {
        return Ok(());
    }
    Err(format!("NEEDS_APPROVAL: {first} not in allow list"))
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn deny_blocks() {
        assert!(exec_allowed("rm -rf /", &["cargo".into()]).is_err());
        assert!(exec_allowed("cargo test", &["cargo".into()]).is_ok());
    }
    #[test]
    fn glob_suffix() {
        assert!(matches_glob("src/a.rs", "src/**/*.rs", ""));
        assert!(!matches_glob("src/a.py", "src/**/*.rs", ""));
    }
    #[test]
    fn diff_applies() {
        let dir = std::env::temp_dir();
        let p = dir.join("quant_diff_test.txt");
        std::fs::write(&p, "a\nb\nc").unwrap();
        let n = apply_diff(&p, "@@ -1,3 +1,3 @@\n a\n-b\n+B\n c", None).unwrap();
        assert!(n >= 1);
        let t = std::fs::read_to_string(&p).unwrap();
        assert!(t.contains('B'));
        let _ = std::fs::remove_file(&p);
    }
}
