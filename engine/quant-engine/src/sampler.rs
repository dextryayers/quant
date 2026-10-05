use serde::{Deserialize, Serialize};

/// Sampler presets tuned for CPU 7B Q4. Deterministic Edit, balanced Chat.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Sampler {
    pub temp: f32,
    pub top_p: f32,
    pub top_k: u32,
    pub repeat: f32,
    pub max_tokens: usize,
    pub stops: Vec<String>,
}

impl Sampler {
    pub fn chat() -> Self {
        Self { temp: 0.7, top_p: 0.9, top_k: 40, repeat: 1.1, max_tokens: 1024, stops: vec![] }
    }
    pub fn edit() -> Self {
        Self {
            temp: 0.2, top_p: 0.9, top_k: 40, repeat: 1.05, max_tokens: 2048,
            stops: vec!["```".into()],
        }
    }
    pub fn complete() -> Self {
        Self { temp: 0.3, top_p: 0.9, top_k: 40, repeat: 1.05, max_tokens: 96, stops: vec!["\n\n".into()] }
    }
}

/// Minimal chat templates per family. V0.1.0 renders prompt text, V0.2 feeds llama.cpp.
pub fn render(family: &str, system: &str, turns: &[(String, String)]) -> String {
    let mut out = String::new();
    match family {
        "qwen" => {
            if !system.is_empty() {
                out.push_str("<|im_start|>system\n");
                out.push_str(system);
                out.push_str("<|im_end|>\n");
            }
            for (role, text) in turns {
                out.push_str(&format!("<|im_start|>{role}\n{text}<|im_end|>\n"));
            }
            out.push_str("<|im_start|>assistant\n");
        }
        "llama" => {
            out.push_str("<|begin_of_text|>");
            if !system.is_empty() {
                out.push_str(&format!("<|start_header_id|>system<|end_header_id|>\n\n{system}<|eot_id|>"));
            }
            for (role, text) in turns {
                out.push_str(&format!("<|start_header_id|>{role}<|end_header_id|>\n\n{text}<|eot_id|>"));
            }
            out.push_str("<|start_header_id|>assistant<|end_header_id|>\n\n");
        }
        _ => {
            if !system.is_empty() {
                out.push_str(&format!("System: {system}\n"));
            }
            for (role, text) in turns {
                out.push_str(&format!("{role}: {text}\n"));
            }
            out.push_str("assistant:");
        }
    }
    out
}

/// Streaming pacer: split into word chunks for SSE. Real tokens replace this in V0.2.
pub fn pace(text: &str) -> Vec<String> {
    text.split_inclusive(' ').map(|w| w.to_string()).collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn qwen_template_has_markers() {
        let p = render("qwen", "sys", &[("user".into(), "hi".into())]);
        assert!(p.contains("<|im_start|>"));
    }
    #[test]
    fn llama_template_has_markers() {
        let p = render("llama", "sys", &[("user".into(), "hi".into())]);
        assert!(p.contains("<|start_header_id|>"));
    }
}
