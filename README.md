# MailWhere

<p align="center">
  <img src="assets/app-icon.svg" alt="MailWhere" width="128" />
</p>

한국어 업무 메일을 계속 지켜보며 **할 일, 회의/일정성 항목, 마감 리마인드**를 찾아주는 Windows 11 상주형 업무 보드 PoC입니다. 핵심 방향은 “메일을 대신 조작하는 agent”가 아니라, 메일에서 놓치기 쉬운 후속조치를 조용히 모아 보여주는 read-only 비서입니다.

## 지금 되는 것

- Classic Outlook COM 기반 read-only Inbox/Sent Items 메일 읽기
- 지금 메일 확인(기본은 최근 30일, 갯수 제한 없이 날짜 기준)
- 규칙 기반 할 일 후보 탐지와 선택형 fallback
- 선택형 LLM endpoint 분석
  - Ollama native `/api/chat`
  - OpenAI-compatible local server `/v1/chat/completions`
  - OpenAI-compatible local server `/v1/responses`
- endpoint에서 모델 목록 불러오기 + LLM 연결 테스트
- 메일 확인별 LLM 시도/성공/fallback/실패 요약
- Ollama 분석 시 thinking 비활성화, 보수적 샘플링, 짧은 JSON 출력 제한, batch 분석
- LLM batch 분석은 8건 단위 기본값과 부분 실패 보정으로 마지막 묶음 실패를 줄임
- LLM 분석 품질 개선: 답장/전달 메일, To/CC 수신 여부, 담당자 표현을 더 보수적으로 판단
- LLM 실패 후보는 중복 생성하지 않고, endpoint 복구 후 같은 메일을 다시 분석
- RE/FW 제목 정규화와 현재 작성부/전달 맥락/인용 히스토리 분리
- 같은 스레드에서 반복되는 동일 업무 후보 중복 생성 억제
- 명시적으로 다른 사람에게 배정된 요청은 내 업무로 자동 등록하지 않음
- SQLite 로컬 task 저장
- LLM 실패/낮은 확신 후보는 기본 화면에 섞지 않고, 명시적으로 검토 후보에서만 처리
- 기본 08:00 오늘 업무 보드 자동 열기, Windows 시작 직후에는 기본 10분 지연 후 다른 프로세스가 준비된 뒤 표시
- 오늘 브리핑은 업무 보드의 오늘 보기로 이어지는 가벼운 요약이며, 업무 보드는 활성 항목 전체 원장입니다
- 업무 보드는 전체/오늘/7일 내/30일 내/기한 미정 필터와 `내가 할 일`/`기다리는 중` 2열 카드로 표시
- 업무 카드에서 열기/나중에/수정/보관을 바로 처리하고, 기한은 카드에서 빠르게 바꿀 수 있음
- 상단 버튼 또는 tray 우클릭의 **오늘 업무 보기**로 오늘 업무 보드 다시 열기
- 초기/대량 메일 확인 시 후보별 팝업 폭탄 대신 요약 알림 1회 + 검토 후보/보드 중심 처리
- 검토 후보 버튼 처리와 충돌 적은 Alt+A 등록 / Alt+S 나중에 보기 / Alt+I 무시 단축키
- 메일 확인 중 진행 상태 표시, 중지 버튼, timeout 발생 시 전체 확인 중단 방지
- 업무 보드/홈 카드의 [열기] 버튼으로 가능한 경우 Outlook 원본 메일 열기
- D-day 표시와 D-7/D-1/D-day reminder planning, D-day/snooze-due는 하루 1회 interrupt toast
- tray 상주 + 앱 자체 우하단 토스트 알림 스택
- 테스트/개발용 업무 데이터 초기화 버튼과 `scripts/reset-local-data.ps1`
- GitHub Actions Windows portable zip 빌드

## 안전 기본값

- 메일 발송/삭제/이동/답장/첨부 자동 분석 없음
- vendor-specific mailbox export files 직접 parsing 없음
- 외부/endpoint LLM 기본 OFF
- raw mail body와 prompt 로그 저장 없음
- 수동 확인 성공 전 새 메일 자동 확인 비활성

