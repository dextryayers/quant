# Quant IDE - Master Plan v2 Deep Spec
## OpenCode and Antigravity Class AI IDE with C# GUI plus Rust Engine

Version: 2.0 Deep Spec
Status: Source of truth for design plus build order
Stack: Avalonia UI 11 plus .NET 10 for GUI, Rust plus Axum for engine, GGUF via llama.cpp for local inference
IPC: localhost REST plus SSE at 127.0.0.1:3737, gRPC optional later
Language: English only
Style rule: never use emdash character. Use hyphen - or comma or colon instead.

---

## Table of Contents
1. Vision, Personas, Goals, Metrics
2. Benchmark Deep Dive
3. Architecture Overview
4. Design System for Modern Premium Elegant Professional UI UX
5. Screen by Screen UX Spec
6. Workspace plus File plus Folder Full Action Spec
7. Editor Pro Spec
8. Global Search plus Symbols plus Problems Spec
9. AI Chat plus Agent Spec
10. Rust Engine plus Local GGUF Spec
11. Index plus RAG Spec
12. IPC API Contract with Examples
13. Security, Privacy, Permissions
14. Performance plus Memory plus Responsiveness Budgets
15. Testing plus Quality plus Release Gates
16. Phased Delivery Plan Phase 0 to Phase 14
17. Packaging, First Run, Docs, Support
18. Risks and Mitigations
19. Appendix A: Master Shortcut Map
20. Appendix B: Config Schema
21. Appendix C: Prompt and Slash Catalog
22. Appendix D: Glossary and Conventions

---

## 1. Vision, Personas, Goals, Metrics

### 1.1 Vision
Quant IDE is a local first, memory efficient, premium AI IDE for coding plus daily discussion. It opens fast, stays calm under large repos, answers with local GGUF models when offline, and scales to agentic edits with explicit approval. It borrows speed thinking from OpenCode and calm polish from Antigravity, without Electron weight and without forced cloud lock in.

### 1.2 Personas
- P1 Solo builder: wants fast open, edit, chat, apply diff, run tasks. Low patience for setup.
- P2 Pro engineer: wants multi root, symbols, global replace, agent with audit, terminal tasks.
- P3 Low spec user: 8 GB RAM laptop, CPU only, needs small quant plus clear guidance.
- P4 Privacy user: offline first, no telemetry without opt in, local index only.

### 1.3 Jobs To Be Done
- Open a folder and start editing in under 2 seconds.
- Ask about code with file citations and apply a safe diff.
- Create, move, rename, duplicate project scaffolds without leaving the IDE.
- Run build plus test and jump to errors.
- Switch local models without losing threads.
- Continue daily discussion with general knowledge when no repo is open.

### 1.4 Goals for v1
- G1 Native lean shell. Avalonia GUI plus Rust sidecar, no web view for core.
- G2 Full workspace parity. Read, open, create, rename, move, delete, duplicate, reveal, search, watch.
- G3 Pro editor. Tabs, splits, breadcrumbs, symbols, global find replace, diagnostics.
- G4 Agent chat. Ask, Edit, Agent modes with context chips plus approvals.
- G5 Local GGUF. mmap load, streaming SSE, cancel, HF download with resume.
- G6 RAG that cites. Hybrid retrieval with file line citations.
- G7 Crash isolation. Engine can restart independently.
- G8 Keyboard first premium UX. Palette covers 100 percent of actions.

### 1.5 Non Goals for v1
- No plugin marketplace. Provide internal extension seams only.
- No remote SSH or WSL editing. Local disks only.
- No full debugger UI. Provide tasks plus terminal plus problems plus launch stubs.
- No collaboration or live share.
- No forced account. Cloud providers optional and off by default.

### 1.6 Success Metrics
- Cold start to editable window: under 1.5 s on mid hardware, under 2.5 s on low hardware.
- Typing latency: under 16 ms p95 in 10k line files.
- Palette open: under 50 ms. Quick Open: under 100 ms warm.
- Explorer filter on 50k files: under 300 ms warm.
- Chat first token local 7B Q4 CPU: under 1.5 s warm model, streaming readable at 15 to 40 tokens per s.
- Agent apply accuracy on fixture tasks: 8 of 10 without manual fix.
- Stability: 7 day daily use without data loss, engine crash never kills GUI.

---

## 2. Benchmark Deep Dive

### 2.1 OpenCode: adopt the brain
- Tool loop: read, glob, grep, edit, bash with approval receipts.
- Session threads with resume, fork, rename, export markdown.
- Slash commands plus custom prompts from repo files.
- Config driven: model, permissions, ignore, autocompact.
- Dry run inspector that shows exact prompt plus tokens before send.
- What to improve: give it GUI polish, diff per hunk UI, approval cards, citations.

### 2.2 Antigravity: adopt the calm
- Left activity bar plus side panel plus editor plus right agent plus bottom panel plus status bar.
- Command palette plus fuzzy file finder with preview.
- Ghost completion plus selection quick actions: explain, fix, refactor.
- Artifact panel for preview plus diff plus terminal output.
- Onboarding with theme pick, model pick, folder pick, shortcut tour.
- What to improve: make it local first, lean RAM, offline capable.

### 2.3 VS Code: adopt completeness
- Explorer full actions matrix, multi select, drag drop, watcher banners, problems panel.
- Settings JSON plus UI, keybindings editor, tasks.json pattern.
- What to avoid: extension host heaviness, telemetry by default.

### 2.4 Cursor and Zed: adopt speed cues
- Fast symbol jump, inline diff accept, next suggestion key, token meter.
- Minimal chrome, focus mode, zen mode.
- What to avoid: cloud only inference dependency.

### 2.5 Differentiation for Quant IDE
- Rust engine sidecar with mmap GGUF plus hybrid RAG plus tool gateway.
- Single .NET Avalonia shell under 250 MB with 5 files open.
- Offline presets for 3B, 7B, 8B quants with RAM preflight.
- Audit log for every write plus exec with replay.

---

## 3. Architecture Overview

### 3.1 Process Model
- GUI process `Quant.Desktop`: renders, handles input, owns ViewModels, theme, dialogs, workspace state cache. It never loads GGUF.
- Engine process `quant-engine`: Axum server on localhost. Owns model manager, sampler, index store, retrieval, tools, downloader, metrics.
- Supervisor: GUI spawns engine as child, passes port plus token plus models dir plus log dir. It watches heartbeat every 3 s. On crash it shows retry card and restarts with backoff. Last 200 log lines surface in Output panel.

### 3.2 Startup Sequence
1. GUI parses args and config, selects theme, restores layout skeleton instantly.
2. GUI spawns engine child or connects to existing port if token matches.
3. GUI calls `GET /health`, then `GET /v1/models`, then `POST /v1/index/refresh` lazy in background.
4. Explorer renders from fast file list first, then enriches with git status plus symbols.
5. Chat restores last thread per workspace. Model picker shows active plus RAM estimate.
6. Status bar shows engine dot green, index progress, model name, RAM use.

### 3.3 Shutdown Sequence
- GUI cancels in flight streams, flushes workspace state, sends `POST /v1/shutdown` graceful with 2 s timeout, then kills child if needed. Index WAL flushes. No loss of open tabs or drafts. Unsaved files prompt with Save All option.

### 3.4 Monorepo Target Layout
```text
quant/
  plan.md
  README.md
  Quant.slnx
  global.json
  Directory.Build.props
  .editorconfig
  gui/
    Quant.Desktop/
      Program.cs, App.axaml, App.axaml.cs
      Views/
        MainWindow.axaml
        Shell/
          ActivityBar.axaml
          SideExplorer.axaml
          SideSearch.axaml
          SideChat.axaml
          SideModels.axaml
          SideSettings.axaml
          EditorArea.axaml
          BottomPanel.axaml
          StatusBarView.axaml
          CommandPalette.axaml
        Dialogs/
          ConfirmDialog.axaml
          InputDialog.axaml
          ConflictDialog.axaml
          ModelDownloadDialog.axaml
          ApprovalDialog.axaml
      ViewModels/
      Models/
      Services/
        QuantEngineClient.cs
        WorkspaceService.cs
        ExplorerService.cs
        EditorService.cs
        ChatService.cs
        ModelService.cs
        IndexService.cs
        TerminalService.cs
        ThemeService.cs
        ShortcutService.cs
        ToastService.cs
      Styles/
        ThemeDarkPremium.axaml
        ThemeLightPro.axaml
        Controls.axaml
      Assets/
      quant.json schema
    Quant.Tests/
  engine/
    quant-engine/
      Cargo.toml
      src/
        main.rs
        config.rs
        supervisor.rs
        routes/
          health.rs
          models.rs
          chat.rs
          index.rs
          search.rs
          tools.rs
          metrics.rs
        inference/
          manager.rs
          sampler.rs
          template.rs
          embeddings.rs
        index/
          crawl.rs
          chunk.rs
          symbols.rs
          store.rs
          retrieve.rs
        tools/
          read.rs
          glob.rs
          grep.rs
          diff.rs
          exec.rs
          download.rs
        util/
          tokens.rs
          secrets.rs
      models/.gitkeep
      tests/fixtures/
  openapi/quant-engine.yaml
  scripts/
    run-dev.ps1
    run-dev.sh
    make-fixture-repo.ps1
  docs/
```

### 3.5 Technology Choices
- GUI: .NET 10, C# 13, Avalonia 11 plus AvaloniaEdit via Avalonia.AvaloniaEdit, CommunityToolkit.Mvvm, xUnit plus Avalonia.Headless for tests.
- Engine: Rust stable, Tokio, Axum 0.7, Tower HTTP CORS localhost only, Serde, Tantivy or SQLite FTS plus custom trigram for v1, tree-sitter for symbols, reqwest for HF download, llama.cpp binding for inference.
- IPC: JSON over HTTP plus SSE. NDJSON for logs. Future gRPC only if CPU profiling proves need.
- Storage: workspace `.quant/` holds `threads/`, `index/`, `logs/`, `approvals.jsonl`, `settings.workspace.json`. Global `%AppData%/Quant/` or `~/.config/quant/` holds `settings.json`, `models/registry.json`, `keybindings.json`.

