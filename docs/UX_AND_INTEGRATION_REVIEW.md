# UX / Integration Review

Date: 2026-05-15
Updated: 2026-05-17, after v0.4.1 compact settings patch

## 핵심 제품 정의

MailWhere의 핵심은 “메일 앱을 한 번 더 열어보게 만드는 도구”가 아니라, Windows에 조용히 떠 있으면서 메일 기반 후속 조치와 마감 리스크를 먼저 알려주는 개인 업무 비서다. 그래서 0.1.0 UX 판단 기준은 기능 수보다 아래 4개다.

1. **켜두면 이득**: 사용자가 앱을 계속 보고 있지 않아도 tray, 지정 시간 업무 보드, reminder가 가치 전달.
2. **메일 신뢰 유지**: 발송/삭제/이동/읽음 처리/첨부 자동 분석은 하지 않음.
3. **배우지 않아도 시작**: 진단 → 지금 메일 확인 → 업무 보드 확인의 짧은 루프.
4. **LLM 실패 허용**: endpoint가 없거나 JSON이 깨져도 확인 필요에 보관하고, endpoint 복구 후 다시 분석할 수 있어야 함.

## 최신 공식 자료에서 얻은 적용점

- Microsoft는 Windows 10/11의 local app notification을 WPF/WinForms 같은 앱도 보낼 수 있다고 설명한다. 다만 unpackaged desktop app은 activation/identity 쪽 추가 절차가 필요하다. 현재는 사용자가 놓치지 않도록 MailWhere 자체 우하단 토스트 스택을 1차 알림으로 쓰고, OS notification/app identity 트랙은 MSIX 필요성이 확인될 때 별도로 검토한다. Source: Microsoft Learn, local app notification for C# apps, https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/send-local-toast
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
- Raycast-like command surface: 빠른 할 일 추가와 지금 메일 확인 버튼을 명확한 primary action으로 둠.

## 현재 반영한 UX 개선

- Korean-first main window: “오늘 봐야 할 항목”, “지금 메일 확인”, “확인 필요”. 진단은 설정의 문제 해결 영역에 둔다.
- Tray-first lifecycle: 직접 실행은 업무 보드를 바로 열고, Windows 자동 시작은 `--tray`로 조용히 시작한다.
- Scheduled 업무 보드: 지정 시간에는 통합 업무 보드를 먼저 열고, 보드 열기에 실패한 경우에만 알림으로 fallback한다.
- App-owned toast stack: 앱을 열어보지 않아도 우하단 카드형 toast로 scan summary/reminder/error를 보여주되, 초기/대량 스캔 항목은 항목별 팝업으로 쏟아내지 않음.
- 확인 필요 표시: LLM 실패/낮은 확신 항목은 앱 확인 필요에서 처리하되, 업무 보드는 확인 필요 스트레스를 줄이고 할 일/일정 중심으로 표시한다.
- 통합 업무 보드: 기본은 `오늘`이며 `오늘`/`이번 주`/`전체`/`날짜 없음` 필터와 단일 목록으로 표시한다.
- 업무 보드 재접근: 상단 버튼 또는 tray 우클릭 메뉴에서 업무 보드를 다시 열 수 있음.
- 업무 카드 action model: `열기`, `나중에`, `보관`을 기본 액션으로 둔다. 편집은 행 더블클릭으로 처리한다.
- 대량 메일 확인 진행 상태: Outlook 읽기/분석 진행 상태를 표시하고 확인 중 주요 버튼을 잠가 “렉/멈춤”처럼 보이지 않게 함.
- 긴 LLM 확인 제어: 확인 중지 버튼을 제공하고, timeout은 확인 필요로 남긴 뒤 다음 항목을 계속 처리한다.
- LLM 가시성: 연결 테스트와 확인별 LLM 시도/성공/fallback/실패/평균 응답 시간을 표시.
- LLM 속도: Ollama는 `think=false`, 짧은 JSON schema prompt, 출력 길이 제한, 8건 batch 호출과 부분 batch 실패 보정으로 대량 확인 체감 속도를 개선한다.
- LLM 판단 품질: 답장/전달 메일, 담당자 표현, FYI/공지, 불명확한 마감을 더 보수적으로 판단하도록 개선한다.
- LLM-first 정책: LLM을 켠 경우 LLM을 먼저 시도한다. 기본은 `LlmOnly`이고, 규칙 기반 fallback은 사용자가 설정 > AI 분석 또는 실패 모달에서 명시적으로 허용한 경우에만 사용한다.
- 모델 선택 UX: 기본 모델명은 비워두고, endpoint 입력 후 Ollama `/api/tags` 또는 OpenAI-compatible `/v1/models`에서 모델 목록을 불러와 dropdown으로 선택할 수 있다. 목록이 없으면 직접 입력한다.
- LLM 실패 재분석: 실패 항목은 같은 source에 중복 생성하지 않고, LLM 복구 후 재분석이 성공하면 기존 실패 항목을 정리한다.
- 업무 보드: 메일 제목/신뢰도/긴 근거보다 사용자가 해야 할 일, 사람 말투 날짜, 보낸 사람을 우선 표시하고, 확인 필요 항목은 별도 창 CTA로 접어둔다. 항목 더블클릭으로 AI 추출값을 바로잡는다.
- Reminder timer: 앱이 켜져 있는 동안 30분마다 due reminder 항목을 재검토.
- 새 메일 자동 확인은 설정에서 켜되 내부 readiness check를 통과한 경우에만 보수적으로 read-only 확인을 수행한다.
- 설정 UI: `기본`, `알림`, `AI 분석`, `개발자 도구` 탭으로 나누고, max mail count/startup delay 같은 운영자성 숫자는 일반 사용자 화면에서 제거한다.
- 진단/개발자 UX: 알림/샘플/필터 테스트는 매일 보는 header에서 빼고 설정의 개발자 도구 탭에 둔다.
- App icon: 실행 파일/tray/window에 같은 심볼을 사용.
- Portable artifact 정리: `START_HERE_시작하기.txt`, README, docs, assets, sample settings 포함.

