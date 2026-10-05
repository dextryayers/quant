# Quant IDE

Local first AI IDE. Avalonia .NET 10 GUI plus Rust engine plus GGUF local inference.

## Prereqs
- .NET SDK 10.0.400
- Rust stable with cargo
- Windows 10 plus, Linux, or macOS

## Quickstart
```powershell
# terminal 1: engine on 127.0.0.1:3737
cargo run -p quant-engine --manifest-path engine/quant-engine/Cargo.toml

# terminal 2: GUI
dotnet run --project gui/Quant.Desktop
```

Or one command:
```powershell
./scripts/run-dev.ps1
```

## Layout
- `gui/Quant.Desktop`: Avalonia shell, editor, chat, theme tokens
- `engine/quant-engine`: Axum sidecar, health, models, chat stream, models dir in `engine/quant-engine/models`
- `plan.md`: source of truth, English only, no emdash character
- `scripts`: dev boot, fixture repo, perf baseline
- `perf`: baseline numbers

## Config
Copy `quant.json.example` to `quant.json` to override port and model defaults. GUI merges global plus workspace config. Logs go to `.quant/logs` per workspace plus engine stdout.

## Health
- Engine: `GET http://127.0.0.1:3737/health`
- Models: `GET http://127.0.0.1:3737/v1/models`
- Chat stream: `POST http://127.0.0.1:3737/v1/chat/stream` with SSE

See `plan.md` Phase 0 to Phase 14 for build order.
"# quant" 
