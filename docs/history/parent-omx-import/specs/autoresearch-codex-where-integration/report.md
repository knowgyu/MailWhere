# MailWhere + OfficeWhere를 Codex CLI에서 강하게 쓰는 방향

작성일: 2026-05-16 KST  
범위: `/home/knowgyu/workspace/MailWhere`, `/home/knowgyu/workspace/OfficeWhere`, `CODEX_SKILLS_HOOKS_AGENTS_GUIDE.md` 로컬 근거 기반 read-only 분석

## 1. 결론

두 앱을 단순히 “메일 앱” + “문서 검색 앱”으로 따로 쓰기보다, Codex CLI 안에서는 **업무 증거 내비게이터**로 묶는 것이 가장 강하다.

- **MailWhere**는 “내가 해야 할 일/검토할 일/마감/메일 근거”를 안전하게 만든다.
- **OfficeWhere**는 그 일과 관련된 로컬/공유 폴더의 Office/PDF 문서를 검색·비교·중복 탐지한다.
- **Codex CLI**는 둘 사이를 연결해 “메일에서 생긴 일 → 관련 문서 찾기 → 변경본/중복/근거 정리 → 다음 액션 제안”을 수행한다.

추천 MVP는 제품 코드를 바로 강결합하지 않고, **`where-desk` Codex skill + read-only helper scripts**로 시작하는 것이다. 이후 안정화되면 plugin/MCP/agent/hook으로 확장한다.

## 2. 근거: MailWhere가 제공하는 표면

### 2.1 제품 성격

MailWhere README는 Windows 11 상주형 업무 보드 PoC이며, “메일을 대신 조작하는 agent”가 아니라 메일에서 놓치기 쉬운 후속조치를 모으는 **read-only 비서**라고 정의한다 (`MailWhere/README.md:7`). 현재 기능은 Outlook COM read-only 메일 읽기, rule/LLM 기반 action item 탐지, SQLite task 저장, 검토 후보, 업무 보드, Outlook 원본 열기, D-day/reminder, tray/toast 등을 포함한다 (`MailWhere/README.md:11-39`).

안전 기본값도 명확하다. 메일 발송/삭제/이동/답장/첨부 자동 분석이 없고, 외부/endpoint LLM은 기본 OFF다 (`MailWhere/README.md:41-45`). 보안 문서는 raw mail body가 SQLite task schema에 없고, evidence snippet은 제한·삭제 가능하며, Phase 0/1은 Outlook mailbox를 mutate하지 않는다고 못박는다 (`MailWhere/docs/SECURITY.md:3-10`).

### 2.2 내부 구조와 데이터

구조는 위험한 Windows/Outlook 부분을 adapter로 격리한다 (`MailWhere/docs/ARCHITECTURE.md:3-12`). 핵심 레이어는 다음이다.

- `MailWhere.Core`: domain/analyzer/gates/pipeline
- `MailWhere.Storage`: SQLite persistence
- `MailWhere.OutlookCom`: Windows-only Classic Outlook COM read adapter
- `MailWhere.Windows`: WPF tray app/UI

Codex가 가장 안전하게 읽을 수 있는 데이터는 app-owned SQLite의 task/review 데이터다.

- `LocalTaskItem`은 `Title`, `DueAt`, `SourceIdHash`, optional `SourceId`, `Confidence`, `Reason`, `EvidenceSnippet`, status, sender/received/recipient role, kind를 가진다 (`MailWhere/src/MailWhere.Core/Domain/LocalTaskItem.cs:12-29`).
- source-derived deletion은 title/reason/evidence/source id/sender/received/recipient role을 제거하거나 redaction한다 (`MailWhere/src/MailWhere.Core/Domain/LocalTaskItem.cs:31-58`).
- SQLite schema의 `tasks`와 `review_candidates`는 제목, 기한, source hash/id, confidence, reason, evidence snippet, sender/received/recipient role, suppressed/resolution 등을 저장한다 (`MailWhere/src/MailWhere.Storage/Schema.cs:5-44`).
- 실제 DB는 `%LocalAppData%/MailWhere/followups.sqlite`에 해당한다. 코드상 app data directory는 `Environment.SpecialFolder.LocalApplicationData/MailWhere`이고 store는 `followups.sqlite`를 연다 (`MailWhere/src/MailWhere.Windows/WindowsRuntimeDiagnostics.cs:105-113`, `MailWhere/src/MailWhere.Windows/MainWindow.xaml.cs:908-918`).

### 2.3 이미 계획된 OfficeWhere 연결성