### 3.6 Threading and Memory Model
- GUI: UI thread only for render. IO on thread pool. Streaming deltas batched every 16 to 32 ms to avoid layout thrash. Virtualized lists everywhere.
- Engine: Tokio multi thread with blocking pool for inference. Single model lock with read write split: many readers for embeddings, single writer for generate. Inference runs on blocking thread to keep Axum responsive. Cancellation via token plus client disconnect.
- Files: never read full 50k tree into UI at once. Paged tree with lazy expand. Content reads capped at 2 MB per file in chat context unless explicit attach with warning.

---

## 4. Design System for Modern Premium Elegant Professional UI UX

### 4.0 Anti AI Slop Manifesto - Strict Rules
This product must never look like generic AI output. Enforce these bans plus requirements in design review.

Banned patterns:
- No emoji as icons in chrome. Use 1.5 px stroke icons, 16 px grid, round caps. Emoji only inside user content, never in buttons, tabs, or status.
- No purple blue gradient background abuse. One restrained accent only: #6E9DFF. Surfaces stay flat with 1 px borders.
- No glassmorphism blur panels over code. Code needs contrast, not blur.
- No oversized hero headers or marketing copy inside the IDE. The IDE is a tool, not a landing page.
- No centered giant placeholder text like "Ask me anything". Use left aligned 13 px work focused copy.
- No chat bubbles with low contrast gray on gray. User and assistant must be distinct by border plus label, not by loud colors.
- No fake depth with many shadows. Use level 0 flat, level 1 border, level 2 dialog shadow only.
- No ALL CAPS sentences. Use tracked 11 px uppercase only for section labels like EDITOR or ASSISTANT.
- No lorem or placeholder ships. Every empty state has one action plus one shortcut.

Required craft details:
- Wordmark: QUANT tracked 2 px plus muted IDE, with 8 px accent dot. No logo emoji.
- Density: 30 px rows, 12 px padding, 8 px radius cards, 6 px inputs. Consistent everywhere.
- Typography contrast: primary #E8ECF3 on #0F1115 ratio above 12. Secondary #9AA4B8 for meta only, never for code.
- Focus: 2 px accent ring with offset, always visible, never shifting layout.
- Motion: 120 ms hover, 160 ms open, 180 ms slide, ease out cubic. Skeleton shimmer only for index and model load, never for typing.
- Sound: none by default. Haptics: none. Calm tool.
- Copy voice: verb led, short, technical. Examples: Open Folder, Load Model, Accept Hunk, Retry, View Logs. Never: Unleash your creativity.
- Icon language: folder, file, search, git branch, chat, box for models, gear. All same stroke weight, no filled mix.
- Data density over decoration: show path, line, size, RAM, tokens in 12 px secondary text. Hide decoration to make room for facts.

Premium checklist per screen:
- One primary action visible. The rest in palette or overflow.
- Every list virtualized. Every long op has progress plus cancel.
- Every destructive action has confirm or 10 s undo.
- Every loading state has skeleton plus status bar task, never a blocking modal.
- Every error has reason plus next action plus log link.

### 4.1 Principles Expanded
- Calm first: chrome at 60 percent contrast, content at full contrast.
- One primary action per view. Secondary actions in overflow or palette.
- Text is UI: buttons use verbs like Open Folder, Load Model, Accept Hunk.
- No dead ends: every empty state has one action plus one shortcut.
- Forgiving: destructive actions offer undo for 10 s or confirm with clear scope.

### 4.2 Spacing, Radius, Elevation
- Base unit 4 px. Panel padding 12 px. Card padding 12 to 16 px. Row height 30 to 32 px.
- Radius: 8 px cards, 6 px inputs, 8 px dialogs, 12 px palette.
- Elevation: level 0 flat, level 1 subtle border, level 2 border plus 8 px blur shadow 12 percent black, level 3 dialog shadow 24 percent.
- Motion: 120 ms hover, 160 ms open, 180 ms slide. Ease out cubic. Reduced motion disables all.

### 4.3 Theme Tokens - Exact Premium Scale
Dark premium default, flat surfaces, 1 px borders, single accent:
- `bg.base #0F1115`, `bg.surface1 #151923`, `bg.surface2 #1C2230`, `bg.overlay #1E2636`
- `border.subtle #2A3346 at 100 percent`, `border.strong #3A4763`
- `text.primary #E8ECF3`, `text.secondary #9AA4B8`, `text.muted #6B7690`
- `accent.primary #6E9DFF`, `accent.soft #22345E`, `accent.on #0B1220`
- `success #3FB68B`, `warning #E5A63D`, `danger #E5626E`
- `editor.bg #0F1115`, `editor.gutter #151923`, `editor.lineHi #1A2233`, `editor.selection #2A3B5E`
- `chat.user #1E2A44 with border #33456E`, `chat.assistant #151923 with border #2A3346`
- Focus ring: 2 px `accent.primary` at 50 percent plus 2 px offset, no layout shift.
- Shadow: only dialogs get `0 12 32 rgba(0,0,0,0.35)`. Cards and lists use borders, not shadows.

Light pro, paper calm, same accent darker:
- `bg.base #F7F8FA`, `bg.surface1 #FFFFFF`, `bg.surface2 #EEF1F6`, `border.subtle #D9E0EC`, `border.strong #B9C4D6`
- `text.primary #141A26`, `text.secondary #5A657A`, `text.muted #8A94A8`
- `accent.primary #2F6BFF`, `accent.soft #DCE6FF`
- `chat.user #EAF1FF with border #C4D7FF`, `chat.assistant #FFFFFF with border #D9E0EC`

Type scale, no marketing sizes:
- Section label: 11 px, 600 weight, 1.5 px letter spacing, uppercase, muted.
- UI base: 13 px, 400 weight, 20 px line height.
- UI secondary: 12 px, 400 weight, 18 px line height.
- Code: 14 px, 400 weight, 22.4 px line height. Tab width 4. Ligatures optional, off by default for clarity.
- Chat prose: 13 px, 400 weight, 21 px line height, max 76 ch. Code inside chat reuses 13 px mono with 8 px padding block.
- Numbers tabular: file sizes, RAM, tokens use tabular nums to avoid jitter.

Font stack:
- UI: Inter, Segoe UI, system sans. Fallback system only, never web fetch at runtime.
- Code: Cascadia Code, JetBrains Mono, Consolas, Menlo monospace.
- Never use display serif or rounded playful sans in chrome. Keep tool character.

Spacing scale:
- 4, 8, 12, 16, 24. Panel padding 12. Card padding 12. Gap rows 4 to 8. Page max 1200 for settings, editor fluid.

Icon rules:
- 16 px bounding, 1.5 px stroke, round caps and joins. Active icon accent tint 10 percent wash, not filled blob.
- File icons: single hue per type at 70 percent saturation max. No rainbow folder set.
- Status dots: 8 px solid with 2 px ring on dark. Green connected, amber working, red error, gray offline.

### 4.4 Component Catalog - Premium Behavior Spec
General states for all: rest, hover with 6 percent white overlay on dark, pressed with 10 percent, focus visible ring, disabled at 45 percent opacity, loading with 14 px spinner that keeps width, error with inline 12 px danger text plus retry link.

- BtnPrimary: 32 px height, 8 px radius, accent fill, dark text on accent for contrast, label verb led. Hover lifts none, only color shift 6 percent. No shadow.
- BtnSecondary: 32 px, transparent with 1 px border subtle, primary text. Hover border strong.
- BtnGhost: text only, secondary text, hover surface2 background. Used for Cancel, Clear, Dismiss.
- BtnDanger: outline danger border, danger text, solid danger fill only in confirm dialog primary slot.
- IconBtn: 28 px square, 6 px radius, icon 16 px centered, tooltip with shortcut always.
- TextField: 32 px, surface1 fill, 1 px border subtle, 6 px radius, 12 px horizontal padding. Focus border accent plus 2 px outer ring at 25 percent. Error border danger plus message below with icon.
- SearchField: same as TextField plus left 14 px search icon muted plus right kbd hint `Esc` to clear plus result count `12 of 480`.
- ModelPicker card: 3 rows. Row1 name 13 px semibold plus quant badge 11 px mono with border. Row2 meta 12 px secondary: size, context, source. Row3 actions Load plus Reveal plus Delete ghost. Active card has left 2 px accent bar plus Loaded pill success tint, not full green fill.
- Tabs: 34 px height, 0 radius top with 2 px underline active accent, inactive muted text. Dirty dot 6 px amber. Pinned tabs icon only 36 px width. Close X 16 px appears on hover, always visible on active. Middle click closes. Overflow `...` menu lists rest with preview.
- TreeRow: 30 px, 6 px radius on hover, chevron 12 px muted rotates 90 deg on expand, label 13 px, git dot 6 px right. Hover reveals New File plus New Folder plus More icon buttons without shifting label. Selected row accent soft background plus 1 px accent border left 2 px bar.
- Chip: 26 px height, 13 px radius pill, surface2 fill, 1 px border subtle, icon 12 px plus label 12 px plus token count 11 px mono muted plus X 12 px on hover. Click previews source. Overflow collapses to `+3` pill with tooltip list.
- ApprovalCard: bordered card 8 px radius, header 13 px semibold plus mode pill, body mono 12 px command or diff preview max 160 px scroll, footer Allow Once primary plus Always for Workspace secondary plus Deny ghost plus Details link. Timer shows auto deny in 60 s for exec.
- ProgressBar: 4 px height, 2 px radius, track surface2, fill accent solid. No animated stripes. Status bar task shows label plus percent plus Cancel X.
- Toast: bottom right 360 px max, surface overlay fill, 1 px border, 8 px radius, icon 16 px left, title 13 px semibold, message 12 px secondary, action button ghost plus close X. Undo variant keeps 10 s countdown bar 2 px accent.
- Dialog: 480 px default, 640 px large, overlay 50 percent black no blur, card 12 px radius with 1 px border plus dialog shadow. Title 14 px semibold, body 13 px, footer right aligned primary plus secondary plus ghost. Enter confirms, Esc cancels, no accidental backdrop click confirm.
- EmptyState: left aligned in panels, centered only in welcome. Icon 20 px muted in 36 px bordered square, title 13 px semibold, body 12 px secondary one line, primary button plus shortcut hint `Ctrl+P` in kbd style.
- Skeleton: 12 px rounded bars surface2 with 1.2 s soft pulse opacity 0.6 to 1.0, no shimmer sweep. Rows match real row height to avoid shift.
- Kbd: 11 px mono, border subtle, 4 px radius, 4 px horizontal padding, muted text. Used in menus, palette footer, tooltips.
- Tooltip: 12 px, surface overlay, 1 px border, 6 px radius, 8 px padding, delay 400 ms, includes shortcut right aligned muted.
- StatusBar: 28 px, surface1, top 1 px border, items 12 px secondary with 16 px spacing, dot plus label, click opens related panel. No emoji. Separators 1 px vertical 12 px height.

