# MailWhere Docs

현재 MailWhere 문서는 **tray-first read-only 업무 보드** 모델을 기준으로 정리합니다. 과거 OMX/import 기록은 보존하되, 현재 제품 판단은 이 파일과 아래 현재 문서를 우선합니다.

## 먼저 볼 문서

- [`../README.md`](../README.md): 제품 요약, 실행, 개발 검증, LLM 설정.
- [`START_HERE.ko.txt`](START_HERE.ko.txt): portable zip에 포함되는 사용자 시작 안내.
- [`PROJECT_CONTEXT.md`](PROJECT_CONTEXT.md): 이전 기획에서 현재 구현까지 이어지는 맥락과 보존 제약.
- [`ARCHITECTURE.md`](ARCHITECTURE.md): 레이어와 안전 경계.
- [`VISUAL_QA_WORKFLOW_2026-05-16.md`](VISUAL_QA_WORKFLOW_2026-05-16.md): tray-first/업무 보드/보관 모델로 정리한 최신 UI 결정.

## 운영/배포

- [`DEPLOYMENT.md`](DEPLOYMENT.md): portable-first 배포와 GitHub Actions artifact.
- [`MANAGED_PC_SMOKE_TEST.md`](MANAGED_PC_SMOKE_TEST.md): 관리형 Windows PC에서 새 메일 자동 확인을 켜기 전 확인 절차.
- [`CAPABILITY_PROBES.md`](CAPABILITY_PROBES.md): Outlook/저장소/알림/LLM capability probe 계약.
- [`FAILURE_MODES.md`](FAILURE_MODES.md): 실패 모드와 degrade 동작.
- [`SECURITY.md`](SECURITY.md): read-only mailbox, raw body 최소 저장, LLM 안전 경계.

## 제품/기획

- [`ROADMAP.md`](ROADMAP.md): 이미 반영된 릴리즈와 다음 단계.
- [`UX_AND_INTEGRATION_REVIEW.md`](UX_AND_INTEGRATION_REVIEW.md): UX 원칙과 OfficeWhere/Agentic AI 연결 방향.
- [`PRODUCT_ARCHITECTURE_AND_AGENT_CLI.md`](PRODUCT_ARCHITECTURE_AND_AGENT_CLI.md): 제품 아키텍처 확장과 read-only Agent CLI 통합 연구.
- [`ASSUMPTIONS.md`](ASSUMPTIONS.md): 아직 검증 중인 가정.
- [`ADR/`](ADR/): Outlook COM, WPF tray app, read-only-first, portable-first 결정 기록.

## 기록/증거

- [`history/parent-omx-import/`](history/parent-omx-import/): 상위 workspace의 과거 인터뷰/계획/로그 import. 현재 정책과 다를 수 있으므로 history로만 봅니다.
- [`../visual-things/`](../visual-things/): 2026-05-16 visual QA screenshot, diagnosis, verdict 증거.

## 현재 용어 기준

- 사용자-facing CTA는 `지금 메일 확인`을 씁니다. 내부 구현 설명에서만 scan이라는 표현을 허용합니다.
- `새 메일 자동 확인`은 수동 확인 성공과 운영 정책 확인 뒤 켜는 기능입니다. 내부 문서의 safety gate는 사용자 화면에 그대로 노출하지 않습니다.
- 카드 1차 액션은 `열기`, `나중에`, `수정`, `보관`입니다.
- `나중에`는 다시 표시되는 snooze이고, `보관`은 active 목록에서 제외되어 자동으로 다시 뜨지 않습니다.
- Outlook 원본 메일은 read-only입니다. MailWhere의 상태 변경은 로컬 DB에만 적용됩니다.
