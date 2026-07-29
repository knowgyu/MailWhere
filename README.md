# MailWhere

<p align="center">
  <img src="assets/app-icon.svg" alt="MailWhere" width="128" />
</p>

MailWhere는 Windows tray에 조용히 상주하면서 Classic Outlook 메일에서 놓치기 쉬운 **할 일, 일정성 항목, 회신 대기, 마감 리마인드**를 로컬 업무 보드로 모아주는 read-only 보조 앱입니다.

핵심 방향은 “메일을 대신 처리하는 agent”가 아닙니다. Outlook 원본은 그대로 두고, MailWhere 안에서만 업무 후보를 `열기`, `나중에`, `보관`으로 정리합니다. 잘못 뽑힌 제목/기한은 카드를 더블클릭해 바로잡습니다.

## 현재 제품 모델

- **Tray-first**: 자동 시작은 tray만 띄우고, 사용자가 직접 실행하면 업무 보드를 바로 엽니다.
- **지정 시간 업무 보드**: 기본 08:00에 오늘 업무 보드를 열어 빠르게 훑게 합니다. 보드 열기에 실패한 경우에만 알림으로 fallback합니다.
- **통합 업무 보드**: tray의 `열기`가 곧 업무 보드입니다. 기본은 `오늘`이고 필터는 `오늘`/`이번 주`/`전체`/`날짜 없음` 순서입니다.
- **한 줄 업무 행**: 업무는 “제목 · 날짜 · 보낸 사람”만 먼저 보이고, 오른쪽에 `열기`, `나중에`, `보관`만 둡니다. 제목/기한은 더블클릭으로 수정합니다.
- **분리된 보조 화면**: 확인 필요, 메일 검색, 보관함은 별도 창으로 열고 설정/개발자 도구는 설정 창 탭으로 열어 업무 목록을 방해하지 않습니다.
- **보관 모델**: 여러 종료/제외 액션을 사용자-facing 개념으로 나누지 않고 `보관`으로 통합합니다. 보관된 항목은 active 목록에서 사라지고 자동으로 다시 뜨지 않지만, 보관함에서 열거나 복원할 수 있습니다.

## 지금 되는 것

- Classic Outlook COM 기반 read-only Inbox/Sent Items 메일 읽기
- **지금 메일 확인**: Inbox/Sent 검색 mirror는 전체 이력을 이어서 동기화하고, 업무 후보 분석은 기본 최근 30일(설정 1/7/30/90일)을 확인
- **새 메일 자동 확인**: Outlook ItemAdd 이벤트를 감지해 짧게 debounce하고, 놓친 이벤트는 자동 확인 간격의 cursor scan으로 보정
- 자동 확인은 Inbox/Sent Items cursor를 분리해 한쪽 실패가 다른 쪽 성공 시각을 잘못 앞당기지 않음
- 자동 delta 확인에서는 중복/이미 처리한 source를 body/LLM 분석 전에 fast filter로 제거하고, 애매한 메일은 계속 분석 대상으로 유지
- 규칙 기반 업무 후보 탐지와 선택형 LLM 분석
- Ollama native `/api/chat`, OpenAI-compatible `/v1/chat/completions`, `/v1/responses` endpoint 지원
- endpoint 모델 목록 불러오기와 LLM 연결 테스트
- LLM 시도/성공/fallback/실패 요약과 확인 필요 창의 **실패한 AI 분석 다시 시도**
- 답장/전달 메일, To/CC 수신 여부, 담당자 표현을 보수적으로 판단
- 같은 스레드의 동일 업무 후보 중복 생성 억제
- 낮은 확신/LLM 실패 후보는 기본 업무 보드에 섞지 않고 별도 검토 후보 창에서 처리
- 기다리던 메일의 회신/내 확인 답장은 확인 필요 창 안에서 `보관(Y)`/`유지(N)`로 처리
- 업무 카드 더블클릭으로 제목과 기한 수정
- `나중에`로 지정 시각까지 active 목록에서 제외하고, 시간이 지나면 다시 표시
- `보관`으로 active 목록에서 제외하고, 보관함에서 원본 열기/복원
- 가능한 경우 `열기`로 Outlook 원본 메일 열기
- **지금 메일 확인**으로 채워지는 로컬 mail mirror를 SQLite/FTS5만 읽는 WPF **메일 검색** 창과 `search-mail` CLI 검색
- 다자 수신자에게 보낸 회신 요청은 Outlook 대화 ID/보낸 사람 기준으로 `n/m명 회신` 현황 표시 및 export
- 향후 LLM skill이 읽을 수 있는 raw-mail-free export SDK/API (`MailWhereExportService`)
- Codex/where-skills 같은 외부 자동화가 안전하게 읽을 수 있는 read-only JSON CLI provider (`MailWhere.Cli.exe`)
- D-day, D-7/D-1/D-day reminder planning, snooze-due reminder
- MailWhere 자체 우하단 toast stack과 tray 메뉴
- 설정 > 개발자 도구 탭의 샘플 데이터/알림/필터 테스트와 `scripts/reset-local-data.ps1`
- GitHub Actions Windows portable zip 빌드

