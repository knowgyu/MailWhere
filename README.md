# MailWhere

<p align="center">
  <img src="assets/app-icon.svg" alt="MailWhere" width="128" />
</p>

MailWhere는 Windows tray에 조용히 상주하면서 Classic Outlook 메일에서 놓치기 쉬운 **할 일, 일정성 항목, 회신 대기, 마감 리마인드**를 로컬 업무 보드로 모아주는 read-only 보조 앱입니다.

핵심 방향은 “메일을 대신 처리하는 agent”가 아닙니다. Outlook 원본은 그대로 두고, MailWhere 안에서만 업무 후보를 `열기`, `나중에`, `보관`으로 정리합니다. 잘못 뽑힌 제목/기한은 카드를 더블클릭해 바로잡습니다.

## 현재 제품 모델

- **Tray-first**: 앱 시작 시 메인 창을 오래 띄워두는 구조가 아니라 tray에서 조용히 동작합니다.
- **지정 시간 업무 보드**: 기본 08:00에 오늘 업무 보드를 열어 빠르게 훑게 합니다. 보드 열기에 실패한 경우에만 알림으로 fallback합니다.
- **통합 업무 보드**: tray의 `열기`가 곧 업무 보드입니다. 기본은 `오늘`이고 필터는 `오늘`/`이번 주`/`날짜 없음`/`전체` 순서입니다.
- **한 줄 업무 행**: 업무는 “제목 · 날짜 · 보낸 사람”만 먼저 보이고, 오른쪽에 `열기`, `나중에`, `보관`만 둡니다. 제목/기한은 더블클릭으로 수정합니다.
- **분리된 보조 화면**: 검토 후보, 설정, 개발자 도구는 main 탭이 아니라 별도 창으로 열어 업무 목록을 방해하지 않습니다.
- **보관 모델**: 여러 종료/제외 액션을 사용자-facing 개념으로 나누지 않고 `보관`으로 통합합니다. 보관된 항목은 active 목록에서 사라지고 자동으로 다시 뜨지 않습니다.

## 지금 되는 것

- Classic Outlook COM 기반 read-only Inbox/Sent Items 메일 읽기
- **지금 메일 확인**: 기본은 최근 30일, 갯수 제한 없이 날짜 기준으로 확인
- 규칙 기반 업무 후보 탐지와 선택형 LLM 분석
- Ollama native `/api/chat`, OpenAI-compatible `/v1/chat/completions`, `/v1/responses` endpoint 지원
- endpoint 모델 목록 불러오기와 LLM 연결 테스트
- LLM 시도/성공/fallback/실패 요약과 retryable 실패 후보 처리
- 답장/전달 메일, To/CC 수신 여부, 담당자 표현을 보수적으로 판단
- 같은 스레드의 동일 업무 후보 중복 생성 억제
- 낮은 확신/LLM 실패 후보는 기본 업무 보드에 섞지 않고 별도 검토 후보 창에서 처리
- 업무 카드 더블클릭으로 제목과 기한 수정
- `나중에`로 지정 시각까지 active 목록에서 제외하고, 시간이 지나면 다시 표시
- `보관`으로 active 목록에서 제외하고 다시 자동 표시하지 않음
- 가능한 경우 `열기`로 Outlook 원본 메일 열기
- D-day, D-7/D-1/D-day reminder planning, snooze-due reminder
- MailWhere 자체 우하단 toast stack과 tray 메뉴
- 개발자 도구 창의 샘플 데이터/알림/필터 테스트와 `scripts/reset-local-data.ps1`
- GitHub Actions Windows portable zip 빌드

## 안전 기본값

- Outlook 메일 발송, 삭제, 이동, 읽음 처리, 답장 자동화 없음
- 첨부파일 자동 분석 없음
- vendor-specific mailbox export 파일 직접 parsing 없음
- 외부/endpoint LLM 기본 OFF
- raw mail body와 prompt 로그 저장 없음
- 수동 확인 성공 전 새 메일 자동 확인 비활성

## 다운로드해서 실행

GitHub Actions artifact 또는 Release zip을 받아 압축을 풀고 아래 파일을 실행합니다.

```text
MailWhere.exe
```

zip 안의 `START_HERE_시작하기.txt`를 먼저 읽는 것을 권장합니다.

## 기본 사용 흐름

1. `MailWhere.exe`를 실행하면 앱이 tray에 상주합니다.
2. tray 메뉴의 **열기**로 오늘 기준 업무 보드를 엽니다.
3. **지금 메일 확인**으로 최근 메일을 읽어 로컬 업무 후보를 만듭니다.
4. 지정 시간에 열리는 **오늘 업무 보드**를 훑고, 필요하면 tray의 **오늘 업무 보기**로 다시 엽니다.
5. 업무 행에서 `열기`, `나중에`, `보관`으로 정리하고, 제목/기한은 더블클릭으로 바로잡습니다.

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
PATH="$PWD/.tools/dotnet:$PATH" scripts/verify-static.sh
```

테스트 중 로컬 업무/검토 데이터를 지우고 다시 시작하려면 아래 스크립트를 실행합니다. 기본은 `%LOCALAPPDATA%\\MailWhere\\followups.sqlite*`만 삭제하고 설정은 유지합니다.

```powershell
.\scripts\reset-local-data.ps1
```

portable 출력 예:

```text
artifacts/MailWhere-v0.4.0-win-x64-portable.zip
```

## LLM endpoint

기본은 AI 분석 OFF라 로컬 규칙 기반 분석만 사용합니다. AI 분석을 켜면 **AI가 먼저 분석**하고, 실패하면 기본적으로 검토 후보에 남깁니다. 규칙 기반 fallback은 사용자가 명시적으로 선택하거나 실패 후 모달에서 동의한 경우에만 켭니다. 인증이 필요한 OpenAI-compatible endpoint는 설정 창에서 `인증 없음`, `API 키 입력`, `환경 변수에서 읽기` 중 하나를 고릅니다.

- `LlmOnly`: LLM 실패 시 자동 등록하지 않고 검토 후보에 남김(기본)
- `LlmThenRules`: LLM 실패 시 규칙 기반 analyzer로 fallback

```json
{
  "ExternalLlmEnabled": true,
  "LlmProvider": "OllamaNative",
  "LlmEndpoint": "http://localhost:11434",
  "LlmModel": "",
  "LlmTimeoutSeconds": 90,
  "LlmFallbackPolicy": "LlmOnly"
}
```

vLLM 같은 OpenAI-compatible local endpoint는 `LlmProvider`를 `OpenAiChatCompletions` 또는 `OpenAiResponses`로 설정합니다. 기본 모델명은 비워두고, 앱의 설정 창에서 **모델 불러오기** 버튼으로 `/api/tags` 또는 `/v1/models`에서 목록을 가져와 선택하는 흐름을 권장합니다. **연결 테스트**는 메일 내용이 아닌 작은 JSON probe만 보냅니다. 자세한 내용은 [`docs/LLM_ENDPOINTS.md`](docs/LLM_ENDPOINTS.md)를 참고하세요.

## 팀 기본 설정 seed

portable 폴더에 `MailWhere.defaults.json`을 같이 두면, 사용자별 설정 파일이 아직 없을 때 첫 실행에서 그 값을 기본 설정으로 복사합니다. 릴리즈에는 `MailWhere.defaults.sample.json`만 포함되며, 실제 endpoint/model 값은 배포자가 sample을 복사해 수정하세요. API key나 개인 토큰은 이 파일에 넣지 않는 것을 권장합니다.

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
