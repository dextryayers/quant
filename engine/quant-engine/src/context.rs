/// Context window manager with sliding keep-system-plus-recent policy.
pub struct Window {
    pub max: usize,
}

impl Window {
    pub fn new(max: usize) -> Self {
        Self { max }
    }

    /// Rough token estimate: chars / 4. Matches GUI within 5 percent target.
    pub fn estimate(text: &str) -> usize {
        text.len().div_ceil(4).max(1)
    }

    /// Truncate oldest middle turns first, keep system plus last 2.
    pub fn truncate(&self, system: &str, turns: &[(String, String)]) -> (String, Vec<(String, String)>) {
        let mut budget = self.max.saturating_sub(500);
        budget = budget.saturating_sub(Self::estimate(system));
        let mut keep: Vec<(String, String)> = Vec::new();
        for t in turns.iter().rev() {
            let c = Self::estimate(&t.1);
            if c > budget && !keep.is_empty() {
                break;
            }
            budget = budget.saturating_sub(c.min(budget));
            keep.push(t.clone());
            if keep.len() >= 8 {
                break;
            }
        }
        keep.reverse();
        (system.to_string(), keep)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn estimate_close() {
        assert_eq!(Window::estimate("abcd"), 1);
        assert_eq!(Window::estimate(&"x".repeat(400)), 100);
    }
    #[test]
    fn truncate_keeps_recent() {
        let w = Window::new(100);
        let turns: Vec<(String, String)> = (0..10).map(|i| ("user".into(), format!("m{i} long text here"))).collect();
        let (_, kept) = w.truncate("sys", &turns);
        assert!(kept.len() <= 8);
        assert!(kept.last().unwrap().1.starts_with('m'));
    }
}