## v0.4.2에서 반영한 v0.4.1 Windows smoke TODO

아래 항목은 v0.4.1 릴리즈 이후 실제 화면 확인에서 확인되어 v0.4.2 패치 범위로 반영했다.

1. **메인 헤더 버튼 정렬**: grid 기반 header와 shared rounded button template으로 primary `지금 메일 확인`을 오른쪽 끝에 고정하고 raw focus rectangle을 제거했다.
2. **필터 순서 변경**: 업무 보드 필터는 `오늘` / `이번 주` / `전체` / `날짜 없음` 순서다.
3. **개발자 도구 가시성 복구**: 설정 안의 `개발자 도구` 탭에 화면 테스트와 샘플/알림 도구를 명확히 묶었다.
4. **확인 필요 단축키 단순화**: `Alt+A/S/I` 대신 창 단위 `등록(Y)`, `나중에(S)`, `무시(I)`, `Esc` 닫기를 쓴다.
5. **확인 필요 버튼 순서**: 행 액션은 `원본 열기`를 가장 앞에 둔다.
6. **확인 필요 창 톤 통일**: main board와 같은 rounded card/list/button tone을 공유한다.
7. **용어 정리**: 사용자-facing 명칭은 `확인 필요`, 재시도 버튼은 `AI 분석 다시 시도`로 정리했다.
8. **프롬프트 payload 정리**: static system prompt는 유지하고, user payload는 `analysisDate`/`timezone` metadata 뒤 final `content`/`contents`에 본문을 두는 구조로 바꿨다.

내가 추가로 보는 개선 후보:

- **Windows visual smoke를 릴리즈 체크리스트화**: main board, 확인 필요, settings, edit dialog, toast를 같은 배율에서 캡처해 release 전에 확인한다.
- **메인 보드 키보드 사용성**: Enter로 열기, S로 나중에, Archive 키 또는 Delete/Backspace confirmation 등은 나중에 추가 가치가 크다.
- **확인 필요 bulk 처리**: 항목이 여러 개 쌓였을 때 한 행씩 누르는 흐름은 금방 피곤해진다. 다중 선택/일괄 나중에/일괄 무시를 후속으로 둔다.
- **“왜 떴는지” detail panel**: 기본 목록은 짧게 유지하되, 선택 시 오른쪽/하단에 근거·출처·AI 실패 여부를 보여주면 잘못 등록하는 부담이 줄어든다.
- **샘플 데이터 품질**: 같은 제목/발신자/시간이 반복되면 실제 중복처럼 보여 제품 신뢰도가 떨어진다. 개발자 샘플은 의도적으로 다양하게 만들거나 “샘플 반복 생성” 표시를 둔다.
- **아이콘 리프레시**: 실행 파일/tray/window 아이콘을 더 차분한 MailWhere 심볼로 교체할지 사용자가 concept 중 선택한다.

## 비개발자 사용성을 위해 아직 부족한 점

1. **알림 히스토리/quiet hours**: 자체 toast는 즉시성은 좋지만, 사용자가 자리를 비운 동안 놓친 알림을 다시 보는 notification center가 아직 없다.
2. **확인 필요 액션**: 단일 선택 등록/무시는 생겼지만, 다중 선택/일괄 처리/마감 수정은 아직 없다.
3. **새 메일 자동 확인**: 보수적 polling은 readiness check 이후에만 켜진다. Outlook event subscription은 실제 관리형 PC 안정성 확인 후 별도 검토한다.
4. **캘린더**: 직접 sync보다 local shadow calendar/ICS export가 안전하다. Outlook Calendar COM은 별도 probe와 read-only 정책이 필요하다.
5. **LLM 품질 관측**: 연결 테스트와 scan-level 통계는 생겼지만, 항목별 “LLM 판단인지 fallback 판단인지”를 UI에 더 명확히 드러내야 한다.
6. **원본 메일 열기 한계**: 새로 스캔된 항목은 source id로 Outlook 원본을 열 수 있지만, 기존 DB 항목이나 이동/삭제된 메일은 열리지 않을 수 있다.

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