MailWhere 문서에는 이미 OfficeWhere bridge가 roadmap에 있다. Phase 0.4는 “OfficeWhere search handoff for task-related documents”와 “Read-only task export for OfficeWhere indexing”를 포함하고, acceptance에 “Document search handoff does not store full mail body in another index”가 있다 (`MailWhere/docs/ROADMAP.md:142-154`). UX/integration review도 단기 검색 handoff, 중기 JSONL/SQLite view export, 장기 local protocol/CLI bridge를 제안하며, 메일 본문 전체를 OfficeWhere index에 자동 투입하지 말라고 한다 (`MailWhere/docs/UX_AND_INTEGRATION_REVIEW.md:65-70`).

즉, Codex CLI 통합은 제품 방향과 충돌하지 않는다. 다만 raw body/첨부 자동 분석/메일 mutation은 금지해야 한다.

## 3. 근거: OfficeWhere가 제공하는 표면

### 3.1 제품 성격

OfficeWhere는 흩어진 Excel/Word/PowerPoint/PDF 문서를 찾고, 수정본 변경 내용과 이름만 다른 동일 내용 문서를 확인하는 desktop app이다 (`OfficeWhere/README.md:17-44`). 지원 범위는 `.xlsx`, `.docx`, `.pptx`, `.pdf` 검색이며, Excel/Word/PowerPoint는 변경 이력 비교도 지원한다 (`OfficeWhere/README.md:46-51`). 원본 보호 원칙도 명확해서 원본 문서는 복사·수정·삭제하지 않고 app data만 저장한다 (`OfficeWhere/README.md:39-44`, `OfficeWhere/docs/architecture.md:20-26`).

### 3.2 내부 구조와 API

OfficeWhere는 Electron shell + React/Vite renderer + FastAPI backend + SQLite app data 구조다 (`OfficeWhere/docs/architecture.md:5-14`). Codex CLI 입장에서는 FastAPI backend가 가장 좋은 접점이다.

주요 API 표면:

- `POST /api/search`: filename/content/combined 검색, file type filter, modified range, excluded folders, result limit 지원 (`OfficeWhere/backend/api/search.py:166-229`).
- search schema는 query, limit, file_limit, file_types, search_scope, modified_from/to, excluded_folder_paths를 받으며 결과는 file_id/name/path/file_type/location/snippet/hash 정보다 (`OfficeWhere/backend/models/schemas.py:330-360`).
- `POST /api/files`: 경로를 등록하고 index chunks/comparison artifacts를 저장한다 (`OfficeWhere/backend/api/files.py:132-238`).
- `POST /api/files/{file_id}/open`, `/show-in-folder`: 등록 파일 열기/폴더 표시 요청을 보낸다 (`OfficeWhere/backend/api/files.py:333-356`).
- `/api/library/groups` 계열은 version family/duplicate 같은 문서 묶음 요약과 detail을 제공한다 (`OfficeWhere/backend/models/schemas.py:452-506`).

실행 접점:

- backend-only entrypoint `backend_server.py`는 `--host`, `--port`, `--data-dir`, `--log-level` 또는 `OW_*` env를 받는다 (`OfficeWhere/backend_server.py:1-78`).
- 직접 실행 기본 포트는 `127.0.0.1:18765` (`OfficeWhere/backend_server.py:61-66`).
- packaged Electron은 고정 포트를 쓰지 않고 사용 가능한 loopback port를 골라 `backendBaseUrl = http://127.0.0.1:{port}`로 둔 뒤 IPC bridge `app:get-backend-base-url`을 통해 renderer에 전달한다 (`OfficeWhere/frontend/electron/main.ts:164-165`, `OfficeWhere/frontend/electron/main.ts:1215-1272`).

따라서 Codex CLI용 helper는 **명시적 `OFFICEWHERE_BASE_URL`**을 우선 사용하고, 없으면 개발용 `127.0.0.1:18765` health check를 시도하며, 장기적으로 OfficeWhere가 backend URL discovery file을 app data에 남기는 방식이 좋다.

## 4. Codex/OMX 확장 표면 근거

로컬 가이드는 다음 원칙을 준다.