All components have states: rest, hover, pressed, focus visible, disabled, loading, error. Disabled uses 45 percent opacity plus not-allowed cursor. Loading replaces label with spinner plus keeps width to avoid shift.

### 4.5 Accessibility and Internationalization
- Full keyboard map, visible focus, logical tab order: palette, activity, side, editor, bottom, status.
- Screen reader labels for tree, tabs, chat messages, approvals.
- Contrast AA minimum. Text never below 11 px for functional labels.
- Strings in resource files, English only for v1 but no hardcoded concatenation that blocks future locales.
- Reduced motion and high contrast toggles in Settings plus OS respect.

---

## 5. Screen by Screen UX Spec

### S01 Welcome and Onboarding
- Layout: centered 720 px flat card on base background, 1 px border, 12 px radius. No gradient hero, no marketing headline. Title left aligned: Workspace setup.
- Steps: 1 Choose theme with live mini preview, 2 Choose font size with code sample, 3 Pick starter model preset with RAM badges, 4 Open Folder or Try Fixture.
- Elements: theme swatches show real editor snippet, model cards show size plus quant plus RAM required, Open Folder primary, Paste HF link secondary ghost.
- Exit: main shell with sample thread plus explorer focused. Single tour tooltip points to palette once, dismiss forever.
- Edge: offline shows Local ready pill gray plus skips download step. Copy is factual, never hype.

### S02 Main Shell
- Regions and default sizes: activity 48 px, side 320 px, editor flexible, right rail 360 px collapsible, bottom 220 px collapsible, status 28 px.
- Persist: sizes, visibility, active activity, open tabs, splits, bottom tab, right rail width.
- Responsive: under 1100 px right rail overlays as drawer. Under 800 px side becomes drawer.
- Loading: skeleton side plus editor tabs shimmer under 300 ms, real content streams in.

### S03 Command Palette plus Quick Open
- Two modes in one control: `>` command mode, file mode default, `@` symbol mode, `?` help.
- Size 640 px, top 18 percent, 8 results plus preview pane for files on right 320 px.
- Fuzzy scoring: exact prefix boost, filename boost over path, recency boost, open tab boost.
- Footer: Up Down navigate, Enter open, Ctrl+Enter open to side, Esc close.
- Empty: shows top 5 recent plus 3 suggested actions.

### S04 Explorer
- Header: workspace name plus root switcher plus overflow: Add Folder, Save Workspace As, Refresh, Show Excluded toggle.
- Filter field with count like `128 of 51240`. Sort menu: Name, Type, Modified. View menu: Tree, Compact, List.
- Tree: lazy expand, folder chevron, file icon by extension, git dot, hover quick actions.
- Selection: single click preview, double click pin, Ctrl click multi, Shift range, F2 rename inline.
- Footer: index status plus excluded count plus Reveal Active File button.

### S05 Editor Area
- Tab bar with left breadcrumbs and right split actions plus overflow.
- Breadcrumb path plus symbol stack. Click segment opens sibling picker.
- Gutter: line numbers, fold, diff colors, diagnostic dots, breakpoint stub slot.
- Hover: symbol signature, type, actions: Peek, Go to Definition stub, Add to Chat, Copy.
- Right click: full edit menu plus AI submenu: Explain Selection, Fix, Refactor, Generate Tests, Add to Chat.
- Zen mode: hides side plus bottom plus status except minimal, keeps palette.

### S06 Chat Panel
- Header: thread title editable, mode segmented Ask Edit Agent, model picker button, new thread, history, clear.
- Context zone: chips row plus token meter plus Add via @ plus dry run eye icon.
- Thread: grouped messages, avatarless clean bubbles, assistant prose with citations like `[src/App.cs:42]`.
- Code block toolbar: lang, Copy, Insert at Cursor, Apply as Diff, Open in Split.
- Composer: multiline auto grow to 160 px, Send on Enter, newline on Shift+Enter, Stop while streaming, mic stub hidden in v1.
- Approval inline: card replaces composer until decision, with details expander.
- History drawer: search threads, rename, delete, export markdown, fork from message.

### S07 Models Panel
- Active card on top with Unload, VRAM RAM meter, context use bar, idle timeout select.
- List: name, quant badge, size, context, source HF link, status Downloaded or Remote, RAM estimate, Load, Delete, Reveal Files.
- Download dialog: preset cards plus custom repo plus file plus SHA expected optional plus destination plus progress with pause resume cancel plus verify log.
- Empty: 3 presets with one click download plus Paste link option.

### S08 Global Search plus Replace
- Inputs: query, include glob, exclude glob, toggles regex, case, word, plus Replace field toggle.
- Scope: Current File, Open Files, Current Folder, Workspace. Folder picker when needed.
- Results grouped by file with 2 line context, checkbox per file and per hit, Replace Selected with preview diff plus undo.
- Performance: streaming results, first 50 in under 200 ms warm, total count plus capped render with virtualization.

### S09 Bottom Panel
- Tabs: Problems, Output, Terminal, Tasks. Badge counts. Filter per tab. Clear per tab.
- Problems: table severity, file, line, message, source. Click jumps. Group by file toggle.
- Output: channel picker Engine, Index, Agent, Tasks. Auto scroll toggle. Copy plus Save log.
- Terminal: tabs for shells, New plus Split plus Kill, cwd follows explorer or editor, link click to open file line.
- Tasks: list from `.quant/tasks.json`, Run, Stop, Rerun, View Problems.

### S10 Settings
- Sections: Appearance, Editor, Explorer, Chat, Models, Index, Agent and Approvals, Terminal, Shortcuts, About.
- Search settings field filters live. Each setting shows default badge plus Reset.
- Shortcuts editor: record chord, conflict warning, reset to default.
- About: versions GUI plus engine plus llama backend, paths, Copy Diagnostics button.

---

## 6. Workspace plus File plus Folder Full Action Spec

### 6.1 Data Model
- Workspace has id, name, roots list, trusted bool, activeFile, openTabs, splits, panelState, selectedModel, ignore overrides.
- Root has path, name override, excluded bool, favorite bool.
- Node has path, kind file or folder or symlink, size, modified, readonly, gitStatus, excludedReason.
- Operation has id, kind, sources, dest, undoToken, status, startedAt, finishedAt.

All mutating ops go through `ExplorerService` which validates, executes, emits watcher aware refresh, and offers undo where possible via recycle or move back journal.

### 6.2 Action Matrix
Each action has stable command id for palette plus shortcut plus context availability plus safety.

File actions:
- `file.new`: Ctrl+N. Empty untitled then Save As on first save. Works with no workspace.
- `file.newFromTemplate`: palette plus explorer New submenu. Templates: Rust main, C# class, Python script, Markdown doc, .gitignore dotnet plus rust plus node, MIT license, README. Placeholder vars for name plus date.
- `file.open`: Enter or double click. Preview if single click setting on.
- `file.openToSide`: Ctrl+Enter from palette or context. Opens in next split, creates split if none.
- `file.openInNewWindow`: spawns second GUI with file path arg.
- `file.quickOpen`: Ctrl+P. Preview on highlight, pin on Enter.
- `file.save`: Ctrl+S. `file.saveAll`: Ctrl+K S. `file.saveAs`: Ctrl+Shift+S. `file.revert`: reload from disk with confirm if dirty.
- `file.rename`: F2 inline. Validates length, illegal chars `< > : " / \ | ? *`, reserved Windows names, collision. Shows inline error. Updates open tabs plus chips plus threads references.
- `file.duplicate`: creates `name copy.ext` or `name copy 2.ext`. Focuses new file.
- `file.delete`: Del. Default to recycle bin. Confirm if folder non empty or multi select over 5. Undo toast 10 s where OS allows restore. `file.deletePermanent`: Shift+Del with typed confirm for folders.
- `file.cutCopyPaste`: Ctrl+X C V. Cross root supported. Conflict dialog with Keep Both, Replace, Skip, Apply to All. Progress for over 20 items.
- `file.revealInOS`: opens OS explorer and selects file. Handles spaces plus unicode.
- `file.copyPath`: absolute. `file.copyRelative`: workspace relative. `file.copyUri`: file URI. Toast confirms.
- `file.openWithDefault`: OS shell open. NoUnsaved loss.
- `file.showInTerminal`: opens terminal at parent dir.
- `file.addToChat`: attaches as context chip with line range if selection.
- `file.pin`: pin tab. `file.close`, `file.closeOthers`, `file.closeToRight`, `file.closeAll`.
- `file.properties`: size, modified, readonly toggle, executable bit on Unix, hash SHA1 short for large file warning.

Folder actions:
- `folder.new`: Ctrl+Shift+N in explorer context. Inline edit focused.
- `folder.open`: expand. `folder.expandRecursive`: Ctrl+Alt+Right. `folder.collapseAll`: Ctrl+K Ctrl+0 style plus button.
- `folder.rename`, `folder.duplicate`, `folder.delete` same safety as files plus extra warning for non empty with item count.
- `folder.newFileHere`, `folder.newFolderHere`, `folder.newTerminalHere`, `folder.searchHere`, `folder.replaceHere`.
- `folder.addToWorkspace`, `folder.removeFromWorkspace` with keep open files prompt.
- `folder.addToFavorites`, `folder.hideFromView`, `folder.unhideAll`. Hidden stored per workspace.
- `folder.copyAsImport`: for `models/` root, paste HF link flow.
- `folder.bulkMove`, `folder.bulkRenamePreview`: pattern `{name}_{index}`, dry run list, then apply with undo journal.

