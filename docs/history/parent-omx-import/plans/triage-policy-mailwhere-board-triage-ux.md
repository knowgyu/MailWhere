# Development-only triage policy — MailWhere

This is an internal development artifact for agents/developers. It is not user-facing release documentation.

## Product rule

MailWhere extracts local follow-up items. It must not mutate Outlook mail.

## Decision order

1. Treat `currentMessage` as the only new request by default.
2. Use `forwardedContext` only when the current sender explicitly asks the user to check/respond/handle the forwarded content.
3. Never auto-create a task from `quotedHistory` alone.
4. If the current request explicitly names another assignee, ignore it unless the assignee matches the mailbox owner.
5. If the request is useful but not clearly assigned or not clearly actionable, send it to review.
6. Only auto-create when action, owner, and due/intent are sufficiently clear.
7. Do not invent due dates. Use `null` when not stated.

## UI rule

The default board is for action, not model introspection.

- Show: action title, due bucket/time, minimal sender/source affordance when available.
- Hide by default: confidence, LLM/provider labels, prompt/source-origin terms, long reasons, long evidence.
- Put uncertain items behind a compact review-needed CTA.

## Example decisions

- “영희님, 내일까지 비용 자료 검토 후 회신 부탁드립니다.” and mailbox owner is 김영희 → `autoCreateTask`.
- “철수님, 내일까지 비용 자료 검토 부탁드립니다.” and mailbox owner is 김영희 → `ignore`.
- “아래 고객 요청 건 확인 후 대응 부탁드립니다.” with forwarded request → `review` or `autoCreateTask` depending on clarity.
- Reply that only says “확인했습니다” with old quoted deadline → `ignore`.
- “회의 가능 시간 확인 부탁드립니다” with a concrete date/time → `calendarEvent` or `meeting`.

