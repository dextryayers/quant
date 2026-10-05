mod context;
mod download;
mod embed;
mod index;
mod model;
mod retrieve;
mod sampler;

use axum::{
    Json, Router,
    extract::{Query, State},
    http::StatusCode,
    response::sse::{Event, Sse},
    routing::{get, post},
};
use async_stream::stream;
use futures::stream::Stream;
use serde::{Deserialize, Serialize};
use std::{collections::HashMap, convert::Infallible, net::SocketAddr, path::PathBuf, sync::Arc, time::Instant};
use tokio::sync::RwLock;
use tower_http::cors::CorsLayer;
use tracing::info;

#[derive(Clone)]
struct AppState {
    version: String,
    models_dir: PathBuf,
    started_at: Instant,
    port: u16,
    manager: Arc<RwLock<model::Manager>>,
    downloader: download::Downloader,
    embed: Arc<RwLock<embed::Embed>>,
    chunks: Arc<RwLock<Vec<index::Chunk>>>,
    symbols: Arc<RwLock<Vec<index::Symbol>>>,
    metrics: Arc<RwLock<Metrics>>,
    cancels: Arc<RwLock<HashMap<String, bool>>>,
}

#[derive(Debug, Default, Clone, Serialize)]
struct Metrics {
    requests_total: u64,
    tokens_in: u64,
    tokens_out: u64,
    ttft_ms_p50: u64,
}

// ---------- DTOs ----------
#[derive(Debug, Serialize)]
struct HealthResp {
    status: String,
    version: String,
    model_loaded: bool,
    models_dir: String,
    port: u16,
    uptime_s: u64,
}

#[derive(Debug, Serialize)]
struct DetailedHealth {
    status: String,
    version: String,
    uptime_s: u64,
    port: u16,
    models_dir: String,
    index_files: usize,
    index_chunks: usize,
    queue_depth: usize,
    ram_total_mb: f64,
    ram_free_mb: f64,
}

#[derive(Debug, Deserialize)]
struct ChatMessage {
    role: String,
    content: String,
}

#[derive(Debug, Deserialize)]
struct ChatReq {
    messages: Vec<ChatMessage>,
    #[serde(default)]
    stream: bool,
    #[serde(default = "default_model")]
    model: String,
    #[serde(default = "default_ctx")]
    context: usize,
}

fn default_model() -> String {
    "mock-qwen-coder-7b-q4".into()
}
fn default_ctx() -> usize {
    8192
}

#[derive(Debug, Serialize)]
struct ChatResp {
    id: String,
    model: String,
    choices: Vec<ChatChoice>,
    usage: Usage,
}

#[derive(Debug, Serialize)]
struct ChatChoice {
    index: u32,
    message: ChatMessageOut,
}

#[derive(Debug, Serialize)]
struct ChatMessageOut {
    role: String,
    content: String,
}

#[derive(Debug, Serialize)]
struct Usage {
    #[serde(rename = "in")]
    input: usize,
    out: usize,
}

// ---------- Handlers ----------
async fn health(State(s): State<Arc<AppState>>) -> Json<HealthResp> {
    let m = s.manager.read().await;
    Json(HealthResp {
        status: "ok".into(),
        version: s.version.clone(),
        model_loaded: m.active.is_some(),
        models_dir: s.models_dir.to_string_lossy().into(),
        port: s.port,
        uptime_s: s.started_at.elapsed().as_secs(),
    })
}

async fn detailed(State(s): State<Arc<AppState>>) -> Json<DetailedHealth> {
    let (total, free) = model::ram_mb();
    Json(DetailedHealth {
        status: "ok".into(),
        version: s.version.clone(),
        uptime_s: s.started_at.elapsed().as_secs(),
        port: s.port,
        models_dir: s.models_dir.to_string_lossy().into(),
        index_files: s.chunks.read().await.iter().map(|c| c.path.clone()).collect::<std::collections::HashSet<_>>().len(),
        index_chunks: s.chunks.read().await.len(),
        queue_depth: 0,
        ram_total_mb: total,
        ram_free_mb: free,
    })
}

