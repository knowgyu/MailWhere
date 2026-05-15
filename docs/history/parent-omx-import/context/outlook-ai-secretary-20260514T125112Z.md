# Deep Interview Context Snapshot: outlook-ai-secretary

- Timestamp UTC: 20260514T125112Z
- Task statement: Clarify requirements for a Windows 11 always-on AI secretary that reads Classic Outlook mail through COM and manages summaries, tasks, reminders, and possibly calendar-like signals.
- Desired outcome: Execution-ready specification before project creation, with strong assumptions/probes/fallbacks so company-PC failures are explicit and graceful.
- Stated solution: WPF, .NET 10, Outlook COM, SQLite, portable/self-contained deployment, read-only first, LLM provider abstraction.
- Probable intent hypothesis: Build a practical high-success productivity tool for a closed Samsung office environment where server APIs/Knox/M365/Exchange are unavailable or unreliable, using what Outlook already displays.
- Known facts/evidence:
  - User likely uses Classic Outlook 2016/2019-ish, POP3/export from Samsung mySingle/Knox-like desktop into Outlook.
  - User develops at home and downloads/runs on company PC later.
  - Need documentation and code design around assumptions, capability probes, and clean feature disablement.
  - Existing /home/knowgyu/workspace contains unrelated projects; no known current implementation for this app.
- Constraints:
  - Closed corporate environment; no Microsoft Graph/M365/Exchange assumptions.
  - Company mail may be sensitive; external LLM usage should be disabled by default for company mode.
  - Avoid mySingle/Knox internal format parsing.
  - Avoid automatic sending/deleting/moving in first version.
- Unknowns/open questions:
  - The primary job-to-be-done and first daily loop the assistant must own.
  - Required autonomy level vs user approval boundary.
  - Security/data retention policy the user wants to enforce.
  - Whether calendar is Outlook Calendar, meeting mails/ICS, local shadow calendar, or manual tasks first.
  - Minimum viable company smoke-test acceptance criteria.
- Decision-boundary unknowns:
  - What the agent may decide automatically, what requires user confirmation, and what must never happen.
  - Which failure modes should disable features vs block the whole app.
- Likely codebase touchpoints: New greenfield repo/app; likely docs/, src/App, src/Connectors/OutlookCom, src/Core, src/Storage, src/LLM, src/UI.
- Prompt-safe initial-context summary status: not_needed

## Deep Interview Artifacts

- Transcript: `.omx/interviews/outlook-ai-secretary-20260514T130354Z.md`
- Spec: `.omx/specs/deep-interview-outlook-ai-secretary.md`
- Final ambiguity: 13.1%
