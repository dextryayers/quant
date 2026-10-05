use std::collections::HashMap;
use std::sync::{Arc, Mutex};

/// Deterministic hash embeddings for V0.1.0 CPU RAG without model download.
/// 128 dim, L2 normalized, cached per chunk hash. Real tiny model plugs in later.
#[derive(Clone)]
pub struct Embed {
    cache: Arc<Mutex<HashMap<String, Vec<f32>>>>,
    pub loaded: bool,
    pub dim: usize,
}

impl Embed {
    pub fn new() -> Self {
        Self { cache: Arc::new(Mutex::new(HashMap::new())), loaded: false, dim: 128 }
    }

    pub fn ensure_loaded(&mut self) {
        self.loaded = true;
    }

    pub fn unload_pressure(&mut self) {
        // Embeddings unload first under RAM pressure.
        self.loaded = false;
        self.cache.lock().unwrap().clear();
    }

    pub fn vec(&self, text: &str) -> Vec<f32> {
        let key = format!("{:x}", hash(text));
        if let Some(v) = self.cache.lock().unwrap().get(&key) {
            return v.clone();
        }
        let v = hash_embed(text, self.dim);
        self.cache.lock().unwrap().insert(key, v.clone());
        v
    }

    pub fn batch(&self, texts: &[String]) -> Vec<Vec<f32>> {
        texts.iter().map(|t| self.vec(t)).collect()
    }

    pub fn cosine(a: &[f32], b: &[f32]) -> f32 {
        let mut dot = 0.0;
        let mut na = 0.0;
        let mut nb = 0.0;
        for (x, y) in a.iter().zip(b.iter()) {
            dot += x * y;
            na += x * x;
            nb += y * y;
        }
        dot / (na.sqrt() * nb.sqrt()).max(1e-6)
    }
}

fn hash(s: &str) -> u64 {
    // FNV-1a 64
    let mut h: u64 = 0xcbf29ce484222325;
    for b in s.bytes() {
        h ^= b as u64;
        h = h.wrapping_mul(0x100000001b3);
    }
    h
}

fn hash_embed(text: &str, dim: usize) -> Vec<f32> {
    let mut v = vec![0.0f32; dim];
    for (i, tok) in text.split_whitespace().enumerate() {
        let h = hash(tok) as usize % dim;
        v[h] += 1.0 / (1.0 + (i % 8) as f32 * 0.1);
    }
    // char trigrams add robustness for code
    let chars: Vec<char> = text.chars().collect();
    for w in chars.windows(3) {
        let s: String = w.iter().collect();
        let h = hash(&s) as usize % dim;
        v[h] += 0.3;
    }
    let n = v.iter().map(|x| x * x).sum::<f32>().sqrt().max(1e-6);
    for x in v.iter_mut() {
        *x /= n;
    }
    v
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn normalized_and_cached() {
        let e = Embed::new();
        let a = e.vec("fn main rust");
        let b = e.vec("fn main rust");
        assert_eq!(a, b);
        let n: f32 = a.iter().map(|x| x * x).sum::<f32>().sqrt();
        assert!((n - 1.0).abs() < 0.01);
        assert!(Embed::cosine(&a, &e.vec("totally different pizza")) < 0.9);
    }
}
