# MailWhere

<p align="center">
  <img src="assets/app-icon.svg" alt="MailWhere" width="128" />
</p>

한국어 업무 메일을 계속 지켜보며 **Action item, 회의/일정성 항목, 마감 리마인드**를 찾아주는 Windows 11 상주형 업무 보드 PoC입니다. 핵심 방향은 “메일을 대신 조작하는 agent”가 아니라, 메일에서 놓치기 쉬운 후속조치를 조용히 모아 보여주는 read-only 비서입니다.

## 지금 되는 것

- Classic Outlook COM 기반 read-only 메일 읽기
- 최근 1개월 스캔(기본은 갯수 제한 없이 날짜 기준)
- rule-based action item 탐지와 선택형 fallback
- 선택형 LLM endpoint 분석
  - Ollama native `/api/chat`
  - OpenAI-compatible local server `/v1/chat/completions`
  - OpenAI-compatible local server `/v1/responses`
- endpoint에서 모델 목록 불러오기 + LLM 연결 테스트
- 스캔별 LLM 시도/성공/fallback/실패 요약
- LLM 실패 후보는 중복 생성하지 않고, endpoint 복구 후 같은 메일을 다시 분석
- RE/FW 제목 정규화와 현재 작성부/전달 맥락/인용 히스토리 분리
- 같은 스레드에서 반복되는 동일 action item 중복 생성 억제
- 명시적으로 다른 사람에게 배정된 요청은 내 업무로 자동 등록하지 않음
- SQLite 로컬 task 저장
- 낮은 확신 후보를 검토함에 표시
- 기본 08:00 오늘의 업무 보드 창 표시(이후 실행 시 다음 정시)
- 상단 버튼 또는 tray 우클릭에서 오늘의 업무 보드 다시 열기
- 초기/대량 스캔 시 후보별 팝업 폭탄 대신 scan summary 1회 + 검토함/보드 중심 처리
- 검토 후보 버튼 처리와 충돌 적은 Alt+A 등록 / Alt+S 나중에 보기 / Alt+I 무시 단축키
- 스캔 중 진행 상태 표시와 스캔 버튼 잠금
- D-day 표시와 D-7/D-1/D-day reminder planning
- tray 상주 + 앱 자체 우하단 토스트 알림 스택
- GitHub Actions Windows portable zip 빌드

## 안전 기본값

- 메일 발송/삭제/이동/답장/첨부 자동 분석 없음
- vendor-specific mailbox export files 직접 parsing 없음
- 외부/endpoint LLM 기본 OFF
- raw mail body와 prompt 로그 저장 없음
- 운영 smoke gate 전 자동 watcher 비활성

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

출력:

```text
artifacts/MailWhere-v0.1.4-win-x64-portable.zip
```

## LLM endpoint

기본은 LLM OFF라 로컬 규칙 기반 분석만 사용합니다. LLM을 켜면 **LLM이 먼저 분석**하고, 실패하면 기본적으로 검토함에 남깁니다. 규칙 기반 fallback은 사용자가 명시적으로 선택하거나 실패 후 모달에서 동의한 경우에만 켭니다.

- `LlmOnly`: LLM 실패 시 자동 등록하지 않고 검토함에 남김(기본)
- `LlmThenRules`: LLM 실패 시 규칙 기반 analyzer로 fallback

```json
{
  "ExternalLlmEnabled": true,
  "LlmProvider": "OllamaNative",
  "LlmEndpoint": "http://localhost:11434",
  "LlmModel": "",
  "LlmFallbackPolicy": "LlmOnly"
}
```

vLLM 같은 OpenAI-compatible local endpoint는 `LlmProvider`를 `OpenAiChatCompletions` 또는 `OpenAiResponses`로 설정합니다. 기본 모델명은 비워두고, 앱의 **모델 불러오기** 버튼으로 `/api/tags` 또는 `/v1/models`에서 목록을 가져와 선택하는 흐름을 권장합니다. **연결 테스트**는 메일 내용이 아닌 작은 JSON probe만 보냅니다. 자세한 내용은 [`docs/LLM_ENDPOINTS.md`](docs/LLM_ENDPOINTS.md)를 참고하세요.

## 문서

- 배포: [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md)
- 관리형 PC smoke test: [`docs/MANAGED_PC_SMOKE_TEST.md`](docs/MANAGED_PC_SMOKE_TEST.md)
- UX/연동 리뷰: [`docs/UX_AND_INTEGRATION_REVIEW.md`](docs/UX_AND_INTEGRATION_REVIEW.md)
- 로드맵: [`docs/ROADMAP.md`](docs/ROADMAP.md)
- 보안: [`docs/SECURITY.md`](docs/SECURITY.md)
- 구조: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