Multi root actions:
- `workspace.openFolder`: Ctrl+K Ctrl+O. Replaces roots after save prompt.
- `workspace.addFolder`: appends root. `workspace.removeFolder`: with file close plan.
- `workspace.saveAs`: writes `.quant-workspace.json`. `workspace.recent`: MRU 15 with pin and clear.
- `workspace.trust`: Trust, Distrust. Untrusted disables exec plus tasks plus auto apply.
- `workspace.refresh`: full rescan. `workspace.revealActiveFile`: Ctrl+Shift+R style plus button.

All actions appear in palette with `Explorer:` prefix plus in context menus in logical groups plus separators. Disabled states explain why via tooltip, example Read only or Untrusted workspace.

### 6.3 Explorer Interactions Detail
- Single click: preview tab italic. Double click or Edit key: pin tab.
- Hover row: shows New File, New Folder, More icons on right without shifting label.
- Inline rename: Enter commit, Esc cancel, Tab commit and next. Validation live.
- Multi select: Ctrl toggle, Shift range, Ctrl+A all visible, Del deletes with single confirm summarizing count.
- Drag drop internal: drag files to folder to move, hold Ctrl to copy, hold Alt to create shortcut stub where OS allows. Auto expand folder on 600 ms hover. Drop on editor area opens files. Drop on chat attaches. Drop from OS copies into workspace with conflict dialog.
- Keyboard tree: Arrows navigate, Right expand, Left collapse or parent, Home End, Type to filter, F2 rename, Del delete, Ctrl+C X V, Ctrl+Enter open to side.
- Filter: fuzzy on name plus path segments, highlights match ranges, shows `x of y`, Esc clears, toggle `Show Excluded`.
- Sort: Name asc, Type then Name, Modified desc. Favorites pinned group on top optional.

### 6.4 Conflict and Error Handling
- Name collision dialog lists existing size plus modified plus incoming, with radio Keep Both, Replace, Skip, checkbox Apply to All, plus Show Diff for text under 200 KB.
- Read only file edit: banner offers Make Writable or Save As.
- Locked file on Windows: retry once after 300 ms, then error card with Process hint and Retry button.
- Symlink: shows link icon, target tooltip, delete removes link not target with explicit label.
- Long path Windows: uses extended prefix internally, warns over 240 chars in rename.

### 6.5 Watcher Spec
- Native watcher per root with 150 ms debounce plus 1 s coalesce for bulk.
- External change to open dirty file: banner with Reload, Compare Side by Side, Keep Mine.
- External delete of open file: tab becomes orphan with Save As CTA.
- Bulk guard: over 2000 events in 5 s pauses live tree updates, shows Refresh Required pill, index continues in background.
- Ignore: respects `.gitignore` plus `.quantignore` plus global exclude list. Toggle shows excluded grayed with reason tooltip.

### 6.6 Acceptance for File System
- CRUD matrix passes for files plus folders plus multi select plus drag drop on Windows and Linux.
- Undo toast restores delete plus move in 10 s window where OS recycle allows, else journal move back.
- Watcher catches external `git pull` with 2k files without freeze and prompts refresh once.
- Paths with spaces, unicode, plus `#` plus `%` work for copy, reveal, terminal, chat attach.
- No destructive action without confirm or undo. Verified by checklist in Phase 2 gate.

---

## 7. Editor Pro Spec

### 7.1 Tabs and Groups
- Groups: up to 3, split right or down, move tab via drag or `View: Move Tab to Next Group`.
- Preview tab: italic, single slot per group, replaced by next preview until pinned.
- Pin: pinned tabs left section with smaller close affordance.
- Dirty: dot plus Save prompt on close with Save All option. Auto save modes: off, afterDelay 800 ms, onFocusLost.
- Restore: open tabs plus active plus scroll plus selection per group persist per workspace.

### 7.2 Rendering and Performance
- Virtualized lines, folded regions persist per file hash.
- Large file mode over 2 MB or 50k lines: disables minimap, bracket color, word wrap optional, shows banner with Re enable attempt.
- Typing path: no async blocking, no full reparse per keystroke. Symbol reparse debounced 400 ms.
- Scroll: smooth 60 fps target on 10k lines.

### 7.3 Language Support v1
- Rust, C#, TypeScript, JavaScript, JSON, Python, Go, Java, HTML, CSS, Markdown, TOML, YAML, XML, SQL, Bash, Dockerfile.
- Highlight plus brackets plus comments plus fold plus word completions built in. Full LSP comes after v1, but problem matchers from tasks cover build errors.

### 7.4 Edit Commands
- Toggle Comment Ctrl+/, Duplicate Line Ctrl+D style alternative Ctrl+Shift+D to avoid browser clash configurable, Move Line Alt+Up Down, Sort Lines, Trim Whitespace, Format Document Shift+Alt+F with per lang toggle.
- Multi cursor: Ctrl+D select next, Ctrl+U undo selection, Alt+Click add, Ctrl+Alt+Up Down column, Esc clear to single.
- Case: Upper, Lower, Title for selection. Join Lines, Split Lines.
- Go: Go to Line Ctrl+G, Go to Symbol Ctrl+T, Back Alt+Left, Forward Alt+Right, Last Edit Ctrl+K Ctrl+Q style plus palette.

### 7.5 Gutter and Hover
- Gutter shows fold chevrons on hover, diff green plus red plus blue modified, error red dot plus warning yellow dot.
- Hover card: signature, docs first 3 lines, actions Add to Chat plus Copy plus Peek stub.
- Lightbulb for Quick Fix when problem matcher or agent suggests fix. Enter applies with preview.

### 7.6 Rulers and Guides
- Indent guides, bracket pair guides, current line highlight, whitespace dots toggle, rulers at 100 plus 120 optional, minimap 80 px with slider, sticky header for class plus function.

---

## 8. Global Search plus Symbols plus Problems Spec

### 8.1 Global Search
- Query pipeline: normalize, respect ignore, stream results, cap 2000 hits with See More.
- Result row: file icon, path, line number, 2 line preview with highlight, checkbox.
- Replace: preview diff per file, Replace Selected, Replace All with undo stack per workspace. Binary files skipped with count note.
- History: last 20 queries per workspace with star.

### 8.2 Symbol Search
- Sources: tree-sitter index plus open file live parse.
- Kind icons: class, interface, struct, enum, function, method, field, var, const.
- Preview: 5 line snippet plus jump on Enter plus open to side on Ctrl+Enter.

### 8.3 Problems
- Sources in v1: task matchers for dotnet, cargo, tsc, eslint stub, plus agent proposed fixes.
- Table columns: severity icon, message, file, line, col, source, actions.
- Filters: errors only, current file only, search text. Group by file toggle.
- Click: opens file at line col with 3 line reveal. Bulk: Copy All as markdown for chat.

---

## 9. AI Chat plus Agent Spec

### 9.1 Modes in Detail
- Ask mode: read only tools. System prompt forbids diff and exec. Answers cite files. If edit needed it offers Switch to Edit button.
- Edit mode: single turn proposal plus diff. No shell. Apply per hunk or file with undo.
- Agent mode: multi step loop up to 12 steps default configurable. Each step can call one tool batch. GUI approves write plus exec per policy. Loop stops on final answer, approval need, error, or token cap.

### 9.2 Agent Loop State Machine
States: idle, composing prompt, generating, tool proposed, awaiting approval, executing, summarizing, done, error, cancelled.
Transitions logged to Output channel Agent with timestamps.
Cancellation: Stop button sends cancel id, engine aborts sampler within 200 ms, partial text kept with Cancelled badge.

### 9.3 Tool Catalog v1
- `read`: args path, offset, limit. Cap 4000 lines or 200 KB. Returns line numbered text plus hash.
- `glob`: args pattern, root. Returns ranked paths with ignored filter.
- `grep`: args query, include, exclude, regex bool, context lines. Returns hits with file line preview.
- `symbols`: args query, lang. Returns symbol table hits.
- `context_pack`: args files plus query. Returns RAG chunks with scores for dry run.
- `apply_diff`: args file plus unified diff plus base hash. Validates hash, shows hunk UI, returns applied hunks.
- `exec`: args command, cwd, timeout s. Requires approval. Returns exit code plus stdout tail plus stderr tail truncated at 64 KB.
- `download_model`: args hf_repo, filename. Streams progress, not allowed in Ask mode.

Deny patterns: recursive delete root, format, credential exfiltration via curl with token vars, writing outside workspace except `models/` via explicit flow.

### 9.4 Context System and Budget
Default budget 8k tokens:
- System plus instructions: 800 reserved.
- Attached chips: up to 4000, truncated largest first with notice.
- RAG chunks: up to 2000, top 5.
- History: sliding last 8 turns or 1500 tokens, older summarized on demand.
- Free margin: 500.
Meter shows used plus cap plus per chip breakdown on hover. Over cap shows Trim Largest button plus Auto Trim toggle.

Chip types: file, selection range, open tabs bundle, folder summary, symbol, terminal selection, problem selection, docs snippet. Each chip has preview on click and remove X. Drag from explorer adds file chip. Drag selection from editor adds range chip.

### 9.5 Rendering and Actions
- Markdown: headings, lists, tables, task lists, callouts info warning danger, footnotes stub.
- Code fence: header with lang plus filename if known, buttons Copy, Insert at Cursor, Apply as Diff, Open in Split, Add Followup.
- Citations: `[path:line]` clickable, hover shows 3 line preview, Ctrl click opens to side.
- Long answers: collapsible sections per heading over 40 lines with Copy Section.
- Errors: engine offline card with Start Engine plus View Logs plus Retry.

