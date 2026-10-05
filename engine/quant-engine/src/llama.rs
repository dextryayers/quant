use llama_cpp_2::{
    context::params::LlamaContextParams,
    llama_backend::LlamaBackend,
    llama_batch::LlamaBatch,
    model::{params::LlamaModelParams, LlamaModel},
    sampling::LlamaSampler,
};
use std::num::NonZeroU32;
use std::path::Path;
use std::sync::{
    Arc, Mutex,
    atomic::{AtomicBool, Ordering},
};
use tokio::sync::mpsc;

/// Sampling controls mapped from our presets.
#[derive(Debug, Clone)]
pub struct GenParams {
    pub temp: f32,
    pub top_k: i32,
    pub top_p: f32,
    pub repeat: f32,
    pub max_tokens: usize,
    pub stops: Vec<String>,
}

impl GenParams {
    pub fn chat() -> Self {
        Self { temp: 0.7, top_k: 40, top_p: 0.9, repeat: 1.1, max_tokens: 1024, stops: vec![] }
    }
    pub fn precise() -> Self {
        Self {
            temp: 0.2, top_k: 40, top_p: 0.9, repeat: 1.05, max_tokens: 2048,
            stops: vec!["```".into()],
        }
    }
}

/// Real GGUF runner. One model resident, requests serialized.
/// Heavy work runs on spawn_blocking threads, never on the Axum runtime.
pub struct Runner {
    inner: Mutex<Inner>,
}

struct Inner {
    #[allow(dead_code)]
    backend: Option<Arc<LlamaBackend>>,
    model: Option<LlamaModel>,
    id: Option<String>,
    n_ctx: usize,
}

impl Runner {
    pub fn new() -> Self {
        Self {
            inner: Mutex::new(Inner { backend: None, model: None, id: None, n_ctx: 8192 }),
        }
    }

    pub fn is_loaded(&self) -> bool {
        self.inner.lock().unwrap().model.is_some()
    }

    pub fn active_id(&self) -> Option<String> {
        self.inner.lock().unwrap().id.clone()
    }

    /// Load a real .gguf with mmap. Returns resident MB estimate.
    pub fn load(&self, path: &Path, id: &str, n_ctx: usize) -> Result<f64, String> {
        let backend = LlamaBackend::init().map_err(|e| format!("BACKEND: {e:?}"))?;
        let backend = Arc::new(backend);
        let params = LlamaModelParams::default()
            .with_n_gpu_layers(0)
            .with_use_mmap(true)
            .with_use_mlock(false);
        let t0 = std::time::Instant::now();
        let model = LlamaModel::load_from_file(&backend, path, &params)
            .map_err(|e| format!("LOAD {path:?}: {e:?}"))?;
        tracing::info!("gguf loaded in {}ms: {path:?}", t0.elapsed().as_millis());
        let size_mb =
            std::fs::metadata(path).map(|m| m.len() as f64 / 1024.0 / 1024.0).unwrap_or(0.0);
        let mut inner = self.inner.lock().unwrap();
        inner.backend = Some(backend);
        inner.model = Some(model);
        inner.id = Some(id.to_string());
        inner.n_ctx = n_ctx;
        Ok(crate::model::estimate_resident(size_mb, n_ctx))
    }

    pub fn unload(&self) {
        let mut inner = self.inner.lock().unwrap();
        inner.model = None;
        inner.id = None;
        inner.backend = None;
    }

    /// Blocking generation. Streams pieces through tx when present.
    pub fn generate(
        &self,
        prompt: &str,
        gen: &GenParams,
        cancel: &AtomicBool,
        tx: Option<mpsc::Sender<String>>,
    ) -> Result<String, String> {
        let inner = self.inner.lock().unwrap();
        let model = inner.model.as_ref().ok_or_else(|| "no model loaded".to_string())?;
        let n_ctx = inner.n_ctx.min(32768) as u32;
        // Backend handle outlives every context created below.
        let backend = inner.backend.clone().ok_or_else(|| "no backend".to_string())?;

        let ctx_params = LlamaContextParams::default()
            .with_n_ctx(NonZeroU32::new(n_ctx))
            // Physical batch must fit inside the context, or decode fails.
            .with_n_batch(n_ctx.min(512));
        let mut ctx = model
            .new_context(&backend, ctx_params)
            .map_err(|e| format!("CTX: {e:?}"))?;

        let vocab = model.vocab();
        let mut tokens = vocab.tokenize(prompt.as_bytes(), vocab.should_add_bos(), false);
        let room = n_ctx as usize;
        let keep = room.saturating_sub(gen.max_tokens + 64).max(64);
        if tokens.len() > keep {
            tokens = tokens[tokens.len() - keep..].to_vec();
        }
        if tokens.is_empty() {
            return Err("prompt tokenized to nothing".into());
        }

        // Prompt eval in 512-token batches.
        let mut n_past: i32 = 0;
        let mut i = 0;
        while i < tokens.len() {
            if cancel.load(Ordering::Relaxed) {
                return Err("cancelled".into());
            }
            let mut batch = LlamaBatch::new(512, 1);
            let mut n = 0;
            while i < tokens.len() && n < 512 {
                let logits = i + 1 == tokens.len();
                batch.add(tokens[i], n_past + n, &[0], logits).map_err(|e| format!("BATCH: {e:?}"))?;
                i += 1;
                n += 1;
            }
            ctx.decode(&mut batch).map_err(|e| format!("DECODE: {e:?}"))?;
            n_past += n;
        }

        let seed: u32 = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map(|d| d.as_nanos() as u32)
            .unwrap_or(42);
        let mut sampler = if gen.temp <= 0.0 {
            LlamaSampler::chain_simple([LlamaSampler::greedy()])
        } else if std::env::var("QUANT_SIMPLE_SAMPLER").is_ok() {
            LlamaSampler::chain_simple([LlamaSampler::greedy()])
        } else {
            // NOTE: dist samples the distribution and must come last.
            LlamaSampler::chain_simple([
                LlamaSampler::penalties(vocab.n_tokens(), 64, gen.repeat, 0.0, 0.0),
                LlamaSampler::temp(gen.temp),
                LlamaSampler::top_k(gen.top_k),
                LlamaSampler::top_p(gen.top_p, 1),
                LlamaSampler::dist(seed),
            ])
        };

        let mut out = String::new();
        for _ in 0..gen.max_tokens {
            if cancel.load(Ordering::Relaxed) {
                break;
            }
            let tok = sampler.sample(&ctx, -1);
            if vocab.is_eog(tok) {
                break;
            }
            let bytes = vocab.token_to_piece(tok, false, None);
            let piece = String::from_utf8_lossy(&bytes).to_string();
            out.push_str(&piece);
            if let Some(tx) = &tx {
                let _ = tx.blocking_send(piece);
            }
            if gen.stops.iter().any(|s| !s.is_empty() && out.contains(s)) {
                for s in &gen.stops {
                    if !s.is_empty() {
                        if let Some(pos) = out.find(s) {
                            out.truncate(pos);
                        }
                    }
                }
                break;
            }
            let mut batch = LlamaBatch::new(1, 1);
            batch.add(tok, n_past, &[0], true).map_err(|e| format!("BATCH: {e:?}"))?;
            n_past += 1;
            ctx.decode(&mut batch).map_err(|e| format!("DECODE: {e:?}"))?;
        }
        Ok(out)
    }
}
