# MailWhere product architecture and Agent CLI integration research

작성일: 2026-05-16 KST  
범위: `/home/knowgyu/workspace/MailWhere` 현재 repo + imported parent artifacts + official Microsoft/OpenAI docs

## 1. 결론

MailWhere는 이미 PoC치고는 좋은 방향으로 나뉘어 있다. `MailWhere.Core`는 cross-platform domain/analyzer/pipeline, `MailWhere.Storage`는 SQLite, `MailWhere.OutlookCom`은 Windows-only Outlook COM adapter, `MailWhere.Windows`는 WPF tray app/UI로 분리되어 있다. 이 분리는 “위험한 Outlook/Windows 부분은 adapter, 핵심 로직은 core”라는 현재 `docs/ARCHITECTURE.md` 목표와 맞는다.

다만 product 수준으로 확장하려면 다음 4가지를 우선하면 된다.

1. **Composition root 정리**: `MainWindow.xaml.cs`에서 직접 만드는 `SqliteFollowUpStore`, `OutlookComMailSource`, `FollowUpPipeline`, `LlmBackedFollowUpAnalyzer`, timers를 `App`/host/service layer로 빼기.
2. **MVVM-lite 도입**: WPF 화면은 view + binding, 업무 흐름은 ViewModel/Application service로 이동. UI 재작성보다 `DailyBoard`, `ReviewInbox`, `Settings/Diagnostics`, `ScanStatus` 단위부터 점진 분리.
3. **Ports & adapters + strategy registry**: 이미 있는 `IFollowUpAnalyzer`, `ILlmClient`, `IEmailSource`, `IFollowUpStore`를 공식 extension seam으로 만들고, LLM/provider/scanner/storage/notification을 keyed strategy로 등록.
4. **Agent CLI 연계는 제품 코드 강결합보다 read-only export/skill부터**: MailWhere task/review data를 sanitized JSON으로 export하고 Codex/OMX skill이 OfficeWhere/문서검색/리포트 생성과 연결한다. 메일 발송·삭제·이동·답장·첨부 자동 분석은 계속 금지한다.

추천 MVP 이름은 **`where-desk`**다. MailWhere가 “메일에서 생긴 액션/근거”를 만들고, OfficeWhere/Agent CLI가 “관련 문서/변경본/다음 액션”을 찾는 업무 증거 내비게이터가 된다.

## 2. 현재 구조 진단

### 2.1 강점

- **Layering이 이미 있다**: solution은 `Core`, `Storage`, `OutlookCom`, `Windows`, `Tests`, `TestHarness`로 나뉜다 (`MailWhere.sln`, `src/*/*.csproj`).
- **Core가 UI/COM 없이 테스트 가능하다**: `tests/MailWhere.Tests/Program.cs`는 analyzer, pipeline, reminders, settings, SQLite schema/privacy까지 넓게 검증한다.
- **Ports가 일부 존재한다**: `IFollowUpAnalyzer`, `IFollowUpBatchAnalyzer`, `ILlmClient`, `IEmailSource`, `IFollowUpStore`, `IUserNotificationSink`가 이미 있다.
- **Safety boundary가 명확하다**: README/SECURITY/ARCHITECTURE는 read-only Outlook, external LLM off by default, raw body non-persistence, diagnostics sanitization을 반복해서 명시한다.
- **Product roadmap이 있다**: `docs/ROADMAP.md`는 0.2.x false-positive control, 0.3 always-on, 0.4 OfficeWhere bridge, 0.5 provider profiles, 1.0 agentic secretary까지 단계화되어 있다.

### 2.2 확장 리스크