### 9.6 Threads and History
- Thread has id, title auto from first prompt, mode, model, created, updated, message count, tokens used.
- Actions: rename, delete with confirm, export markdown, fork from message, archive.
- Search threads by text plus mode plus model. Per workspace isolation plus global Daily Chat outside workspace.

---

## 10. Rust Engine plus Local GGUF Spec

### 10.1 Module Responsibilities
- `config`: load `quant.json`, env overrides, port, token, paths, model defaults.
- `supervisor`: heartbeat, uptime, graceful shutdown, log tail endpoint.
- `routes`: thin HTTP adapters, validation, SSE writers, error mapping.
- `inference/manager`: single active model lock, load with mmap, unload, idle evict timer, RAM preflight, context accounting.
- `inference/sampler`: temperature, top p, top k, repeat penalty, stop strings, seed, max tokens presets for Chat, Edit, Complete.
- `inference/template`: chat templates per family Qwen, Llama, DeepSeek, StarCoder with fallback generic.
- `inference/embeddings`: tiny model lazy load, batch 32, normalize, cache per chunk hash.
- `tools/*`: safe path jail to workspace roots plus models dir, command allow list plus deny list.
- `util/secrets`: scrubber for `sk-`, `ghp_`, `AKIA`, bearer tokens in logs and transcripts.

### 10.2 Load Policy for Low RAM
- Probe free RAM plus total RAM at load. Estimate = file size times 1.15 plus KV cache for context. KV estimate table: 8k about 0.5 GB for 7B Q4, 32k about 1.8 GB. If estimate over 85 percent free, block with suggestion: smaller quant, smaller context, unload other apps, or use 3B preset.
- mmap true, mlock false, low priority IO during load with progress events every 200 ms.
- Idle unload default 15 min configurable. Manual pin keeps loaded.
- Only one chat model plus one embedding model resident. Embedding unloads first under pressure.

### 10.3 Sampler Presets
- Chat balanced: temp 0.7, top p 0.9, top k 40, repeat 1.1, max 1024.
- Edit precise: temp 0.2, top p 0.9, top k 40, repeat 1.05, max 2048, stop on triple backtick close plus file end marker.
- Complete ghost: temp 0.3, max 96, stop on newline plus brace balance heuristic in GUI.
- Tests verbose: temp 0.5, max 2048.

### 10.4 Hugging Face Download Manager
States: queued, resolving, downloading with bytes plus speed plus ETA, verifying SHA, registering, done, error, cancelled.
Features: resume via Range, 4 MB chunks, checksum verify if provided, partial `.part` file, cancel deletes part on opt, retry with backoff 3 times.
Presets v1:
- `Qwen2.5-Coder-7B-Instruct Q4_K_M` about 4.7 GB, 8k, coding balanced.
- `Llama-3.1-8B-Instruct Q4_K_M` about 4.9 GB, 8k, general discussion.
- `Qwen2.5-Coder-3B Q4_K_M` about 1.9 GB, 4k, low RAM fallback.
Each preset stores repo, file, size, sha256 expected, template family, license note.

### 10.5 Metrics
In memory ring plus `GET /v1/metrics`:
- `requests_total`, `tokens_in_total`, `tokens_out_total`, `ttft_ms_p50_p95`, `tps_out`, `queue_depth`, `ram_mb`, `index_files`, `index_chunks`, `uptime_s`.
GUI polls every 5 s for status bar plus Models panel. Logs rotate daily, keep 7 days.

---

## 11. Index plus RAG Spec

### 11.1 Pipeline Stages
1. Crawl: walk roots, apply ignore, stat plus hash, emit adds, mods, deletes.
2. Read: text detect, skip binary plus over `maxFileMB` 2 MB default, normalize line endings.
3. Chunk: 800 tokens with 120 overlap for code, 600 with 100 for docs. Keep path plus start line plus end line plus hash.
4. Symbols: tree-sitter parse for top languages, store symbol rows with kind plus range plus signature.
5. Embed: batch 32 via tiny model, L2 normalize, store vector plus BM25 terms.
6. Commit: WAL plus fsync, versioned schema, background optimize hourly.

### 11.2 Storage Schema v1 SQLite plus Tantivy or FTS
Tables: `files(path PK, hash, size, mtime, lang, excluded)`, `chunks(id PK, path, start_line, end_line, hash, text)`, `symbols(id PK, path, name, kind, line, col, signature)`, `vectors(chunk_id PK, dim, blob)`, `meta(key, value)`.
Index dir: `<workspace>/.quant/index/` plus global cache for embeddings. Delete index action with rebuild. Corrupt index auto rebuilds with notice.

### 11.3 Retrieval Algorithm
- Parse query intent: file name vs symbol vs concept vs error text.
- Stage 1: BM25 top 30 plus filename trigram top 20 plus symbol exact top 10, merge dedup.
- Stage 2: embed query, cosine top 30, intersect boost if in Stage 1.
- Stage 3: rerank heuristic: open tabs plus 0.3, same folder as active plus 0.2, recent edit plus 0.2, test file penalty for non test query minus 0.1.
- Final: top 5 chunks plus 3 symbols with scores plus reasons for inspector.
- Citation format enforced in system prompt: must cite `path:lines` for every factual code claim.

### 11.4 Quality Gates
- Fixture repo with 10 QnA pairs. 8 of 10 hit expected file in top 3.
- Rename refactor updates symbols without full rescan.
- Index warm refresh under 2 s for 10k unchanged files.

---

## 12. IPC API Contract with Examples

Base URL `http://127.0.0.1:3737`. Header `Authorization: Bearer <token>` after Phase 0.2. Content JSON UTF8. Errors use `{ "error": { "code": "...", "message": "..." } }`.

### 12.1 Health
Request: `GET /health`
Response 200:
```json
{ "status": "ok", "version": "0.1.0", "model_loaded": false, "models_dir": "./models" }
```
Detailed later: `GET /v1/health/detailed` adds cpu, ram_mb, uptime_s, index stats, queue depth.

### 12.2 Models
`GET /v1/models`:
```json
{ "data": [{ "id": "qwen2.5-coder-7b-q4_k_m", "file": "qwen2.5-coder-7b-q4_k_m.gguf", "size_mb": 4710.5, "quantized": true, "context": 8192, "loaded": false }] }
```
`POST /v1/models/load`:
```json
{ "id": "qwen2.5-coder-7b-q4_k_m", "context": 8192 }
```
Responses: 200 loaded, 409 already loaded, 422 insufficient RAM with `suggested` preset id.
`POST /v1/models/unload`: 200 with freed_mb.
`POST /v1/models/download`: body `{ "hf_repo": "...", "filename": "...", "sha256": "optional" }` returns `{ "job_id": "..." }`. Progress via SSE `GET /v1/models/download/progress?job_id=...` events `progress`, `verifying`, `done`, `error`.

### 12.3 Chat
`POST /v1/chat/completions` non streaming:
```json
{
  "model": "qwen2.5-coder-7b-q4_k_m",
  "stream": false,
  "messages": [{ "role": "system", "content": "..." }, { "role": "user", "content": "Explain this file" }],
  "context": { "chips": [{ "type": "file", "path": "src/main.rs" }], "rag_top_k": 5 }
}
```
Response:
```json
{ "id": "chatcmpl-1", "model": "...", "choices": [{ "index": 0, "message": { "role": "assistant", "content": "..." } }], "usage": { "in": 1234, "out": 456 } }
```
`POST /v1/chat/stream` SSE:
```text
data: {"delta":"Hello"}
data: {"delta":" world"}
data: {"usage":{"in":123,"out":45}}
data: [DONE]
```
Cancel: `POST /v1/chat/cancel` body `{ "id": "chatcmpl-1" }`.

### 12.4 Index and Search
`POST /v1/index/refresh` body `{ "roots": ["D:/koding/quant"], "full": false }` returns `{ "job_id": "..." }` plus SSE progress.
`GET /v1/search?q=quant-engine&limit=20` returns files with scores.
`POST /v1/search/code` body `{ "query": "...", "top_k": 5 }` returns chunks plus symbols with `why` reasons.

### 12.5 Tools and Agent
`POST /v1/tools/read` body `{ "path": "...", "offset": 1, "limit": 200 }`.
`POST /v1/tools/grep` body `{ "query": "...", "include": "*.rs", "regex": false }`.
`POST /v1/tools/glob` body `{ "pattern": "src/**/*.rs" }`.
`POST /v1/tools/apply-diff` body `{ "path": "...", "diff": "--- ...", "base_hash": "..." }` returns applied hunks or conflict with `expected_hash`.
`POST /v1/tools/exec` body `{ "command": "cargo test", "cwd": "...", "timeout_s": 120 }` requires `approval_token`.
`POST /v1/agent/run` body `{ "mode": "Agent", "thread_id": "...", "message": "..." }` streams mixed events:
```text
data: {"type":"delta","text":"I will..."}
data: {"type":"tool_proposed","tool":"grep","args":{...}}
data: {"type":"awaiting_approval","receipt":"appr-123"}
data: {"type":"tool_result","ok":true,"preview":"..."}
data: [DONE]
```

### 12.6 Metrics and Logs
`GET /v1/metrics` returns counters plus gauges. `GET /v1/logs/tail?lines=200` returns plain text scrubbed.

---

## 13. Security, Privacy, Permissions

### 13.1 Trust Model
- Workspace trust prompt on first open shows path plus consequences. Trusted enables exec plus tasks plus auto index. Untrusted allows read plus chat without RAG write plus no exec.
- Per action approvals: Read auto, Glob and Grep auto, Apply Diff ask per file, Exec ask per command with cwd plus timeout shown.
- Always for Workspace option scoped to exact workspace id plus command prefix, revocable in Settings plus audit view.

### 13.2 Path Jail and Command Safety
- Engine resolves all paths to canonical and checks prefix against roots plus models dir. Symlink escape blocked with `PATH_ESCAPE` error.
- Exec allow list default: `dotnet`, `cargo`, `npm`, `node`, `python`, `git`, `go`. Deny regex covers `rm -rf /`, `mkfs`, `diskpart`, credential env exfiltration, piping secrets to network.
- Timeout default 120 s, max 600 s. Kill tree on timeout or cancel. Output cap 64 KB tail plus full log file path.

