# Managed PC Readiness Test

Run this on the managed Windows PC before enabling 새 메일 자동 확인.
Use a throwaway/synthetic message only; do not paste real mail content into diagnostics or test notes.

## Baseline readiness

1. Start Classic Outlook manually.
2. Start MailWhere.
3. Open Diagnostics.
4. Run probes with the body probe disabled first.
5. Confirm COM/profile/Inbox/metadata results.
6. Run the body probe only if acceptable.
7. Confirm diagnostics export is content-free and shows no mail body, source locator, StoreId, or EntryId.
8. Confirm 새 메일 자동 확인 remains disabled and the readiness result is recorded in runtime settings.
9. Toggle startup registration on/off if allowed.

Do not enable 새 메일 자동 확인 if any probe causes unacceptable policy warnings.

## Controlled synthetic search setup

1. Create or send one test message whose body contains this literal body-only term and whose subject does not: `MailWhereSmokeBodyOnly20260729`.
2. Put the message in a test-safe child mail folder under the default mailbox. Do not use Online Archive.
3. Keep the mailbox open only for normal Outlook use; SQLite search itself must not activate Outlook.
4. Delete the synthetic message after the run if local policy requires it.

## Mail mirror sync and search window smoke

1. Back up/remove local `followups.sqlite*` files.
2. Click **지금 메일 확인** and confirm progress exposes only folder names/counts.
3. Open **메일 검색** and confirm the initial state is empty/help text, not stale results.
4. Search for `MailWhereSmokeBodyOnly20260729` using Enter, then again using the **검색** button; both return the synthetic message.
5. Confirm **전체** shows the child-folder message while **받은 메일** and **보낸 메일** do not. Then repeat with Inbox/Sent test messages to verify those two filters.
   If policy permits a separate synthetic Online Archive copy, confirm it is not indexed.
6. Search a nonsense term and confirm the empty-results state is safe and content-free.
7. Press Esc and confirm the search window closes.
8. Reopen search, select the synthetic result, double-click it, then repeat with **원본 열기**; both must explicitly open the source message.
9. Confirm the SQLite search path did not activate Outlook; only the explicit source-open action may bring Outlook forward.
10. Remove/move the synthetic source message, search again, and confirm explicit source-open fails safely with an error and no source locator display.
11. Stop MailWhere after at least one committed sync batch, restart it, run sync again, and confirm resume has no missing or duplicate search results.
12. Add/change/move/delete mail and confirm the manual 24-hour reconcile removes stale searchable results.
13. Confirm Outlook mailbox state is unchanged except for the synthetic message actions above.
14. Confirm exported diagnostics remain content-free and contain no source locator, StoreId, EntryId, subject body, or body snippet.

Automated coverage includes the mirror checkpoint case where `DefaultPageSize + 1` messages share equal timestamps. The real managed Outlook/EDR run remains external and must be recorded as managed-PC evidence.

## v0.13.1 board and grouped review smoke

1. Open the app directly and from the tray; confirm both manual paths default to **이번 주**.
2. Confirm a task without a due date created within seven days appears in **이번 주** and shows its source mail time.
3. Confirm an undated task older than seven days appears in **미정 backlog**, without an invented due date.
4. Create two synthetic review candidates from the same sender whose titles differ only by numbers; confirm they appear as one counted card.
5. Exercise `모두 나중에`, then repeat with `모두 무시`; confirm every grouped candidate changes state and Outlook remains unchanged.
6. Double-click every review-row button and confirm none opens Outlook; double-click the row text and confirm only that explicit row action opens the source.
7. With Korean IME active, confirm `Y`, `S`, `I`, and `Esc` shortcuts still work.

## v0.13.0 skill and LLM smoke

Run these only if local policy allows the target LLM endpoint and local agent skill installation.

1. Confirm the portable folder contains `skills\mailwhere\SKILL.md`, references, and manifest-style files.
2. Run the install/repair flow for Codex and Claude roots:
   - `%USERPROFILE%\.agents\skills\mailwhere`
   - `%USERPROFILE%\.claude\skills\mailwhere`
3. If a target folder already exists, choose **No** once and confirm the folder is preserved and opened.
4. Repeat repair and choose **Yes** only for the bundled test copy; confirm the bundled content overwrites without creating a backup.
5. Configure `Qwen/Qwen3.8-27B` through the model list returned by the vLLM endpoint.
6. Confirm the endpoint is served with vLLM `>=0.17.0` and `--reasoning-parser qwen3`.
7. Run the MailWhere LLM probe and confirm it exercises synthetic single-item, batch, and waiting-closure shapes, not real mail content.
8. Change one LLM setting such as structured-output mode or temperature and confirm the saved probe proof becomes stale until the probe passes again.
9. Confirm the review backlog label separates total unresolved items, visible page count capped at 100, and retryable LLM failures.
10. Search/list through the skill or CLI and confirm normal output has no body, StoreID, EntryID, source id, or source hash. The only source-open handle allowed there is `open_source_token`.
11. Explicitly open one synthetic result through `MailWhere.exe --open-source-token <token>` and confirm it opens the intended Outlook item.
12. Try an invalid token and confirm the failure is sanitized and does not display a raw locator.