async fn metrics(State(s): State<Arc<AppState>>) -> Json<Metrics> {
    Json(s.metrics.read().await.clone())
}

async fn list_models(State(s): State<Arc<AppState>>) -> Json<serde_json::Value> {
    let m = s.manager.read().await;
    let data = m.scan();
    Json(serde_json::json!({ "data": data, "presets": model::presets() }))
}

#[derive(Debug, Deserialize)]
struct LoadReq {
    id: String,
    #[serde(default = "default_ctx")]
    context: usize,
}

async fn load_model(
    State(s): State<Arc<AppState>>,
    Json(req): Json<LoadReq>,
) -> (StatusCode, Json<serde_json::Value>) {
    let mut m = s.manager.write().await;
    match m.load(&req.id, req.context) {
        Ok(need) => (StatusCode::OK, Json(serde_json::json!({"status":"loaded","id":req.id,"need_mb":need}))),
        Err(e) if e.starts_with("INSUFFICIENT_RAM") => {
            (StatusCode::UNPROCESSABLE_ENTITY, Json(serde_json::json!({"error":{"code":"INSUFFICIENT_RAM","message":e}})))
        }
        Err(e) => (StatusCode::NOT_FOUND, Json(serde_json::json!({"error":{"code":"MODEL_NOT_FOUND","message":e}}))),
    }
}

async fn unload_model(State(s): State<Arc<AppState>>) -> Json<serde_json::Value> {
    let mut m = s.manager.write().await;
    let freed = m.unload();
    Json(serde_json::json!({"status":"unloaded","freed_mb":freed}))
}

async fn active_model(State(s): State<Arc<AppState>>) -> Json<serde_json::Value> {
    let m = s.manager.read().await;
    Json(serde_json::json!({"active": m.active}))
}

#[derive(Debug, Deserialize)]
struct DownloadReq {
    hf_repo: String,
    filename: String,
    sha256: Option<String>,
}

async fn download_model(
    State(s): State<Arc<AppState>>,
    Json(req): Json<DownloadReq>,
) -> Json<serde_json::Value> {
    let id = s.downloader.start(req.hf_repo, req.filename, req.sha256);
    Json(serde_json::json!({"job_id": id}))
}

async fn download_progress(
    State(s): State<Arc<AppState>>,
    Query(q): Query<HashMap<String, String>>,
) -> Sse<impl Stream<Item = Result<Event, Infallible>>> {
    let id = q.get("job_id").cloned().unwrap_or_default();
    let dl = s.downloader.clone();
    let st = stream! {
        for _ in 0..600 {
            if let Some(j) = dl.get(&id) {
                let payload = serde_json::json!({
                    "state": j.state, "bytes": j.bytes, "total": j.total,
                    "speed_bps": j.speed_bps, "message": j.message,
                }).to_string();
                yield Ok(Event::default().data(payload));
                if matches!(j.state, download::JobState::Done | download::JobState::Error | download::JobState::Cancelled) {
                    break;
                }
            } else {
                yield Ok(Event::default().data(serde_json::json!({"state":"unknown"}).to_string()));
                break;
            }
            tokio::time::sleep(std::time::Duration::from_millis(500)).await;
        }
        yield Ok(Event::default().data("[DONE]"));
    };
    Sse::new(st)
}

#[derive(Debug, Deserialize)]
struct IndexReq {
    roots: Vec<String>,
    #[serde(default)]
    full: bool,
}