## 다운로드해서 실행

GitHub Actions artifact 또는 Release zip을 받아 압축을 풀고 아래 파일을 실행합니다.

```text
MailWhere.exe
```

zip 안의 `START_HERE_시작하기.txt`를 먼저 읽는 것을 권장합니다.

## Windows 개발/검증

```powershell
cd MailWhere
.\scripts\verify-windows.ps1
.\scripts\publish-portable.ps1
```

테스트 중 fallback/rule 확인 결과를 지우고 다시 시작하려면 앱의 **문제 해결 → 업무 데이터 초기화**를 누르거나 아래 스크립트를 실행합니다. 기본은 `%LOCALAPPDATA%\\MailWhere\\followups.sqlite*`만 삭제하고 설정은 유지합니다.

```powershell
.\scripts\reset-local-data.ps1
```

출력:

```text
artifacts/MailWhere-v0.3.1-win-x64-portable.zip
```

## LLM endpoint

기본은 LLM OFF라 로컬 규칙 기반 분석만 사용합니다. LLM을 켜면 **LLM이 먼저 분석**하고, 실패하면 기본적으로 검토 후보에 남깁니다. 규칙 기반 fallback은 사용자가 명시적으로 선택하거나 실패 후 모달에서 동의한 경우에만 켭니다.

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

vLLM 같은 OpenAI-compatible local endpoint는 `LlmProvider`를 `OpenAiChatCompletions` 또는 `OpenAiResponses`로 설정합니다. 기본 모델명은 비워두고, 앱의 **모델 불러오기** 버튼으로 `/api/tags` 또는 `/v1/models`에서 목록을 가져와 선택하는 흐름을 권장합니다. **연결 테스트**는 메일 내용이 아닌 작은 JSON probe만 보냅니다. Ollama native 호출은 Qwen 계열 같은 thinking-capable 모델을 업무 triage에 맞게 `think=false`와 짧은 출력 제한으로 호출합니다. 자세한 내용은 [`docs/LLM_ENDPOINTS.md`](docs/LLM_ENDPOINTS.md)를 참고하세요.

### 부서/팀 기본 설정 seed

portable 폴더에 `MailWhere.defaults.json`을 같이 두면, 사용자별 설정 파일이 아직 없을 때 첫 실행에서 그 값을 기본 설정으로 복사합니다. 릴리즈에는 `MailWhere.defaults.sample.json`만 포함되며, 실제 endpoint/model 값은 배포자가 sample을 복사해 수정하세요. API key나 개인 토큰은 이 파일에 넣지 않는 것을 권장합니다.

## 이어서 작업할 때

상위 workspace에서 진행된 초기 기획/인터뷰/검증 산출물은 이 repo로 가져와 [`docs/PROJECT_CONTEXT.md`](docs/PROJECT_CONTEXT.md)에 정리했습니다. 원본 import manifest와 전체 복사본은 [`docs/history/parent-omx-import/`](docs/history/parent-omx-import/)에 있습니다.

## 문서

- 배포: [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md)
- 관리형 PC smoke test: [`docs/MANAGED_PC_SMOKE_TEST.md`](docs/MANAGED_PC_SMOKE_TEST.md)
- UX/연동 리뷰: [`docs/UX_AND_INTEGRATION_REVIEW.md`](docs/UX_AND_INTEGRATION_REVIEW.md)
- Visual QA workflow update: [`docs/VISUAL_QA_WORKFLOW_2026-05-16.md`](docs/VISUAL_QA_WORKFLOW_2026-05-16.md)
- 로드맵: [`docs/ROADMAP.md`](docs/ROADMAP.md)
- 보안: [`docs/SECURITY.md`](docs/SECURITY.md)
- 구조: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
- 제품화/Agent CLI 설계: [`docs/PRODUCT_ARCHITECTURE_AND_AGENT_CLI.md`](docs/PRODUCT_ARCHITECTURE_AND_AGENT_CLI.md)