### 13.3 Secrets and Privacy
- Scrubber runs on logs, transcripts, error cards. Patterns for OpenAI keys, GitHub tokens, AWS keys, Bearer tokens, private keys.
- Index stays local. No upload. Export thread strips secrets option default on.
- Telemetry off by default. If enabled later, only counts plus perf, never file content.

---

## 14. Performance plus Memory plus Responsiveness Budgets

### 14.1 Budgets Table
- GUI cold start: target 1.5 s, max 2.5 s. Measured to first editable paint.
- GUI idle no workspace: target 140 MB, max 180 MB.
- GUI 5 files 1k lines: target 220 MB, max 250 MB.
- Engine idle no model: target 90 MB, max 120 MB.
- Index 10k files cold: target 20 s, max 40 s. Warm no change: target 2 s.
- Search warm: p50 120 ms, p95 300 ms.
- Chat TTFT warm 7B Q4 CPU: target 1.5 s, max 3 s. Streaming 15 to 40 tps.
- Inline complete warm: target 600 ms, max 1200 ms.
- Global replace 500 files: progress live, cancel under 500 ms.

### 14.2 Guardrails
- Preflight before model load with clear dialog: required MB, free MB, suggestion preset.
- Large file banner over 2 MB with safe mode.
- Large folder mode over 100k files: manual expand plus indexed search only plus notice.
- Background work yields to typing. Index pauses 800 ms after keystroke in editor.

---

## 15. Testing plus Quality plus Release Gates

### 15.1 GUI Tests
- xUnit for WorkspaceService, Explorer ops with temp dirs, ChatService SSE parsing, Theme persistence.
- Headless Avalonia tests for palette filter ranking, explorer multi select, tab restore.
- Screenshot goldens for Welcome, Main, Palette, Chat, Models, Settings at 1440p dark plus light.

### 15.2 Engine Tests
- `cargo test` for health, models list, chat mock, SSE framing, path jail, diff apply, grep ranking.
- Golden RAG fixture: fixed repo plus 10 queries plus expected top files.
- Criterion benches for crawl, chunk, retrieve. Fail if 20 percent slower than baseline.

### 15.3 E2E Tracks
- T1 Open folder plus CRUD plus undo plus watcher external change.
- T2 Chat stream plus citation click plus apply diff plus undo.
- T3 Download resume plus load plus switch model plus unload.
- T4 Agent fix task on fixture repo with approval plus audit log check.
- T5 Offline mode: kill network, local chat still works, download shows offline error.

### 15.4 Manual Checklist per Phase
Open folder, create file from template, rename with validation, duplicate, delete with undo, drag drop move, copy path set, quick open, global search replace with undo, chat ask plus edit plus agent dry run, load model, index refresh, terminal task run, restart restores all.

---

## 16. Phased Delivery Plan Phase 0 to Phase 14

Rules: small demoable increments. Each sub phase has Objective, Tasks GUI plus Engine, Deliverables, Tests, Exit Gate. No phase merges without gate plus perf snapshot.

### Phase 0: Foundation, Builds, Config, CI
Objective: one command dev boot plus stable config plus green CI.

- 0.1 Solution hygiene and dev scripts
  - Tasks GUI: fix `Quant.slnx`, `Directory.Build.props`, `.editorconfig`, `global.json` pin .NET 10.
  - Tasks Engine: workspace Cargo with `quant-engine` bin, `models/.gitkeep`, log dir creation.
  - Tasks Shared: `scripts/run-dev.ps1` plus `.sh` to boot engine then GUI with port plus token args. `README` quickstart with prereqs.
  - Deliverable: `run-dev` opens GUI connected to engine green dot.
  - Tests: clean clone build script passes on Windows.
  - Gate: `dotnet build` plus `cargo build` green, engine `/health` reachable from GUI status.

- 0.2 Config plus auth plus logging
  - Tasks: `quant.json` schema v1, global plus workspace merge, per launch token file, `--port` override, log rotation.
  - Deliverable: Settings shows effective config plus Copy Diagnostics.
  - Tests: unit merge test, token file permission test.
  - Gate: change port in config updates GUI plus engine without code edit.

- 0.3 CI smoke plus artifacts
  - Tasks: GitHub Actions `build-test.yml` for dotnet build plus cargo test plus clippy plus fmt check. Upload GUI zip plus engine exe as artifacts.
  - Deliverable: badge plus nightly artifact.
  - Tests: pipeline green on main.
  - Gate: PR blocked on red, artifacts downloadable.

- 0.4 Fixture repo plus perf baseline
  - Tasks: `scripts/make-fixture-repo` generates 2k files mixed langs plus long file plus unicode paths. Baseline script records startup, RAM, index time to `perf/baseline.json`.
  - Deliverable: repeatable perf numbers.
  - Gate: baseline recorded on reference machine and stored.

### Phase 1: Premium Shell, Theme, Palette, Feedback
Objective: calm shell that feels premium on first paint.

- 1.1 Shell regions plus persistence
  - Tasks: ActivityBar, Side host, Editor host, Right rail, Bottom host, StatusBar with splitters. Layout save to `settings.json`.
  - Deliverable: resizable regions restore after restart.
  - Tests: headless layout restore test.
  - Gate: manual resize plus restart keeps sizes within 2 px.

- 1.2 Theme service plus tokens
  - Tasks: dark premium plus light pro XAML dictionaries, font size plus family settings, reduced motion, focus ring audit.
  - Deliverable: instant theme switch, AA contrast report.
  - Tests: golden screenshots dark plus light.
  - Gate: design review passes spacing plus radius plus empty states.

- 1.3 Command palette plus quick open core
  - Tasks: command registry with ids, fuzzy scorer with recency boost, file mode with preview, symbol mode stub, footer hints.
  - Deliverable: all Phase 0 plus Phase 1 actions reachable.
  - Tests: ranking unit tests for 50 queries.
  - Gate: 100 percent of implemented commands in palette, open under 50 ms.

- 1.4 Toasts plus dialogs plus empty states plus onboarding stub
  - Tasks: toast stack with undo variant, confirm dialog, input dialog, error card with View Logs, welcome empty window with Open Folder CTA.
  - Deliverable: no silent failure path.
  - Tests: dialog result unit tests.
  - Gate: destructive stub action shows confirm or undo in manual script.

### Phase 2: Workspace plus Explorer Full Actions
Objective: complete file and folder parity.

- 2.1 Workspace model plus trust
  - Tasks: multi root CRUD, MRU with pin, trust prompt, per workspace state save restore.
  - Deliverable: `WorkspaceService` plus tests.
  - Tests: trust gate blocks exec stub, restore test.
  - Gate: reopen restores tabs plus splits plus model selection.

- 2.2 Tree plus filter plus sort plus icons
  - Tasks: virtualized lazy tree, file icons, git dots stub, filter fuzzy, sort menu, Collapse All, Reveal Active File.
  - Deliverable: 50k fixture smooth scroll.
  - Tests: filter perf test, expand lazy test.
  - Gate: filter warm under 300 ms, scroll no freeze.

- 2.3 File CRUD complete
  - Tasks: New File, Template, Open variants, Save family, Rename inline validation, Duplicate, Delete recycle plus permanent, Properties.
  - Deliverable: context menus plus palette plus shortcuts.
  - Tests: temp dir CRUD matrix including unicode plus spaces.
  - Gate: full matrix green on Windows.

- 2.4 Folder CRUD plus favorites plus hide
  - Tasks: New Here set, Rename, Duplicate, Delete with count confirm, Favorites, Hide/Unhide, Copy as Chat context.
  - Deliverable: folder menus plus bulk preview dialog stub.
  - Tests: non empty folder delete confirm test.
  - Gate: manual bulk move 100 files with progress plus cancel.

- 2.5 Clipboard plus drag drop plus conflicts
  - Tasks: Cut Copy Paste cross root, OS drop copy in, internal move/copy with modifiers, conflict dialog Keep Both Replace Skip plus Apply to All.
  - Deliverable: operation journal for undo move.
  - Tests: collision tests for files plus folders.
  - Gate: conflict flow demo passes without data loss.

- 2.6 Reveal plus share plus terminal entry
  - Tasks: Reveal in OS, Copy Path variants, Copy for Chat, Show in Terminal Here.
  - Deliverable: path actions handle long plus unicode.
  - Tests: path unit tests.
  - Gate: manual verify on paths with spaces and `#`.

- 2.7 Watcher plus large repo guards
  - Tasks: native watcher debounce, external change banners, bulk pause pill, gitignore respect, Show Excluded toggle.
  - Deliverable: no freeze on 2k change pull.
  - Tests: simulated external churn test.
  - Gate: memory within budget during churn.

### Phase 3: Editor Pro Tabs Splits Aids
Objective: fast multi tab editing.

- 3.1 Groups plus tabs plus restore
  - Tasks: 3 groups, preview vs pinned, pin, close family, drag move, per group persistence.
  - Deliverable: `EditorService` plus session restore.
  - Tests: 10 tabs 2 splits restore test.
  - Gate: restart restores scroll plus selection.

- 3.2 Highlight plus gutter plus minimap plus zen
  - Tasks: 17 langs v1, bracket pairs, indent guides, minimap toggle, word wrap, sticky scroll, zen mode.
  - Deliverable: language smoke page.
  - Tests: 10k line open perf test.
  - Gate: typing p95 under 16 ms on reference file.

- 3.3 Edit commands pack
  - Tasks: comment, duplicate, move lines, sort, case, join, format stub, trim plus newline settings.
  - Deliverable: Edit menu plus palette plus shortcuts.
  - Tests: command unit tests on sample text.
  - Gate: all commands demo on fixture file.

- 3.4 Navigation pack
  - Tasks: breadcrumbs with pickers, Go to Line, Back Forward, Last Edit, Ctrl+T stub wired to engine later.
  - Deliverable: navigation stack service.
  - Tests: back forward test.
  - Gate: 20 jump flow without lost caret.