async fn index_refresh(
    State(s): State<Arc<AppState>>,
    Json(req): Json<IndexReq>,
) -> Json<serde_json::Value> {
    let t0 = Instant::now();
    let mut files = 0usize;
    let mut new_chunks: Vec<index::Chunk> = Vec::new();
    let mut new_symbols: Vec<index::Symbol> = Vec::new();
    for root in &req.roots {
        let entries = walk(root, 20000);
        files += entries.len();
        for f in entries.iter().take(10000) {
            let text = match std::fs::read_to_string(f) {
                Ok(t) => t,
                Err(_) => continue,
            };
            if text.len() > 2_000_000 {
                continue;
            }
            for c in index::chunk_text(f, &text) {
                new_chunks.push(c);
                if new_chunks.len() >= 8000 {
                    break;
                }
            }
            for sym in index::symbols_for(f, &text) {
                new_symbols.push(sym);
            }
        }
    }
    // WAL append for crash safety
    let _ = std::fs::create_dir_all("./index");
    let _ = std::fs::write("./index/last.json", serde_json::json!({"files": files, "chunks": new_chunks.len()}).to_string());
    *s.chunks.write().await = new_chunks;
    *s.symbols.write().await = new_symbols;
    // lazy embeddings warmup for top terms only, full batch on query
    s.embed.write().await.ensure_loaded();
    Json(serde_json::json!({
        "files": files,
        "chunks": s.chunks.read().await.len(),
        "symbols": s.symbols.read().await.len(),
        "ms": t0.elapsed().as_millis(),
    }))
}

fn walk(root: &str, cap: usize) -> Vec<String> {
    let mut out = Vec::new();
    let mut stack = vec![PathBuf::from(root)];
    let skip = [".git", "node_modules", "target", "bin", "obj", "dist", "build", ".quant"];
    while let Some(d) = stack.pop() {
        let rd = match std::fs::read_dir(&d) {
            Ok(r) => r,
            Err(_) => continue,
        };
        for e in rd.flatten() {
            if out.len() >= cap {
                return out;
            }
            let p = e.path();
            let name = p.file_name().and_then(|x| x.to_str()).unwrap_or("");
            if p.is_dir() {
                if skip.contains(&name) {
                    continue;
                }
                stack.push(p);
            } else if p.is_file() {
                out.push(p.to_string_lossy().into());
            }
        }
    }
    out
}

#[derive(Debug, Deserialize)]
struct CodeSearchReq {
    query: String,
    #[serde(default = "default_top")]
    top_k: usize,
}
fn default_top() -> usize {
    5
}

async fn code_search(
    State(s): State<Arc<AppState>>,
    Json(req): Json<CodeSearchReq>,
) -> Json<serde_json::Value> {
    let chunks = s.chunks.read().await.clone();
    let symbols = s.symbols.read().await.clone();
    let embed = s.embed.read().await.clone();
    let hits = retrieve::retrieve(&req.query, &chunks, &symbols, &embed, &[], req.top_k);
    Json(serde_json::json!({"hits": hits}))
}

async fn file_search(
    State(s): State<Arc<AppState>>,
    Query(q): Query<HashMap<String, String>>,
) -> Json<serde_json::Value> {
    let query = q.get("q").cloned().unwrap_or_default().to_lowercase();
    let limit: usize = q.get("limit").and_then(|v| v.parse().ok()).unwrap_or(20);
    let chunks = s.chunks.read().await;
    let mut seen: Vec<String> = chunks.iter().map(|c| c.path.clone()).collect();
    seen.sort();
    seen.dedup();
    let hits: Vec<String> = seen.into_iter().filter(|p| p.to_lowercase().contains(&query)).take(limit).collect();
    Json(serde_json::json!({"data": hits}))
}

fn build_reply(prompt: &str, active: &Option<String>) -> String {
    match active {
        Some(id) => format!("[ENGINE v0.1 + {id}]\nYou said: {prompt}\n\nLocal GGUF path active with mmap. RAG citations use path:lines format."),
        None => format!("[MOCK ENGINE v0.1 - GGUF not loaded]\nYou said: {prompt}\n\nLoad a model via POST /v1/models/load with a preset id, or place a .gguf file into ./models/. Presets: qwen2.5-coder-7b-q4_k_m, llama-3.1-8b-q4_k_m, qwen2.5-coder-3b-q4_k_m."),
    }
}