- 반복 작업은 skill, 자동 검사/기록/차단은 hook, 독립 큰 작업은 agent, 팀 배포는 plugin (`CODEX_SKILLS_HOOKS_AGENTS_GUIDE.md:11-29`).
- 현재 로컬 skill은 `~/.codex/skills/*/SKILL.md`, native agent는 `~/.codex/agents/*.toml`, hook은 `~/.codex/hooks.json`에 있다 (`CODEX_SKILLS_HOOKS_AGENTS_GUIDE.md:36-50`).
- 좋은 조합은 “반복 repo 분석 = Skill + read-only agent”, “위험 명령 차단 = Hook”, “팀 배포 = Plugin”이다 (`CODEX_SKILLS_HOOKS_AGENTS_GUIDE.md:264-275`).
- hook은 secret 탐지, session start 상태 주입, stop 시 검증 의무 확인, tool 사용 전 위험 명령 차단처럼 빠르고 결정적인 일에 적합하다. 모든 prompt 재작성이나 long-running command 자동 실행은 피해야 한다 (`CODEX_SKILLS_HOOKS_AGENTS_GUIDE.md:277-295`).
- agent는 독립 module 분석, 서로 다른 관점의 review, 문서 조사와 로컬 코드 탐색 분리에 좋다 (`CODEX_SKILLS_HOOKS_AGENTS_GUIDE.md:297-311`).
- 여러 skill/MCP/hook을 같이 배포해야 하면 plugin 구조가 적절하다 (`CODEX_SKILLS_HOOKS_AGENTS_GUIDE.md:582-605`).

## 5. 추천 설계: `where-desk` skill first

### 5.1 사용자 경험

Codex CLI에서 이런 프롬프트를 받으면 skill이 켜지는 형태가 좋다.

```text
$where-desk 오늘 MailWhere 할 일 기준으로 관련 OfficeWhere 문서 찾아서 다음 액션 정리해줘
$where-desk 이 메일 업무랑 관련된 최신 PPT/Excel 찾아줘
$where-desk 검토 후보 후보 중 문서 근거가 있는 것만 묶어줘
```

출력은 “자동으로 다 처리”가 아니라 **근거 briefing**이어야 한다.

```md
## 오늘의 업무 증거 브리프

### 1. [D-1] A팀 예산안 회신
- MailWhere: task id, due, sender/received, reason/evidence snippet
- OfficeWhere 검색어: "A팀 예산안", "Q2 budget"
- 관련 문서:
  1. 예산안_v3.xlsx — Excel — cell match: ...
  2. 회의자료_최종.pptx — PowerPoint — filename match: ...
- Codex 제안:
  - 먼저 v3.xlsx와 최종.pptx를 열어 변경점 확인
  - 메일 원본은 MailWhere에서 열기
- 금지/미수행: 메일 발송, 원본 문서 수정, 첨부 자동 분석 없음
```

### 5.2 skill 구조

권장 위치는 현재 세션 기준 `~/.codex/skills/where-desk/`이다. portable/plugin화를 염두에 두면 repo 형태로도 보관할 수 있다.

```text
where-desk/
├── SKILL.md
├── scripts/
│   ├── mailwhere_export.py
│   ├── officewhere_search.py
│   ├── where_brief.py
│   └── validate_fixture.py
└── references/
    ├── data-boundaries.md
    ├── mailwhere-schema.md
    └── officewhere-api.md
```

`SKILL.md` 핵심 계약:

- Use when user asks to connect mail-derived tasks/reminders/review candidates with local OfficeWhere document search/comparison inside Codex CLI.
- Do not use for sending/replying/deleting/moving mail, editing source Office documents, indexing raw mail body, or credential-gated external production work.
- Always read MailWhere data through sanitized export and OfficeWhere through read-only search/list/group APIs.
- Default output is a briefing artifact, not mutation.

### 5.3 helper scripts

#### `mailwhere_export.py`

역할: MailWhere SQLite를 read-only로 열어 open tasks/review candidates만 sanitized JSON으로 내보낸다.

입력:

- `--db PATH` optional. 없으면 Windows `%LOCALAPPDATA%/MailWhere/followups.sqlite` 추정.
- `--include review|tasks|all`
- `--limit N`
- `--json`

출력 예:

```json
{
  "source": "MailWhere",
  "db_path": ".../followups.sqlite",
  "items": [
    {
      "kind": "task",
      "id": "...",
      "title": "...",
      "due_at": "...",
      "reason": "...",
      "evidence_snippet": "...",
      "source_sender_display": "...",
      "source_received_at": "...",
      "recipient_role": "Direct"
    }
  ],
  "omitted": ["raw_body", "addresses", "attachments"]
}
```

주의:

- MailWhere DB schema에 raw body는 없지만, 그래도 exporter는 body/address/attachment 필드를 절대 만들지 않는다.
- source_id는 기본 출력에서 숨기고 `--include-source-id` 같은 명시 옵션으로만 내보낸다. 원본 메일 열기는 사람-visible action이어야 한다.

