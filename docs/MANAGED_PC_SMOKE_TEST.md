# Managed Smoke Test

Run this before enabling automatic watching on a managed Windows PC.

1. Start Classic Outlook manually.
2. Start MailWhere.
3. Open Diagnostics.
4. Run probes with body probe disabled first.
5. Confirm COM/profile/Inbox/metadata results.
6. Run body probe only if acceptable.
7. Confirm diagnostics export has no mail content.
8. Confirm automatic watcher remains disabled until the smoke gate is recorded in runtime settings.
9. Toggle startup registration on/off if allowed.

Do not enable automatic watching if any probe causes policy warnings that are unacceptable.