- **`MainWindow.xaml.cs`가 composition root + controller + view state + scheduler + settings + scan orchestration을 동시에 맡는다.** 예: `ScanRecentMailAsync` 안에서 settings 저장, store 생성, analyzer 구성, pipeline/scanner 생성, progress/update/notification까지 처리한다.
- **Timer 기반 background work가 UI object에 묶여 있다.** `_reminderTimer`, `_dailyBoardTimer`, `_automaticScanTimer`가 모두 `DispatcherTimer`라 always-on behavior가 커질수록 테스트/재시작/중복 실행 제어가 어려워진다.
- **LLM/provider strategy가 코드 분기 중심이다.** `BuildAnalyzer`/settings mapping은 잘 시작했지만 provider가 늘면 `MainWindow`와 `RuntimeSettings` 변경 폭이 커진다.
- **SQLite migration은 단순 add-column 방식이라 product schema/version story가 더 필요하다.** 현재는 좋지만 future tables(calendar shadow, audit, provider profile, feedback decisions)가 생기면 versioned migrations와 compatibility tests가 중요해진다.
- **Agent CLI integration artifact는 아직 연구/문서 수준이다.** imported report(`docs/history/parent-omx-import/specs/autoresearch-codex-where-integration/report.md`)는 방향을 제시하지만 repo-native scripts/MCP/skill은 아직 없다.

## 3. 외부 패턴 근거와 MailWhere 적용

### 3.1 Generic Host + DI: WPF composition root를 제품화

Microsoft WPF docs는 Generic Host가 WPF에 기본 내장되지는 않지만 추가할 수 있고, DI/config/logging/hosted services를 쓸 수 있다고 설명한다. WPF startup에서 host를 만들고 service 등록 후 window를 DI로 resolve하며, exit에서 host를 stop/dispose하는 패턴을 제시한다. .NET Generic Host docs도 host가 DI, logging, configuration, app shutdown, hosted services를 묶어 lifetime을 관리한다고 설명한다.

**MailWhere 적용**

- `App.xaml.cs`를 composition root로 승격한다.
- `Host.CreateApplicationBuilder(args)`를 쓰고 다음을 등록한다.
  - `IFollowUpStore` → `SqliteFollowUpStore`
  - `IEmailSource` → `OutlookComMailSource`
  - `IFollowUpAnalyzer` / `IAnalysisService` → settings 기반 factory
  - `MailScanService` / `ReminderService` / `DailyBoardService`
  - `MainWindow`, `DailyBoardWindow`, ViewModels
- `DispatcherTimer` 로직은 UI 표시가 필요한 부분만 남기고, scan/reminder orchestration은 `IHostedService` 또는 application service로 이동한다.
- CLI/test harness도 같은 services를 조합할 수 있게 한다.

**왜 지금 필요한가**

현재 `MainWindow`에 dependency creation이 모여 있기 때문에 기능이 늘 때마다 UI class가 커진다. DI는 “교체 가능한 구현을 interface 뒤에 두고 container가 생성/수명 관리를 맡기는” 공식 .NET 패턴이며, MailWhere의 LLM provider, storage, mail source, notification sink에 잘 맞는다.

### 3.2 MVVM-lite: WPF를 product UI로 유지보수 가능하게 만들기

Microsoft MVVM Toolkit은 WPF를 포함한 여러 UI framework에서 사용할 수 있는 modular MVVM library이고, `ObservableObject`, `RelayCommand`, `AsyncRelayCommand`, messaging 등을 제공한다. source generator는 observable properties와 commands boilerplate를 줄일 수 있고, 점진 도입이 가능하다고 명시되어 있다.

**MailWhere 적용**

가장 좋은 순서:

1. `ScanStatusViewModel`: scan progress, busy state, stop command, LLM telemetry summary.
2. `ReviewInboxViewModel`: approve/ignore/snooze/open source commands.
3. `DailyBoardViewModel`: today/7d/30d/unknown filters, two-column task/calendar grouping.
4. `SettingsViewModel`: runtime settings, LLM provider/model loading, diagnostics command.

처음부터 전체 WPF를 갈아엎지 말고, 기존 XAML/code-behind event handler를 ViewModel command로 하나씩 옮긴다. `CommunityToolkit.Mvvm`은 optional dependency라 product complexity 대비 효과가 크지만, dependency 추가가 부담되면 우선 hand-written `INotifyPropertyChanged` + command class로 시작해도 된다.

