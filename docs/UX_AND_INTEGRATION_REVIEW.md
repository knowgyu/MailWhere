# UX / Integration Review

Date: 2026-05-15

## 핵심 제품 정의

MailWhere의 핵심은 “메일 앱을 한 번 더 열어보게 만드는 도구”가 아니라, Windows에 조용히 떠 있으면서 메일 기반 후속 조치와 마감 리스크를 먼저 알려주는 개인 업무 비서다. 그래서 0.1.0 UX 판단 기준은 기능 수보다 아래 4개다.

1. **켜두면 이득**: 사용자가 앱을 계속 보고 있지 않아도 tray/reminder가 가치 전달.
2. **메일 신뢰 유지**: 발송/삭제/이동/읽음 처리/첨부 자동 분석은 하지 않음.
3. **배우지 않아도 시작**: 진단 → 최근 1개월 스캔 → 할 일/알림 확인의 짧은 루프.
4. **LLM 실패 허용**: endpoint가 없거나 JSON이 깨져도 rule-based 분석과 검토함으로 degraded 동작.

## 최신 공식 자료에서 얻은 적용점

- Microsoft는 Windows 10/11의 local app notification을 WPF/WinForms 같은 앱도 보낼 수 있다고 설명한다. 다만 unpackaged desktop app은 activation/identity 쪽 추가 절차가 필요하다. 따라서 0.1.0은 `NotifyIcon` balloon을 portable fallback으로 두고, 클릭 액션/Action Center 품질이 필요한 시점에 MSIX 또는 app identity 트랙으로 이동한다. Source: Microsoft Learn, local app notification for C# apps, https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/send-local-toast
- Outlook `MailItem`은 메일 메시지를 표현하며 `Body`, `ReceivedTime`, `SenderName` 같은 읽기 속성이 있다. 반면 `_MailItem.UnRead`는 read/write다. 그래서 COM adapter는 메타데이터/본문 읽기에 한정하고 mutating property/method를 static check로 금지한다. Sources: https://learn.microsoft.com/office/vba/api/Outlook.MailItem and https://learn.microsoft.com/en-us/dotnet/api/microsoft.office.interop.outlook._mailitem.unread
- Ollama `/api/chat`는 chat history와 model을 받으며 `format`은 `json`을 지원하고 `stream` 기본값이 true다. 0.1.0 client가 `stream=false`, `format=json`으로 호출하는 것은 portable JSON 분석에 맞다. Source: https://docs.ollama.com/api/chat
- vLLM은 OpenAI-compatible server로 `/v1/chat/completions`와 `/v1/responses` 등을 제공한다. 그래서 provider를 “Ollama native”와 “OpenAI-compatible”로 나눈 설계가 적절하다. Source: https://docs.vllm.ai/en/stable/serving/openai_compatible_server/
- OpenAI의 최신 reasoning-model guidance는 structured outputs, tool calling, hosted/custom tools, state management, compaction, Agents SDK의 tracing/handoffs/state patterns를 강조한다. 이 프로젝트의 agentic 확장은 “메일을 마음대로 조작하는 agent”가 아니라 안전한 tool registry 위의 제안/확인/로컬 상태 갱신 agent여야 한다. Source: https://developers.openai.com/api/docs/guides/latest-model#using-reasoning-models
- OpenAI의 business data/privacy 자료는 기본 학습 미사용, encryption, retention controls, ZDR 승인 옵션, data residency/access control 같은 운영 조건을 제시한다. 외부 Enterprise LLM 연결은 단순 API key 입력이 아니라 retention/region/access/audit 조건을 문서화한 별도 provider profile로 다뤄야 한다. Sources: https://openai.com/business-data/ and https://developers.openai.com/api/docs/guides/your-data

## OfficeWhere에서 가져온 UX 원칙

`../OfficeWhere`의 design-console plan에서 확인한 원칙은 다음처럼 이 프로젝트에 맞게 변환했다.

- Quiet productivity console: 화려한 대시보드보다 조용한 상태/할 일/알림 중심.
- Behavior preservation: 메일 원본과 기존 Outlook workflow를 건드리지 않음.
- Screen-local first: WPF shared control 체계보다 현재 화면에서 바로 이해되는 그룹/문구 우선.
- Raycast-like command surface: 빠른 할 일 추가와 최근 스캔 버튼을 명확한 primary action으로 둠.

## 현재 반영한 UX 개선

