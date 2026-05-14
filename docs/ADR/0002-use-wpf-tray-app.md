# ADR 0002 — Use WPF tray app

Decision: build a Windows user-session WPF tray app instead of a Windows Service.

Constraint: Office automation is intended for interactive desktop use, not unattended services.
Rejected: Windows Service, because Outlook COM automation can be unreliable and unsupported in non-interactive contexts.
Confidence: high
Scope-risk: narrow
Directive: do not move Outlook automation into a service process.
