use axum::{
    Json, Router,
    extract::State,
    http::StatusCode,
    response::sse::{Event, Sse},
    routing::{get, post},
};
use async_stream::stream;
use futures::stream::Stream;
use serde::{Deserialize, Serialize};
use std::{convert::Infallible, net::SocketAddr, path::PathBuf, sync::Arc, time::Instant};
use tower_http::cors::CorsLayer;
use tracing::info;

#[derive(Clone)]
struct AppState {
    version: String,
    models_dir: PathBuf,
    started_at: Instant,
    port: u16,
}

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
    queue_depth: usize,
}

#[derive(Debug, Serialize)]
struct ModelInfo {
    id: String,
    file: String,
    size_mb: Option<f64>,
    quantized: bool,
}

#[derive(Debug, Serialize)]
struct ModelsResp {
    data: Vec<ModelInfo>,
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
}

fn default_model() -> String {
    "mock-qwen-coder-7b-q4".into()
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
struct ChatResp {
    id: String,
    model: String,
    choices: Vec<ChatChoice>,
}

async fn health(State(s): State<Arc<AppState>>) -> Json<HealthResp> {
    Json(HealthResp {
        status: "ok".into(),
        version: s.version.clone(),
        model_loaded: false,
        models_dir: s.models_dir.to_string_lossy().into(),
        port: s.port,
        uptime_s: s.started_at.elapsed().as_secs(),
    })
}

async fn detailed(State(s): State<Arc<AppState>>) -> Json<DetailedHealth> {
    let index_files = count_files(&s.models_dir);
    Json(DetailedHealth {
        status: "ok".into(),
        version: s.version.clone(),
        uptime_s: s.started_at.elapsed().as_secs(),
        port: s.port,
        models_dir: s.models_dir.to_string_lossy().into(),
        index_files,
        queue_depth: 0,
    })
}

fn count_files(dir: &PathBuf) -> usize {
    std::fs::read_dir(dir).map(|r| r.count()).unwrap_or(0)
}

async fn list_models(State(s): State<Arc<AppState>>) -> Json<ModelsResp> {
    // Scan models dir for GGUF files. No load into RAM here, mmap happens at inference time.
    let mut data = Vec::new();
    if let Ok(rd) = std::fs::read_dir(&s.models_dir) {
        for e in rd.flatten() {
            let p = e.path();
            if p.extension().and_then(|x| x.to_str()) == Some("gguf") {
                let size_mb = e.metadata().ok().map(|m| m.len() as f64 / 1024.0 / 1024.0);
                data.push(ModelInfo {
                    id: p.file_stem().and_then(|x| x.to_str()).unwrap_or("unknown").into(),
                    file: p.file_name().and_then(|x| x.to_str()).unwrap_or("").into(),
                    size_mb,
                    quantized: true,
                });
            }
        }
    }
    if data.is_empty() {
        data.push(ModelInfo {
            id: "mock-qwen-coder-7b-q4".into(),
            file: "(no .gguf in ./models - place HF file here)".into(),
            size_mb: None,
            quantized: true,
        });
    }
    Json(ModelsResp { data })
}

async fn chat(State(_s): State<Arc<AppState>>, Json(req): Json<ChatReq>) -> Json<ChatResp> {
    let last = req.messages.last().map(|m| m.content.clone()).unwrap_or_default();
    let reply = mock_answer(&last);
    Json(ChatResp {
        id: "chatcmpl-mock".into(),
        model: req.model,
        choices: vec![ChatChoice {
            index: 0,
            message: ChatMessageOut { role: "assistant".into(), content: reply },
        }],
    })
}

fn mock_answer(prompt: &str) -> String {
    format!(
        "[MOCK ENGINE v0.1 - GGUF not loaded]\nYou said: {prompt}\n\nNext the Rust engine will:\n1. Load GGUF via llama.cpp with mmap Q4_K_M for low RAM\n2. Stream tokens via SSE\n3. Index codebase with tree-sitter for RAG\n\nPlace a .gguf file from Hugging Face into ./models/ then restart the engine."
    )
}

async fn chat_stream(
    State(_s): State<Arc<AppState>>,
    Json(req): Json<ChatReq>,
) -> Sse<impl Stream<Item = Result<Event, Infallible>>> {
    let last = req.messages.last().map(|m| m.content.clone()).unwrap_or_default();
    let full = mock_answer(&last);
    let words: Vec<String> = full.split_inclusive(' ').map(|w| w.to_string()).collect();

    let st = stream! {
        for w in words {
            let payload = serde_json::json!({ "delta": w }).to_string();
            yield Ok(Event::default().data(payload));
            tokio::time::sleep(std::time::Duration::from_millis(30)).await;
        }
        yield Ok(Event::default().data("[DONE]"));
    };
    Sse::new(st)
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
    // Per launch token for localhost auth, phase 0.2 stub with fixed dev token plus file.
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
    write_token_file(port);

    let state = Arc::new(AppState {
        version: env!("CARGO_PKG_VERSION").into(),
        models_dir: models_dir.clone(),
        started_at: Instant::now(),
        port,
    });

    let app = Router::new()
        .route("/health", get(health))
        .route("/v1/health/detailed", get(detailed))
        .route("/v1/models", get(list_models))
        .route("/v1/chat/completions", post(chat))
        .route("/v1/chat/stream", post(chat_stream))
        .route("/v1/shutdown", post(shutdown))
        .fallback(not_found)
        .layer(CorsLayer::permissive())
        .with_state(state);

    let addr = SocketAddr::from(([127, 0, 0, 1], port));
    info!("quant-engine v0.1 listening on http://{addr} models_dir={:?}", models_dir);
    info!("Place GGUF from HF into ./models/*.gguf, example Qwen2.5-Coder-7B-Q4_K_M.gguf");
    let listener = tokio::net::TcpListener::bind(addr).await.unwrap();
    axum::serve(listener, app).await.unwrap();
}