### Phase 4: Global Search plus Symbols plus Problems Foundation
Objective: find anything fast.

- 4.1 In file find bar
  - Tasks: regex, case, word, highlight all, next prev with count.
  - Deliverable: bar with keyboard only flow.
  - Tests: regex edge tests.
  - Gate: 10k line find under 100 ms.

- 4.2 Global search panel
  - Tasks: query plus include plus exclude, scope picker, streaming virtual results, history.
  - Deliverable: panel wired to engine `grep` later, local fallback first.
  - Tests: 500 file search test.
  - Gate: first 50 results under 200 ms warm.

- 4.3 Replace with preview plus undo
  - Tasks: per hit checkbox, diff preview, Apply Selected plus All, workspace undo stack.
  - Deliverable: safe replace flow.
  - Tests: replace undo byte exact test.
  - Gate: 500 file replace with progress plus cancel.

- 4.4 Symbols plus breadcrumbs live
  - Tasks: engine `symbols` integration, kind icons, preview, jump plus open to side.
  - Deliverable: Ctrl+T usable on 6 langs.
  - Tests: symbol fixture test.
  - Gate: jump warm under 100 ms.

- 4.5 Problems foundation
  - Tasks: problems table, filters, jump, Copy as Chat context. Sources stub plus task matchers in Phase 9.
  - Deliverable: virtualized 5k rows.
  - Tests: render perf test.
  - Gate: click jumps to line col.

### Phase 5: Chat Panel Polished plus Threads plus Context
Objective: OpenCode quality chat with premium render.

- 5.1 Thread UI plus streaming
  - Tasks: message list virtualized, SSE batch render, Stop, Retry, Copy, token meter, citations clickable.
  - Deliverable: 200 message smooth scroll.
  - Tests: SSE parser plus batch render test.
  - Gate: streaming no layout thrash in manual review.

- 5.2 Markdown plus code actions
  - Tasks: tables, lists, callouts, code toolbar Copy Insert Apply Open in Split, collapsible long sections.
  - Deliverable: renderer with sanitized markdown.
  - Tests: markdown golden tests.
  - Gate: code apply path works from chat.

- 5.3 Context chips plus @ picker plus dry run
  - Tasks: file, selection, tabs, folder, symbol, terminal, problems chips with token counts plus preview plus remove. Dry run inspector shows prompt plus tokens.
  - Deliverable: chip service plus picker.
  - Tests: budget truncation test.
  - Gate: remove chip reduces tokens in inspector.

- 5.4 Slash plus custom prompts
  - Tasks: built in 8 plus `.quant/prompts/*.md` loader with refresh, variable `{{selection}}` `{{file}}`.
  - Deliverable: prompt catalog in Appendix C live.
  - Tests: custom prompt appears test.
  - Gate: `/fix` on selection produces diff proposal.

- 5.5 Threads history plus export
  - Tasks: per workspace threads, search, rename, delete, fork, export markdown with secrets strip option.
  - Deliverable: history drawer.
  - Tests: export scrub test.
  - Gate: 50 threads search instant.

- 5.6 Apply diff per hunk plus undo
  - Tasks: hunk list with Accept Reject, file accept, Copy Diff, undo restores bytes.
  - Deliverable: diff service with hash check.
  - Tests: apply plus undo exact test.
  - Gate: conflict on external change shows Compare flow.

### Phase 6: Rust Engine Real GGUF plus Model UX
Objective: replace mock with local inference.

- 6.1 Model manager plus preflight
  - Tasks: scan plus sidecar meta, RAM estimate, load unload, idle evict, pin, active query.
  - Deliverable: Models panel live.
  - Tests: preflight math tests, unload frees test.
  - Gate: 7B Q4 guidance clear on 8 GB machine.

- 6.2 HF download with resume plus verify
  - Tasks: job states, Range resume, speed ETA, SHA verify, cancel, retry, presets.
  - Deliverable: download dialog with log.
  - Tests: resume test with local stub server.
  - Gate: interrupted download resumes and verifies.

- 6.3 Chat template plus sampler plus stream
  - Tasks: Qwen plus Llama plus generic templates, presets Chat Edit Complete, stop strings, seed, SSE deltas plus usage, cancel in 200 ms.
  - Deliverable: `chat/stream` meets TTFT target warm.
  - Tests: cancel test, stop string test.
  - Gate: Stop halts tokens promptly, partial kept.

- 6.4 Context window plus summarizer stub
  - Tasks: 8k default, sliding keep system plus recent, over cap UI with Trim plus Summarize stub.
  - Deliverable: meter matches engine within 5 percent.
  - Tests: truncation test.
  - Gate: 32k stress does not OOM.

- 6.5 Embeddings tiny model
  - Tasks: lazy load, batch 32, cache per hash, unload first under pressure.
  - Deliverable: RAG ready vectors.
  - Tests: determinism plus cache hit test.
  - Gate: index uses embeddings without blocking chat.

### Phase 7: Index plus RAG plus Citations
Objective: answers grounded in repo.

- 7.1 Crawler plus ignore plus WAL
  - Tasks: walk, gitignore plus quantignore, hash cache, incremental, progress SSE, corrupt auto rebuild.
  - Deliverable: `.quant/index/` with version.
  - Tests: ignore tests, incremental test.
  - Gate: warm refresh 10k files under 2 s.

- 7.2 Chunk plus symbol extraction
  - Tasks: code 800/120, docs 600/100, symbol rows, language detect.
  - Deliverable: inspector data `why` per chunk.
  - Tests: chunk boundary tests.
  - Gate: rename updates without full rescan.

- 7.3 Hybrid retrieval plus rerank
  - Tasks: BM25 plus trigram plus vector merge, open tab boost, recency boost, top 5 plus 3 symbols.
  - Deliverable: `search/code` with scores plus reasons.
  - Tests: 10 QnA fixture, 8 of 10 top 3.
  - Gate: citations required in Edit and Agent answers.

- 7.4 Privacy plus controls
  - Tasks: local only badge, Delete Index, Exclude path UI, max file size setting.
  - Deliverable: settings plus status.
  - Tests: excluded file never retrieved test.
  - Gate: audit confirms no network during RAG.

### Phase 8: Agent Tools plus Approvals plus Audit
Objective: safe multi step agent.

- 8.1 Tool gateway
  - Tasks: read, glob, grep, symbols, context_pack, apply-diff with hash, exec with approval token, download_model gated.
  - Deliverable: OpenAPI plus dry run view.
  - Tests: jail escape tests, hash mismatch test.
  - Gate: all tools unit plus E2E green.

- 8.2 Approval cards plus policy
  - Tasks: tiered Ask, Allow Once, Always for Workspace, Deny with reason, audit log view with replay.
  - Deliverable: right rail approval queue.
  - Tests: deny list tests, Ask never writes fuzz.
  - Gate: dangerous command blocked with alternative suggestion.

- 8.3 Modes plus limits
  - Tasks: Ask Edit Agent switch, max steps 12 configurable, token cap, stop on need input.
  - Deliverable: mode badge plus permission summary.
  - Tests: mode enforcement tests.
  - Gate: Ask fuzz 200 runs zero writes.

- 8.4 Transcripts plus export
  - Tasks: per run JSONL with tool args redacted for secrets, Export markdown, Replay stub.
  - Deliverable: Output Agent channel.
  - Tests: scrub test.
  - Gate: audit shows who approved what and when.

### Phase 9: Bottom Panel Pro Terminal Tasks Output
Objective: daily runner completeness.

- 9.1 Problems live from tasks
  - Tasks: matchers for dotnet, cargo, tsc, python, plus agent fixes entry.
  - Deliverable: error jump plus quick fix stub.
  - Tests: matcher golden logs.
  - Gate: failed build populates Problems with lines.

- 9.2 Output channels plus logs
  - Tasks: Engine, Index, Agent, Tasks channels, level filter, auto scroll, Save log, Copy.
  - Deliverable: engine log tail view.
  - Tests: large log virtual test.
  - Gate: 100k lines without freeze.

- 9.3 Integrated terminal
  - Tasks: shell tabs, split, kill, cwd modes, link detection `path:line`, attach selection to chat.
  - Deliverable: terminal service.
  - Tests: cwd plus link tests.
  - Gate: long task cancel without GUI freeze.

- 9.4 Tasks runner
  - Tasks: `.quant/tasks.json` schema, Run Stop Rerun, problem matcher, one key build.
  - Deliverable: Tasks tab plus palette entries.
  - Tests: task fail to Problems test.
  - Gate: `cargo test` demo jumps to failures.

### Phase 10: Inline Completion Ghost Text
Objective: Copilot style speed, local first.

- 10.1 Engine complete endpoint
  - Tasks: `POST /v1/complete` with prefix, suffix, RAG hints, debounce 250 ms, cache per caret hash.
  - Deliverable: p50 warm under 600 ms.
  - Tests: cache hit test, cancel test.
  - Gate: no blocking typing in profiler.

- 10.2 GUI ghost UX
  - Tasks: gray text, Tab accept, word accept Ctrl+Right, Esc dismiss, Alt+] next, auto dismiss on conflict.
  - Deliverable: settings On Off Manual.
  - Tests: flicker review checklist.
  - Gate: manual fast typing shows no flicker.

- 10.3 Selection quick actions
  - Tasks: floating toolbar on selection: Explain, Fix, Refactor, Add to Chat, Copy.
  - Deliverable: toolbar with shortcuts.
  - Tests: selection flow test.
  - Gate: one click explain from editor works.

### Phase 11: Source Control Pro Stub to Usable
Objective: enough git for daily flow without full client.

- 11.1 Status plus diff stub
  - Tasks: branch plus ahead behind in status bar, changed files list with stage stub, file diff viewer reuse hunk UI.
  - Deliverable: Source activity with refresh.
  - Tests: status parse tests.
  - Gate: 2k changed files list virtualized.

