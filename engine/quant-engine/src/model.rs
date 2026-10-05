use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};
use std::time::Instant;

/// Preset catalog for V0.1.0. Hash pinned guidance, no network required to list.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Preset {
    pub id: String,
    pub hf_repo: String,
    pub filename: String,
    pub size_mb: f64,
    pub context: usize,
    pub family: String,
}

pub fn presets() -> Vec<Preset> {
    vec![
        Preset {
            id: "qwen2.5-coder-7b-q4_k_m".into(),
            hf_repo: "Qwen/Qwen2.5-Coder-7B-Instruct-GGUF".into(),
            filename: "qwen2.5-coder-7b-instruct-q4_k_m.gguf".into(),
            size_mb: 4710.0,
            context: 8192,
            family: "qwen".into(),
        },
        Preset {
            id: "llama-3.1-8b-q4_k_m".into(),
            hf_repo: "bartowski/Meta-Llama-3.1-8B-Instruct-GGUF".into(),
            filename: "Meta-Llama-3.1-8B-Instruct-Q4_K_M.gguf".into(),
            size_mb: 4920.0,
            context: 8192,
            family: "llama".into(),
        },
        Preset {
            id: "qwen2.5-coder-3b-q4_k_m".into(),
            hf_repo: "Qwen/Qwen2.5-Coder-3B-Instruct-GGUF".into(),
            filename: "qwen2.5-coder-3b-instruct-q4_k_m.gguf".into(),
            size_mb: 1900.0,
            context: 4096,
            family: "qwen".into(),
        },
    ]
}

#[derive(Debug, Clone, Serialize)]
pub struct ModelEntry {
    pub id: String,
    pub file: String,
    pub size_mb: Option<f64>,
    pub quantized: bool,
    pub context: usize,
    pub loaded: bool,
    pub ram_mb: Option<f64>,
}

#[derive(Debug)]
pub struct Manager {
    pub dir: PathBuf,
    pub active: Option<String>,
    pub loaded_at: Option<Instant>,
    pub idle_minutes: u64,
}

impl Manager {
    pub fn new(dir: PathBuf) -> Self {
        Self { dir, active: None, loaded_at: None, idle_minutes: 15 }
    }

    pub fn scan(&self) -> Vec<ModelEntry> {
        let mut out = Vec::new();
        if let Ok(rd) = std::fs::read_dir(&self.dir) {
            for e in rd.flatten() {
                let p = e.path();
                if p.extension().and_then(|x| x.to_str()) != Some("gguf") {
                    continue;
                }
                let size_mb = e.metadata().ok().map(|m| m.len() as f64 / 1024.0 / 1024.0);
                let id = p.file_stem().and_then(|x| x.to_str()).unwrap_or("unknown").to_string();
                let loaded = self.active.as_deref() == Some(id.as_str());
                out.push(ModelEntry {
                    id,
                    file: p.file_name().and_then(|x| x.to_str()).unwrap_or("").to_string(),
                    size_mb,
                    quantized: true,
                    context: 8192,
                    loaded,
                    ram_mb: size_mb.map(|s| estimate_resident(s, 8192)),
                });
            }
        }
        out.sort_by(|a, b| a.id.cmp(&b.id));
        out
    }

    pub fn file_for(&self, id: &str) -> Option<PathBuf> {
        for e in self.scan() {
            if e.id == id {
                return Some(self.dir.join(&e.file));
            }
        }
        // Allow preset id without file present
        presets().iter().find(|p| p.id == id).map(|p| self.dir.join(&p.filename))
    }

    pub fn load(&mut self, id: &str, context: usize) -> Result<f64, String> {
        let path = self.file_for(id).ok_or_else(|| format!("MODEL_NOT_FOUND: {id}"))?;
        let size_mb = std::fs::metadata(&path).map(|m| m.len() as f64 / 1024.0 / 1024.0).unwrap_or_else(|_| {
            presets().iter().find(|p| p.id == id).map(|p| p.size_mb).unwrap_or(4700.0)
        });
        let need = estimate_resident(size_mb, context);
        let (total_mb, free_mb) = ram_mb();
        if need > free_mb * 0.85 && std::fs::metadata(&path).is_ok() {
            let small = presets().iter().map(|p| p.id.clone()).collect::<Vec<_>>().join(", ");
            return Err(format!(
                "INSUFFICIENT_RAM: need {need:.0} MB but only {free_mb:.0} MB free of {total_mb:.0} MB. Try smaller quant or context 4096. Presets: {small}"
            ));
        }
        // Real llama.cpp mmap load lands here in V0.2. V0.1.0 marks resident with sidecar.
        self.active = Some(id.to_string());
        self.loaded_at = Some(Instant::now());
        let _ = std::fs::write(sidecar(&path), format!("{{\"id\":\"{id}\",\"context\":{context}}}"));
        Ok(need)
    }

    pub fn unload(&mut self) -> f64 {
        let freed = self.active.as_ref().and_then(|id| {
            self.file_for(id).and_then(|p| std::fs::metadata(p).ok().map(|m| m.len() as f64 / 1024.0 / 1024.0))
        }).unwrap_or(0.0);
        self.active = None;
        self.loaded_at = None;
        freed
    }

    pub fn evict_if_idle(&mut self) -> bool {
        if let Some(t) = self.loaded_at {
            if t.elapsed().as_secs() > self.idle_minutes * 60 {
                self.unload();
                return true;
            }
        }
        false
    }
}

pub fn estimate_resident(file_mb: f64, context: usize) -> f64 {
    // file mmap plus KV cache estimate: 8k ~ 0.5 GB for 7B, scales linearly.
    file_mb * 1.15 + (context as f64 / 8192.0) * 512.0
}

pub fn kv_mb(context: usize) -> f64 {
    (context as f64 / 8192.0) * 512.0
}

pub fn ram_mb() -> (f64, f64) {
    // sysinfo-based probe with safe fallback.
    let mut s = sysinfo::System::new();
    s.refresh_memory();
    let total = s.total_memory() as f64 / 1024.0;
    let free = s.available_memory() as f64 / 1024.0;
    if total > 0.0 {
        return (total, free);
    }
    (8192.0, 4096.0)
}

fn sidecar(path: &Path) -> PathBuf {
    let mut s = path.as_os_str().to_owned();
    s.push(".json");
    PathBuf::from(s)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn resident_math_sane() {
        let r = estimate_resident(4710.0, 8192);
        assert!(r > 5000.0 && r < 6500.0, "got {r}");
        assert!((kv_mb(8192) - 512.0).abs() < 1.0);
        assert!((kv_mb(4096) - 256.0).abs() < 1.0);
    }

    #[test]
    fn scan_empty_ok() {
        let m = Manager::new(PathBuf::from("./models"));
        let _ = m.scan();
    }
}