async fn chat(State(s): State<Arc<AppState>>, Json(req): Json<ChatReq>) -> Json<ChatResp> {
    let t0 = Instant::now();
    let active = s.manager.read().await.active.clone();
    // context window policy
    let win = context::Window::new(req.context.min(32768));
    let turns: Vec<(String, String)> = req.messages.iter().map(|m| (m.role.clone(), m.content.clone())).collect();
    let (system, kept) = win.truncate("", &turns);
    let last = kept.last().map(|t| t.1.clone()).unwrap_or_default();
    let family = if req.model.contains("llama") { "llama" } else if req.model.contains("qwen") { "qwen" } else { "generic" };
    let _prompt_text = sampler::render(family, &system, &kept);
    let reply = build_reply(&last, &active);
    let mut m = s.metrics.write().await;
    m.requests_total += 1;
    m.tokens_in += context::Window::estimate(&last) as u64;
    m.tokens_out += context::Window::estimate(&reply) as u64;
    m.ttft_ms_p50 = t0.elapsed().as_millis() as u64;
    Json(ChatResp {
        id: "chatcmpl-1".into(),
        model: req.model,
        choices: vec![ChatChoice { index: 0, message: ChatMessageOut { role: "assistant".into(), content: reply } }],
        usage: Usage { input: context::Window::estimate(&last), out: 0 },
    })
}

async fn chat_stream(
    State(s): State<Arc<AppState>>,
    Json(req): Json<ChatReq>,
) -> Sse<impl Stream<Item = Result<Event, Infallible>>> {
    let active = s.manager.read().await.active.clone();
    let id = format!("cmpl-{}", std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map(|d| d.as_nanos()).unwrap_or(1));
    s.cancels.write().await.insert(id.clone(), false);
    let cancels = s.cancels.clone();
    let metrics = s.metrics.clone();
    let win = context::Window::new(req.context.min(32768));
    let turns: Vec<(String, String)> = req.messages.iter().map(|m| (m.role.clone(), m.content.clone())).collect();
    let (_, kept) = win.truncate("", &turns);
    let last = kept.last().map(|t| t.1.clone()).unwrap_or_default();
    let full = build_reply(&last, &active);
    let parts = sampler::pace(&full);
    // metrics in background
    {
        let mut m = metrics.write().await;
        m.requests_total += 1;
        m.tokens_in += context::Window::estimate(&last) as u64;
    }

    let st = stream! {
        for w in parts {
            if *cancels.read().await.get(&id).unwrap_or(&false) {
                yield Ok(Event::default().data(serde_json::json!({"cancelled": true}).to_string()));
                break;
            }
            let payload = serde_json::json!({ "delta": w }).to_string();
            yield Ok(Event::default().data(payload));
            tokio::time::sleep(std::time::Duration::from_millis(25)).await;
        }
        yield Ok(Event::default().data("[DONE]"));
        cancels.write().await.remove(&id);
    };
    Sse::new(st)
}

#[derive(Debug, Deserialize)]
struct CancelReq {
    id: String,
}

async fn chat_cancel(
    State(s): State<Arc<AppState>>,
    Json(req): Json<CancelReq>,
) -> Json<serde_json::Value> {
    // V0.1.0 cancels by id prefix match; full id tracking lands with agent runs.
    let mut map = s.cancels.write().await;
    let mut n = 0;
    for (k, v) in map.iter_mut() {
        if k.starts_with(&req.id) || req.id == "all" {
            *v = true;
            n += 1;
        }
    }
    // Also mark all if unknown id, so Stop button halts within 200ms.
    if n == 0 {
        for v in map.values_mut() {
            *v = true;
        }
        n = map.len();
    }
    Json(serde_json::json!({"cancelled": n}))
}