- Korean-first main window: “오늘 봐야 할 항목”, “최근 1개월 스캔”, “검토함”, “진단 로그”.
- 빠른 할 일 추가: 제목 + 마감 표현(`내일`, `금요일`, `2026-05-20`)을 바로 입력.
- Tray-first notification: 앱을 열어보지 않아도 scan summary/reminder/error를 보여주되, 초기/대량 스캔 후보는 후보별 팝업으로 쏟아내지 않음.
- 검토함 표시: 자동 등록하기 애매한 후보를 앱/업무 보드에서 확인 가능하게 함.
- 오늘의 업무 보드: 기본 08:00 또는 앱 시작 후 다음 정시에 오늘/지남, 7일 내, 마감 없음, 확인 필요 후보를 보드 창으로 표시.
- 업무 보드 재접근: 상단 버튼 또는 tray 우클릭 메뉴에서 업무 보드를 다시 열 수 있음.
- 대량 스캔 진행 상태: Outlook 읽기/분석 진행 상태를 표시하고 스캔 중 주요 버튼을 잠가 “렉/멈춤”처럼 보이지 않게 함.
- LLM 가시성: 연결 테스트와 스캔별 LLM 시도/성공/fallback/실패/평균 응답 시간을 표시.
- LLM-first 정책: LLM을 켠 경우 LLM을 먼저 시도한다. 기본은 `LlmOnly`이고, rule fallback은 사용자가 설정 또는 실패 모달에서 명시적으로 허용한 경우에만 사용한다.
- 모델 선택 UX: endpoint 입력 후 Ollama `/api/tags` 또는 OpenAI-compatible `/v1/models`에서 모델 목록을 불러와 dropdown으로 선택할 수 있다. 목록이 없으면 직접 입력한다.
- Reminder timer: 앱이 켜져 있는 동안 30분마다 due reminder 후보를 재검토.
- Automatic watcher smoke gate 통과 후에는 15분마다 보수적으로 read-only scan을 수행해 새 항목을 로컬 상태에 반영.
- LLM 설정 UI: Disabled/OllamaNative/OpenAiChatCompletions/OpenAiResponses 선택, endpoint/model, 고급 token 환경 변수명 지원.
- App icon: 실행 파일/tray/window에 같은 심볼을 사용.
- Portable artifact 정리: `START_HERE_시작하기.txt`, README, docs, assets, sample settings 포함.

## 비개발자 사용성을 위해 아직 부족한 점

1. **진짜 Windows app notification**: portable fallback은 충분히 단순하지만 클릭 액션/Action Center persistence는 약하다. MSIX 또는 app identity가 확인되면 native app notification으로 옮긴다.
2. **검토함 액션**: 단일 선택 등록/무시는 생겼지만, 다중 선택/일괄 처리/마감 수정은 아직 없다.
3. **자동 watcher**: 보수적 polling은 smoke gate 이후에만 켜진다. Outlook event subscription은 실제 관리형 PC 안정성 확인 후 별도 검토한다.
4. **캘린더**: 직접 sync보다 local shadow calendar/ICS export가 안전하다. Outlook Calendar COM은 별도 probe와 read-only 정책이 필요하다.
5. **LLM 품질 관측**: 연결 테스트와 scan-level 통계는 생겼지만, 후보별 “LLM 판단인지 fallback 판단인지”를 UI에 더 명확히 드러내야 한다.

## OfficeWhere 연결성

- 단기: 메일 action item에서 추출한 키워드를 OfficeWhere search query로 넘기는 “검색 핸드오프”가 가장 안전하다.
- 중기: MailWhere가 만든 task/review candidate를 JSONL 또는 SQLite view로 export하고, OfficeWhere가 이를 read-only source로 색인한다.
- 장기: 둘을 직접 강결합하지 말고 local protocol/CLI bridge를 둔다. 예: `officewhere search --query "프로젝트명 마감"` 또는 custom URI.
- 하지 말 것: 메일 본문 전체를 OfficeWhere 문서 인덱스로 자동 투입. 본문 저장/재색인/삭제 책임이 커진다.

## Agentic AI 방향

좋은 agentic UX는 “자동으로 다 해줌”보다 “안전한 tool을 골라 근거와 다음 행동을 제안함”이다.

Recommended tool boundary:

- Read tools: recent mail summary, local task list, local reminder state, OfficeWhere search.
- Suggest tools: draft action item, draft meeting note, draft reminder schedule.
- Local mutate tools: create/update/dismiss local task only.
- Forbidden by default: send mail, reply, delete/move mail, open/analyze attachment automatically, upload raw body to unapproved endpoint.

0.2 이후에는 LLM prompt를 “JSON 추출기”에서 “tool-aware secretary planner”로 확장하되, mutating tool은 local task에만 제한한다.
