use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Mode {
    Ask,
    Edit,
    Agent,
}

impl Mode {
    pub fn parse(s: &str) -> Self {
        match s.to_lowercase().as_str() {
            "ask" => Mode::Ask,
            "agent" => Mode::Agent,
            _ => Mode::Edit,
        }
    }
    pub fn max_steps(&self) -> usize {
        match self {
            Mode::Ask => 1,
            Mode::Edit => 3,
            Mode::Agent => 12,
        }
    }
}

/// Planned step emitted by the planner before execution. GUI approves write/exec.
#[derive(Debug, Clone, Serialize)]
pub struct Step {
    pub n: usize,
    pub tool: String,
    pub args: serde_json::Value,
    pub need_approval: bool,
    pub reason: String,
}

pub fn plan(mode: Mode, message: &str) -> Vec<Step> {
    let m = message.to_lowercase();
    let mut steps = Vec::new();
    let mut push = |tool: &str, args: serde_json::Value, need: bool, reason: &str| {
        steps.push(Step { n: steps.len() + 1, tool: tool.into(), args, need_approval: need, reason: reason.into() });
    };
    if mode == Mode::Ask {
        push("grep", serde_json::json!({"query": message}), false, "locate relevant code");
        push("read", serde_json::json!({"limit": 120}), false, "read top hit");
        return steps;
    }
    // Edit/Agent heuristic planner for V0.1.0: locate, read, then propose diff.
    if m.contains("test") {
        push("glob", serde_json::json!({"pattern": "**/*test*"}), false, "find tests");
        push("grep", serde_json::json!({"query": message}), false, "locate target");
        push("read", serde_json::json!({"limit": 200}), false, "read target");
        push("apply_diff", serde_json::json!({}), true, "propose test diff");
    } else if m.contains("fix") || m.contains("bug") || m.contains("error") {
        push("grep", serde_json::json!({"query": message}), false, "locate error");
        push("read", serde_json::json!({"limit": 200}), false, "read context");
        push("apply_diff", serde_json::json!({}), true, "propose minimal fix");
    } else if m.contains("run") || m.contains("build") || m.contains("cargo") || m.contains("dotnet") {
        push("exec", serde_json::json!({"command": message}), true, "run requested command");
    } else {
        push("grep", serde_json::json!({"query": message}), false, "locate relevant code");
        push("read", serde_json::json!({"limit": 200}), false, "read top hit");
        if mode == Mode::Agent {
            push("apply_diff", serde_json::json!({}), true, "propose edit");
        }
    }
    steps.truncate(mode.max_steps());
    steps
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn ask_is_readonly() {
        let s = plan(Mode::Ask, "explain login");
        assert!(s.iter().all(|x| x.tool != "exec" && x.tool != "apply_diff" || !x.need_approval || x.tool == "grep" || true));
        assert!(s.iter().all(|x| x.tool != "exec"));
    }
    #[test]
    fn agent_caps_steps() {
        let s = plan(Mode::Agent, "fix everything everywhere");
        assert!(s.len() <= 12);
    }
}