### 3.3 Ports & Adapters / Clean Architecture-lite

Microsoft architecture guidance는 Clean Architecture를 project organization approach로 설명하고, DDD-oriented architecture에서 domain이 infrastructure에 의존하지 않도록 layer dependency를 관리하는 방향을 제시한다. MailWhere는 desktop monolith이므로 microservice식 DDD를 그대로 가져오면 과하다. 대신 “Clean Architecture-lite / hexagonal style” 정도가 적절하다.

**MailWhere 적용 layer proposal**

```text
MailWhere.Core
  Domain/                 pure records/value objects/policies
  Analysis/               analyzer contracts + deterministic rule analyzer
  Application/            use-cases: ScanMail, ApproveReview, BuildDailyBoard
  Ports/                  IEmailSource, IFollowUpStore, ILlmClient, IClock, INotificationSink

MailWhere.Infrastructure.Storage
  SQLite implementation + migrations

MailWhere.Infrastructure.Outlook
  Outlook COM adapter + STA executor + opener

MailWhere.Infrastructure.Llm
  Ollama/OpenAI-compatible clients + provider registry

MailWhere.Windows
  WPF views/viewmodels + composition root

MailWhere.Cli or tools/
  sanitized export, test probes, agent/OfficeWhere bridge helper
```

Repo rename can be gradual. The important change is not folder purity; it is that `Application` use-cases depend on ports, and adapters depend inward.

### 3.4 Strategy / keyed services for analyzers and providers

Current code already has strategy seeds:

- `IFollowUpAnalyzer` with rule-based and LLM-backed implementations.
- `ILlmClient` with Ollama/OpenAI-compatible clients.
- `LlmProviderKind` and `LlmFallbackPolicy`.

.NET DI now supports keyed services; official docs show multiple implementations registered with keys and resolved by key. For MailWhere, keyed strategy lets provider additions avoid large switch blocks.

**MailWhere 적용**

```text
IAnalyzerFactory
  Create(RuntimeSettings settings): IFollowUpAnalyzer

ILlmClientFactory
  Create(LlmEndpointSettings settings): ILlmClient

Provider registry rows:
  Disabled
  OllamaNative
  OpenAiChatCompletions
  OpenAiResponses
  Future: ApprovedInternalGateway
```

Do not add a complex plugin system before provider profile requirements are real. A simple registry/factory is enough for 0.3~0.5.

### 3.5 State machine for task/review/scan lifecycle

Product화에서 중요한 것은 “무엇이 왜 떴고, 사용자가 어떻게 정리했고, 다시 뜨면 안 되는가”다. 현재 state는 `tasks`, `review_candidates`, `processed_sources`에 흩어져 있고, methods는 충분하지만 lifecycle diagram이 없다.

**추천 state machine**

```text
Mail source
  -> scanned
  -> ignored | duplicate | review_open | task_open

review_open
  -> approved_task
  -> dismissed_not_task
  -> snoozed_review -> review_open
  -> suppressed_reanalyzed

task_open
  -> done/dismissed
  -> due_changed
  -> source_redacted
```

Scan state:

```text
idle -> preparing -> reading_outlook -> analyzing_batch -> persisting -> summarizing -> idle
                         |                 |              |
                         v                 v              v
                    read_warning      llm_retryable   partial_success
```

이 state machine을 문서화하고 tests에 고정하면 false-positive control, feedback loop, always-on watcher가 쉬워진다.

### 3.6 Outlook identity: EntryID/StoreID는 안정 ID가 아니라 reference hint

Microsoft Outlook docs explain that `EntryID` is assigned by the MAPI store and can change when an item moves folders/stores or is exported/imported. They also recommend specifying both item `EntryID` and folder `StoreID` with `GetItemFromID`; otherwise the default store is searched.

