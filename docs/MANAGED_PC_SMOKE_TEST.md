# Managed PC Readiness Test

Run this before enabling 새 메일 자동 확인 on a managed Windows PC.

1. Start Classic Outlook manually.
2. Start MailWhere.
3. Open Diagnostics.
4. Run probes with body probe disabled first.
5. Confirm COM/profile/Inbox/metadata results.
6. Run body probe only if acceptable.
7. Confirm diagnostics export has no mail content.
8. Confirm 새 메일 자동 확인 remains disabled until the readiness result is recorded in runtime settings.
9. Toggle startup registration on/off if allowed.

Do not enable 새 메일 자동 확인 if any probe causes policy warnings that are unacceptable.

## Mail mirror sync

1. Back up or remove the local `followups.sqlite*` files.
2. Click **지금 메일 확인** and confirm progress exposes only folder names and counts.
3. Stop after at least one committed batch, restart MailWhere, and confirm the next run resumes without duplicate search results.
4. Run `MailWhere.Cli.exe search-mail --json --query "<known body-only term>"` and confirm the result comes from SQLite without reopening Outlook.
5. Add or change a mail and confirm an automatic/event check indexes it.
6. Move or delete a mail, then confirm a manual or 24-hour reconcile removes its old searchable result.
7. Confirm Outlook mailbox state is unchanged and exported diagnostics contain no mail content.