#### `officewhere_search.py`

역할: OfficeWhere backend에 health/search/list/group 요청을 보낸다.

입력:

- `--base-url` or `OFFICEWHERE_BASE_URL`
- 없으면 `http://127.0.0.1:18765` health check
- `search --query Q --file-types xlsx,pptx --scope filename_content --file-limit 10`
- `groups --kind version_family --q Q`

주의:

- packaged Electron은 동적 포트라 CLI에서 자동 discovery가 현재 약하다. MVP는 `OFFICEWHERE_BASE_URL` 또는 개발 backend fixed port를 요구한다.
- 장기적으로 OfficeWhere가 app data에 `backend-url.json` 같은 discovery file을 쓰면 CLI 통합성이 크게 좋아진다.

#### `where_brief.py`

역할: MailWhere item에서 검색어를 만들고 OfficeWhere 검색 결과를 묶어 markdown/JSON briefing을 만든다.

알고리즘:

1. `mailwhere_export.py`로 open tasks/review candidates 읽기.
2. 각 item의 `title + reason + evidence_snippet`에서 query candidates 추출.
3. OfficeWhere `/api/search`에 filename_content 검색.
4. 결과를 file_id/name/type/location/snippet/hash 기준으로 dedupe.
5. due date, recipient role, confidence, OfficeWhere match type을 기준으로 priority score 계산.
6. Markdown + machine-readable JSON sidecar 출력.

검증:

- fixture SQLite + fake OfficeWhere API response로 deterministic test.
- raw body/address/attachment-like field가 출력에 없는지 snapshot 검사.

## 6. Hook/agent/plugin은 언제 추가하나

### 6.1 Hook: MVP에는 최소만

Hook은 강하지만 예측성이 떨어지면 방해가 된다. 그래서 MVP에서는 skill 명시 호출만으로 충분하다.

추가한다면 다음 두 개만 추천한다.

1. **UserPromptSubmit keyword routing**
   - “MailWhere”, “OfficeWhere”, “관련 문서”, “메일 할 일”, “where-desk”가 같이 나오면 `$where-desk` 힌트를 넣는다.
   - heavy scan/API call은 하지 않는다.

2. **PreToolUse safety guard**
   - `where-desk` 컨텍스트에서 raw mail export, source Office 문서 destructive command, OfficeWhere DB 직접 삭제 같은 패턴을 막거나 경고한다.
   - 예: `rm`, `del`, `sqlite3 ... DELETE`, `UPDATE tasks`, `UPDATE registered_files`, source folder bulk move/delete.

SessionStart에서 전체 DB scan을 매번 하는 hook은 비추천이다. 대신 존재 여부만 빠르게 감지해 “where-desk 사용 가능” 정도만 주입할 수 있다.

### 6.2 Agent: `where-researcher`는 Phase 1

MVP skill이 안정화된 뒤 native agent를 추가한다.

```toml
name = "where-researcher"
description = "Read-only agent that connects MailWhere task context with OfficeWhere document search evidence and produces action briefings."
model_reasoning_effort = "medium"
sandbox_mode = "read-only"
developer_instructions = "Stay read-only. Use where-desk scripts for MailWhere/OfficeWhere access. Never send/reply/delete/move mail or edit source Office documents. Cite task IDs, file IDs, paths, and snippets."
```

용도:

- 여러 MailWhere task를 독립적으로 조사할 때 병렬 subagent로 나누기.
- 하나는 MailWhere task clustering, 하나는 OfficeWhere document evidence, 하나는 final briefing 검증처럼 분리.

주의: 사용자가 단순히 한 건만 묻는 경우 agent는 과하다. skill script만 사용한다.

### 6.3 Plugin: 배포 단위가 되면

`where-desk`가 skill + scripts + optional agent + optional hooks + maybe MCP로 커지면 plugin으로 묶는다.

```text
where-plugin/
├── .codex-plugin/plugin.json
├── skills/where-desk/SKILL.md
├── agents/where-researcher.toml
├── hooks/hooks.json
├── .mcp.json
└── assets/
```

## 7. 더 강한 형태: local MCP server

장기적으로 가장 강한 형태는 Codex가 직접 tool call로 접근하는 local MCP server다.

권장 tool boundary:

- `mailwhere.list_tasks({status, due_window, limit})` — read-only sanitized task list
- `mailwhere.list_review_candidates({limit})` — read-only sanitized candidates
- `mailwhere.open_original_mail({task_id})` — 사람-visible local action, 기본은 확인 필요
- `officewhere.search({query, file_types, scope, limit})` — read-only search API wrapper
- `officewhere.list_groups({kind, q})` — read-only version/duplicate groups
- `officewhere.show_in_folder({file_id})` — 사람-visible local action, 기본은 확인 필요
- `where.brief({task_ids, due_window})` — combined briefing

