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
2. Put the message in a test-safe mailbox location covered by the mirror; note whether it is Inbox or Sent.
3. Keep the mailbox open only for normal Outlook use; SQLite search itself must not activate Outlook.
4. Delete the synthetic message after the run if local policy requires it.

## Mail mirror sync and search window smoke

1. Back up/remove local `followups.sqlite*` files.
2. Click **지금 메일 확인** and confirm progress exposes only folder names/counts.
3. Open **메일 검색** and confirm the initial state is empty/help text, not stale results.
4. Search for `MailWhereSmokeBodyOnly20260729` using Enter, then again using the **검색** button; both return the synthetic message.
5. Repeat with folder filters: **전체** shows the message, **받은 메일** shows it only for Inbox mail, and **보낸 메일** shows it only for Sent mail.
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
