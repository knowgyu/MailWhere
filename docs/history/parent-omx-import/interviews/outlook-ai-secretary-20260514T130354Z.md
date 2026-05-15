# Deep Interview Transcript — Outlook AI Secretary

- Profile: standard
- Context type: greenfield
- Final ambiguity: 0.131
- Threshold: 0.2
- Context snapshot: `.omx/context/outlook-ai-secretary-20260514T125112Z.md`
- Spec: `.omx/specs/deep-interview-outlook-ai-secretary.md`

## Summary

User wants a practical Windows 11 resident AI secretary for a closed Samsung office environment. The app should use Classic Outlook through COM rather than Microsoft Graph/Exchange/M365/Knox internals. Long-term product vision includes four important capabilities: follow-up monitoring, mail overload triage/summarization, quick task capture, and meeting/calendar preparation. The phased plan should include all four, while Phase 1's automatic core loop is follow-up monitoring.

## Round 1 — intent/outcome
**Question:** 이 Outlook AI 비서가 매일 켜둘 가치가 있으려면 첫 버전이 반드시 해결해야 하는 가장 중요한 업무 실패/불편은 무엇인가요? 하나만 고르면 됩니다.
**Answer:**

```json
{
  "kind": "other",
  "value": "4개 다 너무 중요한데?",
  "interpretation": "사용자는 답장/후속조치, 메일 과부하 요약, 빠른 할일 등록, 회의/일정 준비를 모두 핵심으로 본다."
}
```
**Notes:** 비전은 통합형 autonomous secretary로 명확해졌지만 MVP 우선순위/자동화 경계가 불명확하다. 다음 라운드는 scope/tradeoff 강제.
**Scores after:** {"intent": 0.65, "outcome": 0.55, "scope": 0.25, "constraints": 0.55, "success": 0.25}; ambiguity=0.51

## Round 2 — scope/tradeoff
**Question:** v0.1에서 자동으로 먼저 돌아가야 하는 핵심 루프는 무엇인가?
**Answer:**

```json
{
  "kind": "option",
  "value": "auto-followup",
  "selected_labels": [
    "Follow-up 자동 감시"
  ],
  "selected_values": [
    "auto-followup"
  ]
}
```
**Notes:** Contrarian pressure pass completed: all-four-important assumption was narrowed to one automatic loop. Follow-up detector taxonomy and acceptance criteria remain unresolved.
**Scores after:** {"intent": 0.75, "outcome": 0.7, "scope": 0.45, "constraints": 0.55, "success": 0.35}; ambiguity=0.3925

## Round 2-addendum — scope/phasing
**Answer:**

```json
"저것들 그냥 다 하되 phase를 잘 나눠놔봐"
```
**Interpretation:** 전체 제품 비전에는 follow-up, 메일 요약/선별, quick task capture, 회의/일정 준비 4개 모두 포함한다. 단, Phase 1 자동 핵심 루프는 auto-followup으로 두고 나머지는 수동/보조/후속 phase로 나눈다.
**Scores after:** {"intent": 0.8, "outcome": 0.75, "scope": 0.6, "constraints": 0.55, "success": 0.4}; ambiguity=0.315

## Round 3 — Phase 1 non-goals
**Question:** Phase 1에서 안 한다/나중으로 미룰 제외사항은 무엇인가?
**Answer:**

```json
{
  "kind": "multi",
  "selected_values": [
    "no-auto-mutating-mail",
    "no-knox-mysingle-parsing",
    "no-full-calendar-sync",
    "no-auto-attachment-analysis",
    "no-external-llm-default"
  ],
  "selected_labels": [
    "자동 발송/삭제/이동 금지",
    "Knox/mySingle 직접 파싱 금지",
    "캘린더 완전 동기화 제외",
    "첨부 자동 분석 제외",
    "외부 LLM 기본 사용 금지"
  ]
}
```
**Notes:** Phase 1 is read/analyze/suggest oriented. User did not select no-full-history-analysis, so history scope remains unresolved rather than excluded.
**Scores after:** {"intent": 0.8, "outcome": 0.78, "scope": 0.68, "constraints": 0.75, "success": 0.45}; ambiguity=0.2715

## Round 4 — decision boundaries
**Question:** Phase 1에서 앱이 사용자 승인 없이 자동으로 해도 되는 일은 어디까지인가?
**Answer:**

```json
{
  "kind": "multi",
  "selected_values": [
    "auto-read-mail",
    "auto-llm-analysis",
    "auto-create-local-task",
    "auto-notify",
    "auto-local-index"
  ],
  "selected_labels": [
    "메일 읽기/메타데이터 수집",
    "LLM 분석 실행",
    "로컬 task/reminder 생성",
    "알림 띄우기",
    "분석 상태 로컬 기록"
  ],
  "not_selected": [
    "auto-draft-reply"
  ]
}
```
**Notes:** Decision boundary set: automatic read/analyze/create local tasks/notify/index allowed. Reply draft generation not selected, so requires explicit user action or later phase.
**Scores after:** {"intent": 0.85, "outcome": 0.82, "scope": 0.75, "constraints": 0.85, "success": 0.55}; ambiguity=0.2075

## Round 5 — success criteria
**Question:** Phase 1을 회사 PC에서 써볼 만한 PoC 성공으로 인정하려면 어떤 검증 기준이 반드시 통과해야 하나?
**Answer:**

```json
{
  "kind": "multi",
  "selected_values": [
    "followup-quality",
    "false-positive-control",
    "security-storage",
    "daily-usable-ux"
  ],
  "selected_labels": [
    "Follow-up 탐지 품질",
    "오탐 제어",
    "보안/저장 원칙 준수",
    "매일 켜둘 UX"
  ],
  "not_selected": [
    "probe-clear-fallback",
    "process-recent-new-mail"
  ]
}
```
**Notes:** User success prioritizes practical assistant value: follow-up detection quality, low noisy false positives, security/storage discipline, and daily tray/task UX. Diagnostics remain an architectural requirement from earlier context but not the top PoC success value selected in this round.
**Scores after:** {"intent": 0.9, "outcome": 0.88, "scope": 0.82, "constraints": 0.9, "success": 0.8}; ambiguity=0.131