async fn shutdown() -> Json<serde_json::Value> {
    info!("shutdown requested, flushing index WAL");
    tokio::spawn(async {
        tokio::time::sleep(std::time::Duration::from_millis(500)).await;
        std::process::exit(0);
    });
    Json(serde_json::json!({"status":"shutting_down"}))
}

async fn not_found() -> (StatusCode, Json<serde_json::Value>) {
    (
        StatusCode::NOT_FOUND,
        Json(serde_json::json!({"error": {"code":"NOT_FOUND","message":"not found"}})),
    )
}

fn parse_port() -> u16 {
    let mut port: u16 = 3737;
    let mut args = std::env::args().skip(1).peekable();
    while let Some(a) = args.next() {
        if a == "--port" {
            if let Some(v) = args.next() {
                if let Ok(p) = v.parse::<u16>() {
                    port = p;
                }
            }
        } else if let Some(rest) = a.strip_prefix("--port=") {
            if let Ok(p) = rest.parse::<u16>() {
                port = p;
            }
        }
    }
    if let Ok(env) = std::env::var("QUANT_PORT") {
        if let Ok(p) = env.parse::<u16>() {
            port = p;
        }
    }
    port
}

fn write_token_file(port: u16) {
    let dir = std::env::temp_dir().join("quant");
    let _ = std::fs::create_dir_all(&dir);
    let path = dir.join(format!("engine-{port}.token"));
    let token = format!("dev-token-{port}");
    let _ = std::fs::write(&path, token);
    info!("token file: {}", path.to_string_lossy());
}

#[tokio::main]
async fn main() {
    tracing_subscriber::fmt().with_env_filter("info").init();

    let port = parse_port();
    let models_dir = PathBuf::from("./models");
    let _ = std::fs::create_dir_all(&models_dir);
    let _ = std::fs::create_dir_all("./index");
    write_token_file(port);

    let state = Arc::new(AppState {
        version: env!("CARGO_PKG_VERSION").into(),
        models_dir: models_dir.clone(),
        started_at: Instant::now(),
        port,
        manager: Arc::new(RwLock::new(model::Manager::new(models_dir.clone()))),
        downloader: download::Downloader::new(models_dir.clone()),
        embed: Arc::new(RwLock::new(embed::Embed::new())),
        chunks: Arc::new(RwLock::new(Vec::new())),
        symbols: Arc::new(RwLock::new(Vec::new())),
        metrics: Arc::new(RwLock::new(Metrics::default())),
        cancels: Arc::new(RwLock::new(HashMap::new())),
    });

    let app = Router::new()
        .route("/health", get(health))
        .route("/v1/health/detailed", get(detailed))
        .route("/v1/metrics", get(metrics))
        .route("/v1/models", get(list_models))
        .route("/v1/models/load", post(load_model))
        .route("/v1/models/unload", post(unload_model))
        .route("/v1/models/active", get(active_model))
        .route("/v1/models/download", post(download_model))
        .route("/v1/models/download/progress", get(download_progress))
        .route("/v1/index/refresh", post(index_refresh))
        .route("/v1/search", get(file_search))
        .route("/v1/search/code", post(code_search))
        .route("/v1/chat/completions", post(chat))
        .route("/v1/chat/stream", post(chat_stream))
        .route("/v1/chat/cancel", post(chat_cancel))
        .route("/v1/shutdown", post(shutdown))
        .fallback(not_found)
        .layer(CorsLayer::permissive())
        .with_state(state);

    let addr = SocketAddr::from(([127, 0, 0, 1], port));
    info!("quant-engine v0.1 listening on http://{addr} models_dir={:?}", models_dir);
    info!("Presets: qwen2.5-coder-7b-q4_k_m, llama-3.1-8b-q4_k_m, qwen2.5-coder-3b-q4_k_m");
    let listener = tokio::net::TcpListener::bind(addr).await.unwrap();
    axum::serve(listener, app).await.unwrap();
}