- 11.2 Commit helper
  - Tasks: staged message box, `/commit-msg` from diff, Copy message, Commit stub via git CLI with approval.
  - Deliverable: commit flow with confirm.
  - Tests: message generation test on fixture diff.
  - Gate: commit from IDE works on fixture repo.

- 11.3 History stub
  - Tasks: log list with search, click shows files, Open prior version read only.
  - Deliverable: history view.
  - Tests: log parse test.
  - Gate: open prior version does not overwrite working file.

### Phase 12: Tasks Debug Launch plus Preview
Objective: run plus preview without leaving IDE.

- 12.1 Launch profiles
  - Tasks: `.quant/launch.json` for `dotnet run`, `cargo run`, `npm dev` with env plus args plus cwd, Start Stop Restart, port detect.
  - Deliverable: Run panel with output link.
  - Tests: launch stub test.
  - Gate: one key run plus stop on fixture apps.

- 12.2 Markdown plus HTML preview
  - Tasks: markdown preview split with scroll sync, HTML static preview sandboxed local only.
  - Deliverable: preview tab type.
  - Tests: scroll sync test.
  - Gate: large markdown 5k lines smooth.

- 12.3 Problem matchers plus jump plus re-run
  - Tasks: regex matchers per task, Re-run Failed Only stub, Copy Failure as Chat context.
  - Deliverable: matcher editor with test button.
  - Tests: matcher tests.
  - Gate: click error opens exact line.

### Phase 13: Extensibility plus MCP plus Optional Cloud
Objective: seams for future without bloat.

- 13.1 Internal extension seams
  - Tasks: `ICommandProvider`, `IExplorerMenuProvider`, `IChatToolProvider` with sample internal Echo provider.
  - Deliverable: docs plus sample, no public marketplace yet.
  - Tests: provider registration test.
  - Gate: sample adds palette command without core edit.

- 13.2 MCP client stub
  - Tasks: stdio MCP servers from config, list tools, call with approval, timeouts.
  - Deliverable: MCP settings plus log.
  - Tests: stub server test.
  - Gate: MCP tool appears in dry run.

- 13.3 Optional cloud provider off by default
  - Tasks: provider interface, key storage via OS keychain, per thread override, cost meter stub.
  - Deliverable: disabled by default with clear opt in.
  - Tests: no network when disabled test.
  - Gate: offline user never prompted.

### Phase 14: Hardening, Packaging, Release
Objective: installable premium 1.0.

- 14.1 Perf plus memory pass
  - Tasks: startup trace, virtualize remaining lists, icon cache, index throttle, SSE batch tuning.
  - Deliverable: perf report vs baseline with wins documented.
  - Tests: perf gates in CI warn on 10 percent regression.
  - Gate: all budgets in section 14 met on reference hardware.

- 14.2 Packaging plus updater stub
  - Tasks: Windows MSIX plus portable zip, Linux tar plus AppImage, macOS dmg later. Signed CI artifacts. Update check stub with changelog view.
  - Deliverable: clean install to onboarding under 5 s after index.
  - Tests: install smoke on fresh VM image where possible.
  - Gate: portable run without admin works.

- 14.3 Docs plus support plus diagnostics
  - Tasks: user guide, shortcuts sheet, RAM troubleshooting, AV false positive note, HF license note, Copy Diagnostics bundle.
  - Deliverable: in app Help plus `docs/`.
  - Tests: docs link check.
  - Gate: new user reaches first local answer without external help in usability test of 3 users.

---

## 17. Packaging, First Run, Docs, Support

### 17.1 Install Matrix
- Windows x64: MSIX plus portable zip. SmartScreen note with signing steps.
- Linux x64: tar.gz plus AppImage. Wayland plus X11 smoke.
- macOS arm64 later: dmg with quarantine note.
- Engine binary per OS plus arch. GUI downloads matching engine or bundles it. Bundled preferred for v1 to avoid mismatch.

### 17.2 First Run Flow
1. Welcome with theme plus font.
2. Model preset pick with RAM badges plus Download later option.
3. Open Folder or Try Fixture.
4. 60 second tour: palette, explorer actions, chat attach, apply diff, terminal.
5. Success toast: engine green plus index done plus Try Ask button.

### 17.3 Support Bundle
Copy Diagnostics collects: versions, OS, CPU RAM, config redacted, engine log tail 200 lines, index stats, recent errors. Save as zip for issue reports.

---

## 18. Risks and Mitigations Expanded

- Model quality variance: pin SHAs for presets, show eval notes, allow custom repo with template override.
- CPU speed perception: default small context, ghost max 96 tokens, RAG top 5 cap, clear progress plus cancel plus partial results.
- AvaloniaEdit drift: pin 11.3.0, adapter interface, golden editor tests.
- Tantivy native weight: fallback to SQLite FTS if size or AV issues, feature flag `index.backend`.
- Llama binding build pain on Windows: prebuilt backend option plus clear CMake plus CUDA optional docs, CI builds reference DLLs.
- Scope creep: phase gates block new UI until prior gate passes. New ideas go to Backlog section in issues, not into active phase.
- Data loss fear: recycle first, confirm for bulk, undo journal, orphan tab Save As, external change banners.

---

## 19. Appendix A: Master Shortcut Map

Core:
- Palette `Ctrl+Shift+P`, Quick Open `Ctrl+P`, Symbol `Ctrl+T`, Help `Ctrl+Shift+/` shows shortcut sheet.
- Side `Ctrl+B`, Bottom `Ctrl+J`, Right rail `Ctrl+Alt+B`, Zen `Ctrl+K Z`.
- Explorer focus `Ctrl+Shift+E`, Search `Ctrl+Shift+F`, Chat focus `Ctrl+Alt+C`, Models `Ctrl+Alt+M`.
- New File `Ctrl+N`, New Folder `Ctrl+Shift+N`, Rename `F2`, Delete `Del`, Permanent `Shift+Del`.
- Save `Ctrl+S`, Save All `Ctrl+K S`, Close `Ctrl+W`, Reopen `Ctrl+Shift+T`.
- Find `Ctrl+F`, Replace `Ctrl+H`, Global Replace confirm `Ctrl+Shift+H`, Go to Line `Ctrl+G`.
- Accept inline `Tab`, Dismiss `Esc`, Next suggestion `Alt+]`, Partial accept `Ctrl+Right`.
- Terminal `Ctrl+\`` backtick, New terminal `Ctrl+Shift+\``, Problems `Ctrl+Shift+M`, Output `Ctrl+Shift+U`.
- Agent Stop `Esc` while streaming or `Ctrl+.`, Retry `Ctrl+Enter` in composer when last failed.

All chords editable. Conflicts show warning with Reset action.

---

## 20. Appendix B: Config Schema

`quant.json` v1 draft with defaults:
```json
{
  "version": 1,
  "engine": { "port": 3737, "host": "127.0.0.1", "token": "auto", "idleUnloadMinutes": 15, "autoStart": true },
  "models": { "active": "qwen2.5-coder-7b-q4_k_m", "context": 8192, "keepLoaded": false, "presets": ["qwen2.5-coder-7b-q4_k_m", "llama-3.1-8b-q4_k_m", "qwen2.5-coder-3b-q4_k_m"] },
  "index": { "enabled": true, "backend": "sqlite-fts", "exclude": ["node_modules", "target", "bin", "obj", ".git", "dist", "build"], "maxFileMB": 2, "chunkTokens": 800, "overlap": 120 },
  "agent": { "mode": "Edit", "maxSteps": 12, "allow": { "read": "allow", "write": "ask", "exec": "ask" }, "execAllowList": ["dotnet", "cargo", "npm", "node", "python", "git", "go"] },
  "ui": { "theme": "dark-premium", "fontUi": "Inter", "fontCode": "Cascadia Code", "fontSize": 14, "minimap": false, "autoSave": "afterDelay" },
  "privacy": { "telemetry": false, "exportStripSecrets": true }
}
```
Merge order: defaults, global, workspace, CLI flags. Settings UI edits workspace file with schema validation plus Reset to Default per key.

---

## 21. Appendix C: Prompt and Slash Catalog

Built in:
- `/explain`: explain selection or file with citations, grade level adaptive, no edits.
- `/fix`: diagnose error plus propose diff plus tests to verify.
- `/refactor`: cleaner structure, same behavior, list risks.
- `/tests`: generate xUnit or cargo tests for selection, edge cases included.
- `/docs`: XML or rustdoc comments plus README snippet.
- `/commit-msg`: conventional commits from staged diff.
- `/review`: find bugs plus perf plus security nits with file lines.
- `/plan`: break task into steps with file list before edits.
- `/index-refresh`: force reindex scope workspace or folder.
- `/model-load <id>`: load with preflight.
- `/context-clear`: clear chips plus history window.
- `/perf`: suggest faster alternative with benchmark stub.
- `/translate <lang>`: translate comments plus strings only, never code logic rename without ask.

Custom: `.quant/prompts/*.md` with frontmatter `name`, `mode`, `temp`. Variables `{{selection}}`, `{{file}}`, `{{folder}}`, `{{error}}`. Refresh button loads without restart.

---

## 22. Appendix D: Glossary and Conventions

- Chip: attached context unit with token estimate.
- Thread: chat session with mode plus model plus history.
- Hunk: single diff block with header `@@`.
- Root: top folder in workspace. Multi root allowed.
- Trust: workspace flag gating exec plus tasks.
- TTFT: time to first token. TPS: tokens per second.
- RAG: retrieval augmented generation with citations.
- mmap: memory mapped model load to keep RAM lean.
- Q4_K_M: 4 bit quantized preset balancing size and quality.
- Command id: stable palette string like `file.rename`.
- Gate: required checklist before phase merge.

Naming: GUI `Quant.Desktop`, engine `quant-engine`, config `quant.json`, index dir `.quant/index/`, prompts `.quant/prompts/`, tasks `.quant/tasks.json`.

Commit style: `feat(explorer): add duplicate with conflict dialog`. PR must link phase plus sub phase plus gate evidence plus perf snapshot.

---

End of plan v2. This file is source of truth. Update version on every phase exit. Keep English only. Avoid emdash character in all new edits.