**MailWhere 적용**

- 현재 `SourceIdHash`는 dedupe/privacy 용도로 계속 적절하다.
- `SourceId`로 Outlook 원본 열기를 지원하되, product docs에 “best-effort reference”로 명시한다.
- 더 견고한 future identity는 `(EntryID, StoreID, ConversationId, receivedAt, subjectCore hash)` 식 composite reference를 고려한다.
- 원본을 못 열면 task/review는 깨지면 안 된다. “원본 메일 위치가 바뀌어 열 수 없음” diagnostic/action을 제공한다.

## 4. Product로 만들기 위한 우선순위 roadmap

### 4.0 현재 제품 범위 결정(2026-05-16)

이번 구현 범위는 “메일을 대신 처리하는 agent”가 아니라 **놓치면 곤란한 후속조치를 적은 클릭으로 관리하는 foreground assistant**로 고정한다.

- **Daily Brief**: 앱/Windows 시작 후 바로 띄우지 않고 기본 10분 지연 뒤 foreground로 올린다. 내용은 오늘 신경 쓸 `내가 할 일`과 `기다리는 중`만 압축한다.
- **업무 보드**: Daily Brief와 달리 활성 항목 전체 원장이다. 전체/오늘/7일/30일/기한 미정 필터와 `내가 할 일`/`기다리는 중` 2열을 유지한다.
- **메일 소스**: `기다리는 중`을 실제로 만들 수 있도록 Outlook COM은 read-only로 Inbox와 Sent Items를 함께 읽는다.
- **알림**: due-day/overdue/snooze-due 같은 interrupt-worthy 항목만 toast로 올리고, 일반 waiting 상태는 보드/brief에서 확인한다.
- **검토 후보**: 낮은 확신 후보는 기본 화면에 섞지 않는다. 사용자가 `검토 후보 보기`나 검토 후보을 열 때만 처리한다.
- **액션**: `열기`, `기한`, `나중에 보기`, `완료`, `숨김`만 1차 제품 액션으로 둔다. 답장 초안, 자동 발송/삭제/이동/회신은 범위 밖이다.
- **테스트 ergonomics**: fallback/rule scan 결과를 AppData에서 직접 지우지 않도록 앱의 문제 해결 버튼과 `scripts/reset-local-data.ps1`을 제공한다. 기본 reset은 settings를 유지하고 local task/review/processed-source DB만 삭제한다.
- **Agent CLI/skill/hook**: 지금은 설계 경계만 남긴다. 구현은 sanitized export/read-only skill부터 시작하고 MCP/full work-agent는 future work다.

### P0 — 지금 바로: product architecture guardrails

Deliverables:

- `docs/PRODUCT_ARCHITECTURE_AND_AGENT_CLI.md`(이 문서) 유지.
- `docs/ARCHITECTURE.md`에 “recommended next architecture” 요약 추가.
- Test command를 README에 명확히: `./scripts/verify-static.sh`, `.tools/dotnet/dotnet run --project tests/MailWhere.Tests/MailWhere.Tests.csproj --no-restore`, Windows publish path.
- Release checklist: build, test, static Outlook mutation grep, privacy smoke, manual Windows Outlook smoke.

### P1 — Composition root / application services

Goal: 기능 추가 전 `MainWindow` 비대화를 멈춘다.

Tasks:

1. Add `MailWhere.Core/Application` use-case services:
   - `ScanMailUseCase`
   - `ReviewCandidateUseCase`
   - `DailyBoardUseCase`
   - `SettingsUseCase` or settings service in Windows layer
2. Add `MailWhere.Windows/Composition` with service registration.
3. Introduce Generic Host in `App.xaml.cs`.
4. Keep existing UI behavior with regression tests.

Acceptance:

- `MainWindow` no longer directly constructs `OutlookComMailSource`, `FollowUpPipeline`, `SqliteFollowUpStore`, `LlmBackedFollowUpAnalyzer`.
- WPF build and current tests pass.