## 안전 기본값

- Outlook 메일 발송, 삭제, 이동, 읽음 처리, 답장 자동화 없음
- 첨부파일 자동 분석 없음
- vendor-specific mailbox export 파일 직접 parsing 없음
- 외부/endpoint LLM 기본 OFF
- 업무/검토/export에는 raw mail body와 prompt 로그 저장 없음
- 메일 검색 mirror를 켠 경우 normalized plain-text body가 `%LOCALAPPDATA%\MailWhere\followups.sqlite`의 FTS5 corpus에 로컬 저장됨
- 새 메일 자동 확인은 설정에서 켜되, 앱이 안전 조건을 만족할 때만 read-only로 동작

## 다운로드해서 실행

GitHub Actions artifact 또는 Release zip을 받아 압축을 풀고 아래 파일을 실행합니다.

```text
MailWhere.exe
```

zip 안의 `START_HERE_시작하기.txt`를 먼저 읽는 것을 권장합니다.

## Read-only CLI provider

portable zip에는 UI 앱 `MailWhere.exe`와 별도로 `MailWhere.Cli.exe`가 포함됩니다. CLI는 `MailWhere.Core`/`MailWhere.Storage`만 사용하며 Outlook COM, WPF, mailbox 열기/변경, schema 초기화를 수행하지 않습니다. 기본 DB는 `%LOCALAPPDATA%\\MailWhere\\followups.sqlite`이고, 없는 DB는 JSON 오류 `database-not-found`와 exit code `2`로 끝나며 DB/WAL/SHM 파일을 만들지 않습니다.

```powershell
.\MailWhere.Cli.exe health --json
.\MailWhere.Cli.exe manifest --json
.\MailWhere.Cli.exe export --json --db "$env:LOCALAPPDATA\MailWhere\followups.sqlite"
.\MailWhere.Cli.exe list-tasks --json --status open --due-window 7d --limit 50
.\MailWhere.Cli.exe list-review-candidates --json --limit 25
```

모든 응답은 `provider: "MailWhere"`, `contract_version: "v1"`, `app_version`, `generated_at`, `ok`를 포함하는 JSON envelope입니다. 성공은 exit code `0`, 예상 가능한 사용 불가 상태는 `2`, 사용법 오류는 `64`, 예기치 못한 실패는 `70`입니다. CLI JSON은 raw body, source id/hash, evidence snippet, 전체 수신자 목록, prompt logs, API keys를 내보내지 않습니다. `search-mail`은 일반 export와 별도의 명시적 검색 명령이며 Outlook을 열지 않고 SQLite FTS5 mirror만 읽어 160자 이하 snippet과 `can_open_source` flag만 반환합니다. StoreID/EntryID는 출력하지 않습니다.

## 기본 사용 흐름

1. `MailWhere.exe`를 직접 실행하면 오늘 기준 업무 보드가 열리고 앱은 tray에도 상주합니다. Windows 자동 시작은 tray-only로 실행됩니다.
2. tray 메뉴의 **열기**로 언제든 같은 업무 보드를 다시 엽니다.
3. **지금 메일 확인**으로 전체 Inbox/Sent 검색 mirror를 이어서 동기화하고, 최근 메일에서는 로컬 업무 후보를 만듭니다.
4. 지정 시간에 열리는 **오늘 업무 보드**를 훑고, 필요하면 tray의 **오늘 업무 보기**로 다시 엽니다.
5. 업무 행에서 `열기`, `나중에`, `보관`으로 정리하고, 제목/기한은 더블클릭으로 바로잡습니다. 보관한 항목은 **보관함**에서 다시 열거나 복원합니다.

## Windows 개발/검증

```powershell
cd MailWhere
.\scripts\verify-windows.ps1
.\scripts\publish-portable.ps1
```

Linux/CI-like 환경에서는 repo-local SDK가 있을 때 아래 검증을 사용합니다.

```bash
.tools/dotnet/dotnet build MailWhere.sln -v:minimal
.tools/dotnet/dotnet run --project tests/MailWhere.Tests/MailWhere.Tests.csproj
PATH="$PWD/.tools/dotnet:$PATH" dotnet run --project src/MailWhere.Cli/MailWhere.Cli.csproj -c Release -- manifest --json
PATH="$PWD/.tools/dotnet:$PATH" scripts/verify-static.sh
```