금지 tool:

- 메일 send/reply/delete/move/read-state mutation
- source Office 문서 수정/삭제/이동
- raw mail body indexing/upload
- 승인 없는 외부 LLM 업로드

MCP를 바로 MVP로 하지 않는 이유는, 먼저 DB/API 경계와 UX가 안정되어야 하기 때문이다. skill scripts가 그 전단계로 적합하다.

## 8. 제품 쪽에 추가하면 좋은 작은 seam

제품 코드를 바꾸는 단계에서 가장 효과 큰 작은 변경은 다음이다.

### MailWhere

1. **read-only export CLI/API**
   - `MailWhere.exe --export-tasks-json` 또는 별도 `mailwhere-export`.
   - SQLite schema 직접 의존보다 안정적.
   - 출력은 task/review candidate sanitized fields만.

2. **task keyword/query field**
   - LLM/rule analyzer가 `OfficeWhereSearchHints: string[]`를 저장하면 Codex 검색 품질이 오른다.
   - raw body 대신 짧은 키워드/프로젝트명/문서명 후보만 저장.

3. **source id handling policy**
   - source_id는 원본 메일 열기용 local handle이므로 기본 export에서는 숨긴다.

### OfficeWhere

1. **backend discovery file**
   - Electron 시작 시 app data에 `{ "base_url": "http://127.0.0.1:PORT", "pid": ..., "started_at": ... }`를 저장.
   - Codex CLI/helper가 동적 포트 app에도 붙을 수 있다.

2. **CLI search wrapper**
   - `officewhere search --query ... --json`이 FastAPI 또는 backend-only entrypoint를 감싼다.
   - Codex skill이 HTTP details를 몰라도 된다.

3. **external read-only source type: MailWhere task export**
   - 중기에는 MailWhere JSONL export를 OfficeWhere가 “문서”가 아니라 “업무 context source”로 참조할 수 있다.
   - 단, raw mail body는 금지.

## 9. 구현 우선순위

### P0: 지금 바로 만들 가치가 있는 것

- `~/.codex/skills/where-desk/SKILL.md`
- `scripts/mailwhere_export.py` with fixture test
- `scripts/officewhere_search.py` with `OFFICEWHERE_BASE_URL` and `127.0.0.1:18765` fallback
- `scripts/where_brief.py` producing markdown/json artifact
- validation fixture: fake MailWhere DB + fake OfficeWhere response

이 단계는 MailWhere/OfficeWhere 제품 repo를 수정하지 않아도 된다.

### P1: Codex CLI 편의성 강화

- `where-researcher` native agent
- keyword routing hook for Korean/English prompts
- safety guard hook for destructive/source-mutating commands in where context
- output cache under `.omx/artifacts/where-desk/`

### P2: 제품 seam 추가

- MailWhere read-only export CLI/API
- OfficeWhere backend discovery file or CLI wrapper
- OfficeWhere search API에 “task context” style query presets 추가

### P3: Plugin/MCP

- skill + scripts + agent + hooks를 plugin으로 포장
- local MCP server로 tool call 제공
- UI action handoff: open mail/open file/show folder는 explicit confirmation boundary 유지

## 10. 최종 권고

가장 좋은 이름은 `where-desk` 또는 `workwhere`다. 기능 정의는 다음 한 줄이 적절하다.

> MailWhere가 만든 메일 기반 업무 context를 읽고, OfficeWhere의 로컬 문서 검색/비교 증거를 붙여, Codex CLI에서 안전한 다음 액션 브리프를 만드는 read-only workflow.

바로 hook부터 만들지 말고 skill+script로 시작해야 한다. 이유는 세 가지다.

1. 두 앱 모두 user data를 다루므로 자동 hook side effect가 위험하다.
2. OfficeWhere packaged backend port discovery가 아직 CLI 친화적이지 않다.
3. MailWhere는 raw body를 저장하지 않는 점이 장점이므로, 먼저 sanitized export contract를 명확히 해야 한다.

이 설계는 MailWhere의 read-only/원문 최소화 정책과 OfficeWhere의 원본 문서 read-only 정책을 유지하면서, Codex CLI 안에서는 “메일 업무 ↔ 관련 문서 ↔ 변경/중복 증거 ↔ 다음 행동”까지 연결한다.
