# Outlook AI Secretary

<p align="center">
  <img src="assets/app-icon.svg" alt="Outlook AI Secretary" width="128" />
</p>

한국어 업무 메일을 계속 지켜보며 **Action item, 회의/일정성 항목, 마감 리마인드**를 찾아주는 Windows 11 상주형 Outlook 비서 PoC입니다.

## 지금 되는 것

- Classic Outlook COM 기반 read-only 메일 읽기
- 최근 1개월/최대 N건 스캔 설정
- rule-based action item 탐지
- 선택형 LLM endpoint 분석
  - Ollama `/api/chat`
  - OpenAI-compatible/vLLM `/v1/chat/completions`
- SQLite 로컬 task 저장
- 낮은 확신 후보를 검토함에 표시
- 기본 08:00 오늘의 업무 보드 창 표시(이후 실행 시 다음 정시)
- 확인 필요 후보 우측 하단 팝업 + 버튼 처리 + 초점 상태 Ctrl+Y 등록 / Ctrl+N 무시(후보 단위 처리)
- D-day 표시와 D-7/D-1/D-day reminder planning
- tray 상주 + tray notification fallback
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
OutlookAiSecretary.Windows.exe
```

zip 안의 `START_HERE_시작하기.txt`를 먼저 읽는 것을 권장합니다.

## Windows 개발/검증

```powershell
cd OutlookAiSecretary
.\scripts\verify-windows.ps1
.\scripts\publish-portable.ps1
```

출력:

```text
artifacts/OutlookAiSecretary-win-x64-portable.zip
```

## LLM endpoint

기본은 rule-only입니다. LLM을 켜려면 앱 설정 또는 `runtime-settings.json`에서 provider를 명시합니다.

```json
{
  "ExternalLlmEnabled": true,
  "LlmProvider": "Ollama",
  "LlmEndpoint": "http://localhost:11434",
  "LlmModel": "qwen3.6"
}
```

vLLM/OpenAI-compatible endpoint는 `LlmProvider`를 `OpenAiCompatible`로 설정합니다. 자세한 내용은 [`docs/LLM_ENDPOINTS.md`](docs/LLM_ENDPOINTS.md)를 참고하세요.

## 문서

- 배포: [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md)
- 관리형 PC smoke test: [`docs/MANAGED_PC_SMOKE_TEST.md`](docs/MANAGED_PC_SMOKE_TEST.md)
- UX/연동 리뷰: [`docs/UX_AND_INTEGRATION_REVIEW.md`](docs/UX_AND_INTEGRATION_REVIEW.md)
- 로드맵: [`docs/ROADMAP.md`](docs/ROADMAP.md)
- 보안: [`docs/SECURITY.md`](docs/SECURITY.md)
- 구조: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