테스트 중 로컬 업무/검토 데이터를 지우고 다시 시작하려면 설정 > 개발자 도구의 **로컬 업무 데이터 삭제**를 누르거나 아래 스크립트를 실행합니다. 기본은 `%LOCALAPPDATA%\\MailWhere\\followups.sqlite*`만 삭제하고 설정은 유지합니다.

```powershell
.\scripts\reset-local-data.ps1
```

portable 출력 예:

```text
artifacts/MailWhere-v0.12.0-win-x64-portable.zip
```

## LLM endpoint

기본은 AI 분석 OFF라 로컬 규칙 기반 분석만 사용합니다. AI 분석을 켜면 **AI가 먼저 분석**하고, 실패하면 기본적으로 검토 후보에 남깁니다. 규칙 기반 fallback은 사용자가 명시적으로 선택하거나 실패 후 모달에서 동의한 경우에만 켭니다. 인증이 필요한 OpenAI-compatible endpoint는 설정 창에서 `인증 없음`, `API 키 입력`, `환경 변수에서 읽기` 중 하나를 고릅니다.

- `LlmOnly`: LLM 실패 시 자동 등록하지 않고 검토 후보에 남김(기본)
- `LlmThenRules`: LLM 실패 시 규칙 기반 analyzer로 fallback

```json
{
  "ExternalLlmEnabled": true,
  "LlmProvider": "OllamaNative",
  "LlmEndpoint": "",
  "LlmModel": "",
  "LlmTimeoutSeconds": 90,
  "LlmFallbackPolicy": "LlmOnly",
  "LlmInitialConcurrency": 1,
  "LlmMaxConcurrency": 1
}
```

vLLM 같은 OpenAI-compatible local endpoint는 `LlmProvider`를 `OpenAiChatCompletions` 또는 `OpenAiResponses`로 설정합니다. 기본 모델명은 비워두고, 앱의 설정 창에서 **모델 불러오기** 버튼으로 `/api/tags` 또는 `/v1/models`에서 목록을 가져와 선택하는 흐름을 권장합니다. **연결 테스트**는 메일 내용이 아닌 작은 JSON probe만 보냅니다. 자세한 내용은 [`docs/LLM_ENDPOINTS.md`](docs/LLM_ENDPOINTS.md)를 참고하세요.

## 팀 기본 설정 seed

portable 폴더에 `MailWhere.defaults.json`을 같이 두면, 사용자별 설정 파일이 아직 없을 때 첫 실행에서 그 값을 기본 설정으로 복사합니다. 릴리즈에는 `MailWhere.defaults.sample.json`만 포함되며, 실제 endpoint/model 값은 배포자가 sample을 복사해 수정하세요. API key나 개인 토큰은 이 파일에 넣지 않는 것을 권장합니다.

`새 메일 자동 확인`은 한 번 수동 확인이 성공해 안전 gate가 열린 뒤에만 동작합니다. 켜져 있으면 Outlook 새 항목 이벤트로 빠르게 확인하고, 이벤트가 누락되거나 앱/Outlook이 쉬고 있던 동안의 메일은 설정한 자동 확인 간격마다 cursor scan으로 보정합니다. 첫 자동 확인 이후에는 마지막 성공 시각에 짧은 overlap을 더한 구간만 다시 읽어 전체 기간을 매번 훑지 않습니다.

## 문서

문서 지도는 [`docs/README.md`](docs/README.md)를 먼저 보세요.

- 시작 안내: [`docs/START_HERE.ko.txt`](docs/START_HERE.ko.txt)
- 구조: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
- 프로젝트 맥락: [`docs/PROJECT_CONTEXT.md`](docs/PROJECT_CONTEXT.md)
- Visual QA 결정: [`docs/VISUAL_QA_WORKFLOW_2026-05-16.md`](docs/VISUAL_QA_WORKFLOW_2026-05-16.md)
- 배포: [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md)
- 보안: [`docs/SECURITY.md`](docs/SECURITY.md)
- 로드맵: [`docs/ROADMAP.md`](docs/ROADMAP.md)

상위 workspace에서 진행된 초기 기획/인터뷰/검증 산출물은 [`docs/PROJECT_CONTEXT.md`](docs/PROJECT_CONTEXT.md)에 정리했습니다. 원본 import manifest와 전체 복사본은 [`docs/history/parent-omx-import/`](docs/history/parent-omx-import/)에 있습니다.