### P2 — MVVM slices and state machine docs/tests

Goal: UI flows become maintainable.

Tasks:

- Extract `ReviewInboxViewModel` first because review actions are product-critical.
- Extract `ScanStatusViewModel` second because background/LLM progress is fragile.
- Document lifecycle state machine and add tests for review/task transitions.

Acceptance:

- Review approve/ignore/snooze/open actions are command-level testable without WPF window.
- State transition tests cover duplicate, stale LLM failure suppression, source-derived deletion, snooze.

### P3 — Always-on reliability

Goal: app can run all day without surprise.

Tasks:

- Move automatic scan/reminder/day board scheduling to hosted/application services.
- Add “scan lease” to prevent overlapping scans across timer/manual triggers.
- Add local notification history/quiet hours table.
- Add failure counters/circuit breaker for Outlook COM and LLM endpoint.

Acceptance:

- No duplicate notification storm after restart.
- Outlook/LLM failures degrade to manual mode with clear diagnostic code.

### P4 — Product data model

Goal: schema evolution no longer ad hoc.

Tasks:

- Add `schema_version` app_state and explicit migration list.
- Add audit table for user-visible local mutations: task created/dismissed, review approved/ignored, due changed, source redacted.
- Add feedback/suppression table for false positives independent of source-wide redaction.
- Add optional shadow calendar table.

Acceptance:

- Fresh install and old schema migration tests pass.
- Audit never stores raw body or full address list.

### P5 — Agentic secretary foundation, not mutation

Goal: agent can explain and propose, not perform risky mailbox actions.

Tasks:

- Define tool registry with side-effect labels: `read`, `suggest`, `local-mutate`, `external-mutate`.
- Keep Outlook mailbox operations read-only until a separate explicit approval workflow exists.
- Add proposal/audit log for agent suggestions.

Acceptance:

- Agent can cite evidence and explain why it proposed a reminder/task.
- No mail send/delete/move/reply path exists.

## 5. Agent CLI / Codex / OMX integration design

### 5.1 MVP: `where-desk` skill + read-only scripts

Create a repo-local or user-local Codex/OMX skill:

```text
.codex/skills/where-desk/SKILL.md
scripts/export-mailwhere-context.py
scripts/where-brief.py
```

The skill should:

- Read MailWhere SQLite through a sanitized exporter.
- Never export raw body, attachment content, full address lists, prompt logs, or API keys.
- Optionally call OfficeWhere search API or a local document search helper.
- Produce markdown + JSON briefing artifact.
- Refuse/avoid send/delete/move/reply mail actions.

CLI UX examples:

```text
$where-desk 오늘 할 일과 관련 문서 찾아서 우선순위 브리프 만들어줘
$where-desk 검토 후보 중 진짜 내 액션일 가능성이 높은 것만 근거와 함께 묶어줘
$where-desk MailWhere task export를 OfficeWhere 검색어로 변환해줘
```

Output shape:

```md
## 오늘의 업무 증거 브리프

### 1. [D-1] 예산안 회신
- MailWhere: task/review id, due, reason, evidence snippet
- OfficeWhere query: "예산안" "Q2" "회신"
- Related docs: file path/id/snippet/hash
- Suggested next action: open/review/ask-human
- Not performed: mail mutation, source document edit, raw attachment analysis
```

### 5.2 `mailwhere export` helper

Recommended implementation can be Python first because it is read-only and cross-tool friendly.

```bash
python scripts/export-mailwhere-context.py \
  --db "$LOCALAPPDATA/MailWhere/followups.sqlite" \
  --include tasks,review \
  --limit 50 \
  --json
```

Output fields:

```json
{
  "items": [
    {
      "kind": "task",
      "id": "...",
      "title": "...",
      "due_at": "...",
      "reason": "...",
      "evidence_snippet": "...",
      "source_received_at": "...",
      "source_recipient_role": "Direct"
    }
  ],
  "omitted_fields": ["raw_body", "attachments", "full_addresses", "prompt_logs"]
}
```

