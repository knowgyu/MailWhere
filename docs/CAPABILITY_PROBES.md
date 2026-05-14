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
- `rule-only-mode`
- `notification-capability`
- `startup-toggle`

Diagnostics use an allowlist (`count`, `skippedCount`, `version`, `feature`, `enabled`, `mode`, `errorClass`, `statusCode`) with per-key value validation and safe gate reason codes. Probe messages are intentionally not exported.

Managed mode gates automatic watching until the real Windows smoke gate passes. If automatic watching is not explicitly requested, the runtime gate reports `manual` mode even when probes pass.
