# Baseline Metrics Contract

G001 records only content-free capability and timing data before the mail mirror/FTS store exists.

## Local baseline captured 2026-07-26 KST

- Host: Linux container, `dotnet` absent from PATH; bundled SDK at `.tools/dotnet/dotnet` is usable.
- Command: `.tools/dotnet/dotnet test tests/MailWhere.Tests/MailWhere.Tests.csproj --no-restore` -> pass.
- Command: `scripts/verify-static.sh` -> pass; reports system `dotnet` unavailable as documented local gap.
- Outlook/managed-PC probes: not runnable in this Linux container; must be captured on managed Windows in G006.

## Allowed mirror metrics

Diagnostics may export only sanitized keys already accepted by `SanitizedDiagnosticsExporter`:

- counts: `count`, `skippedCount`, `rowCount`, `hitCount`, `failureCount`, `fallbackCount`
- timings: `durationMs`, `elapsedMs`, `p50Ms`, `p95Ms`
- batch/page sizes: `batchSize`, `pageSize`
- safe modes/codes: `feature`, `mode`, `statusCode`, `errorClass`, `tokenizer`, `journalMode`, `connectionMode`, `operation`, `version`, `enabled`

Diagnostics must not export mail `body`, `subject`, raw HTML/RTF, `(StoreID, EntryID)`, sender/recipient addresses, source ids, source hashes, snippets, or file paths.