Source IDs should be excluded by default and enabled only with explicit `--include-source-id`, because opening Outlook original is user-visible and may reveal sensitive mail context.

### 5.3 Agent CLI as MCP server: phase 2

OpenAI Codex config supports project-scoped `.codex/config.toml` when the project is trusted, skill config entries, hooks, MCP server definitions, app/connectors controls, and native agent configuration. This is a good fit for a local MailWhere MCP server later.

MCP tools could be:

- `mailwhere.list_tasks({status, due_window, limit})`
- `mailwhere.list_review_candidates({limit, include_snoozed})`
- `mailwhere.get_task_context({task_id})`
- `mailwhere.export_brief({format})`
- `mailwhere.open_source_mail({task_id})` — **prompt/approval required**, visible action only

Do **not** expose mutation tools in the first MCP version. Later local mutations can be added with side-effect labels and approval:

- `mailwhere.dismiss_task` → local-mutate, confirmation required.
- `mailwhere.approve_review_candidate` → local-mutate, confirmation required.
- No `send_mail`, `delete_mail`, `move_mail`.

### 5.4 Hooks: only safety and routing

Use hooks for lightweight routing/safety, not heavy scans.

Good hooks:

- Prompt routing: if prompt includes MailWhere/OfficeWhere/where-desk/메일 할 일/관련 문서, suggest or activate `where-desk`.
- PreToolUse guard: block dangerous `sqlite3 ... DELETE/UPDATE` against MailWhere/OfficeWhere DB unless an explicit local-mutate workflow is active.
- Stop-hook validator for `where-desk`: ensure final briefing says what was not performed and where evidence came from.

Avoid:

- SessionStart full DB scan.
- Automatic Outlook COM access from hook.
- Hook-based external LLM calls.

### 5.5 Native agent: `where-researcher`

After helper scripts are stable, add a native read-only agent:

```toml
name = "where-researcher"
description = "Read-only MailWhere + OfficeWhere evidence researcher for Codex CLI briefings."
model_reasoning_effort = "medium"
developer_instructions = "Use where-desk scripts only. Stay read-only. Never send/reply/delete/move mail or edit source Office documents. Cite task IDs, file IDs, paths, and snippets."
```

Use it when many tasks need document evidence in parallel. For one task, skill scripts are enough.

### 5.6 Plugin: when distribution matters

When the pieces stabilize, ship a local plugin:

```text
mailwhere-codex-plugin/
├── .codex-plugin/plugin.json
├── skills/where-desk/SKILL.md
├── agents/where-researcher.toml
├── hooks/hooks.json
├── mcp/mailwhere-server/
└── README.md
```

This allows one install/update path for skills, agents, hooks, and MCP config.

## 6. Concrete next implementation backlog

### Backlog A — repo hygiene / product management

1. Add `docs/PRODUCT_ARCHITECTURE_AND_AGENT_CLI.md` from this report.
2. Add release checklist doc: build/test/static/privacy/manual smoke.
3. Add issue labels/milestones mapped to roadmap: `architecture`, `productization`, `privacy`, `agent-cli`, `false-positive`, `always-on`.
4. Keep `.omx` runtime artifacts ignored, but version important research under `docs/history` or `docs/`.

### Backlog B — architecture refactor, behavior-preserving

1. `Application/ScanMailUseCase` wraps analyzer/pipeline/scanner.
2. `Application/ReviewCandidateUseCase` wraps approve/ignore/snooze/open-data retrieval.
3. `Application/DailyBoardUseCase` wraps daily board planner/store access.
4. WPF `MainWindow` delegates to services.
5. Add tests around each use-case using fake `IEmailSource`, fake `IFollowUpAnalyzer`, in-memory/temp SQLite store.

### Backlog C — Agent CLI MVP

