# Capability Probes

Probe outputs are sanitized. They must include statuses and error classes, not mail content.

Required Phase 0 probes:

- `outlook-progid`
- `outlook-com`
- `outlook-profile`
- `outlook-inbox`
- `outlook-mail-metadata`
- `outlook-mail-body` when explicitly requested
- `outlook-polling`
- `outlook-new-mail-event` (deferred until event subscription is implemented)
- `outlook-calendar` (deferred until calendar MVP)
- `storage-writable`
- `llm-endpoint`
- `llm-analysis-shape`
- `rule-only-mode`
- `notification-capability`
- `startup-toggle`

Diagnostics use an allowlist (`count`, `skippedCount`, `version`, `feature`, `enabled`, `mode`, `errorClass`, `statusCode`, plus content-free mirror timing/count/mode keys listed in `BASELINE_METRICS.md`) with per-key value validation and safe gate reason codes. Probe messages are intentionally not exported.

Managed mode gates **새 메일 자동 확인** until a real Windows manual-readiness check passes. If automatic mail checking is not explicitly requested, the runtime gate reports `manual` mode even when probes pass.

`notification-capability` currently means the app can show MailWhere-owned in-app toast notifications and has tray fallback available; it does not imply native Action Center activation.

## LLM analysis-shaped probe

`llm-analysis-shape` is the release gate for external LLM follow-up analysis and LLM-backed waiting-closure judgment. It must use synthetic content, never real mail, and it must exercise the same request shapes that production follow-up analysis and closure judgment use.

Required accept checks:

- single-item analysis parses through the normal analyzer parser;
- batch analysis returns matching ids/counts for the synthetic `items[]`/`contents[]` payload;
- waiting-closure judgment parses through the normal closure parser;
- selected structured-output mode is honored;
- selected thinking-control mode is honored;
- Chat Completions and Responses serialize the selected structured-output and thinking-control fields in their provider-native shapes;
- response is valid JSON, not truncated, and contains no visible reasoning leakage such as `<think>`.

Required reject checks:

- invalid JSON;
- schema mismatch;
- missing or extra batch ids;
- closure response mismatch;
- reasoning metadata or incomplete Responses states;
- `finish_reason=length` or equivalent truncation;
- unsupported structured-output mode;
- thinking leakage in final content;
- transport timeout or HTTP failure.

A successful probe stores a deterministic proof fingerprint over provider, normalized endpoint, model, thinking-control mode, structured-output mode, temperature, max-output-token policy, and batch-size policy. A scan or retry that would call the LLM must refuse to start if this proof is missing or stale.

The single, batch, and waiting-closure probe calls all produce one successful proof. Without a current proof, follow-up analysis and LLM-backed closure judgment stay fail-closed; closure judgment falls back to deterministic rules.

Probe diagnostics may include provider kind, model id, thinking-control mode, structured-output mode, duration, HTTP/status class, finish reason, and returned item count. They must not include prompt payloads, subjects, bodies, addresses, attachment names, API keys, raw responses, StoreID, EntryID, source id, or source hash.