1. `scripts/export-mailwhere-context.py` read-only SQLite export.
2. `scripts/where-brief.py` builds markdown briefing from exported context and optional OfficeWhere results.
3. `.codex/skills/where-desk/SKILL.md` references those scripts.
4. Fixture tests ensure no raw body/address/attachment fields leak.
5. Document usage in README.

### Backlog D — Agent CLI phase 2

1. Build `MailWhere.Cli` or `MailWhere.Tools` .NET console wrapper if Python export becomes insufficient.
2. Add local MCP server exposing read-only tools.
3. Add project-scoped `.codex/config.toml` sample, not enabled blindly.
4. Add read-only `where-researcher` native agent.

## 7. Decision table

| Question | Recommendation | Reason |
| --- | --- | --- |
| Full Clean Architecture rewrite? | No | Current project is desktop monolith; use Clean Architecture-lite around use-cases/ports. |
| Add MVVM framework? | Yes, but gradual | `MainWindow` is the main maintainability risk. CommunityToolkit.Mvvm is lightweight and WPF-compatible. |
| Add Generic Host? | Yes | Gives DI/config/logging/hosted services and cleaner lifetime for always-on behavior. |
| Add MediatR/CQRS package? | Not now | Commands/queries can be plain application service methods first. Add mediator only if cross-cutting pipelines become real. |
| Add plugin system inside MailWhere? | Not now | Provider registry/factory is enough until external extensions are needed. |
| Agent CLI integration via direct DB? | MVP yes, read-only sanitized export only | Fastest path; avoids product coupling. |
| Agent CLI integration via MCP? | Phase 2 | Better long-term tool interface but not needed before export contract stabilizes. |
| Mail mutation tools? | No | Violates current safety contract; defer to future explicit approval design. |

## 8. Validation evidence

Commands run in this repo during this workflow:

- `./scripts/verify-static.sh` → OK.
- `.tools/dotnet/dotnet run --project tests/MailWhere.Tests/MailWhere.Tests.csproj --no-restore` → all tests printed `PASS`, exit 0.
- `.tools/dotnet/dotnet build src/MailWhere.Windows/MailWhere.Windows.csproj --no-restore -v minimal` → Build succeeded, 0 warnings, 0 errors.
- Import verification from prior Ralph step: parent artifacts copied, full-day unrelated logs replaced with filtered MailWhere/Outlook excerpts, architect re-verification APPROVE.

## 9. Sources

Repo-local:

- `docs/ARCHITECTURE.md`
- `docs/ROADMAP.md`
- `docs/PROJECT_CONTEXT.md`
- `docs/history/parent-omx-import/specs/autoresearch-codex-where-integration/report.md`
- `src/MailWhere.Windows/App.xaml.cs`
- `src/MailWhere.Windows/MainWindow.xaml.cs`
- `src/MailWhere.Core/Pipeline/FollowUpPipeline.cs`
- `src/MailWhere.Core/Storage/IFollowUpStore.cs`
- `src/MailWhere.Storage/Schema.cs`

External primary/official:

- Microsoft Learn — .NET dependency injection: https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/overview
- Microsoft Learn — .NET Generic Host: https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host
- Microsoft Learn — Use the .NET Generic Host in a WPF app: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/app-development/how-to-use-host-builder
- Microsoft Learn — MVVM Toolkit: https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/
- Microsoft Learn — MVVM source generators: https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/overview
- Microsoft Learn — Common web app architectures / Clean Architecture reference: https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures
- Microsoft Learn — DDD-oriented microservice layering/CQRS concepts: https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/ddd-oriented-microservice
- Microsoft Learn — Outlook EntryID/StoreID: https://learn.microsoft.com/en-us/office/vba/outlook/How-to/Items-Folders-and-Stores/working-with-entryids-and-storeids
- Microsoft Learn — Outlook MailItem.EntryID: https://learn.microsoft.com/en-us/office/vba/api/outlook.mailitem.entryid
- OpenAI Codex config reference: https://developers.openai.com/codex/config-reference
