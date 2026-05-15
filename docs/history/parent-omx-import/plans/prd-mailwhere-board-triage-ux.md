# PRD — MailWhere board triage UX

## Goal

Make MailWhere feel less like a configuration/debug utility and more like a quiet always-on secretary.

## User-facing outcome

- The daily board shows what the user should do next, not technical analysis details.
- Review-needed items are summarized as a compact count with one clear action to review them.
- Diagnostics are still available for troubleshooting but are no longer a prominent everyday affordance.
- LLM extraction follows a clearer internal triage policy so it is more consistent about task/review/ignore decisions.

## Scope

### Included

- Development-only triage policy artifact under `.omx/plans/`.
- LLM prompt policy improvements and few-shot examples embedded in the analyzer prompt.
- Main window copy/layout adjustments that reduce prominent diagnostics exposure.
- Daily board simplification:
  - action-first sections
  - no visible confidence percentage
  - no default LLM/source-origin labels
  - no long reason/evidence text in primary cards
  - compact review-needed summary
- Test coverage for prompt-policy presence and simplified board display formatting where practical.

### Excluded

- Sender organization/department extraction or display.
- LLM throughput benchmarking.
- Mail mutation, reply drafting, attachment analysis, or full calendar sync.
- Visual pixel-perfect validation from WSL.

## Acceptance criteria

- Main/daily board UI does not surface LLM confidence as a default visible row/card field.
- Daily board primary task cards prioritize due date + action title + optional sender-neutral source affordance.
- Review candidates on the daily board collapse to a short count/CTA rather than a long list.
- Diagnostics are accessible via a less prominent troubleshooting/settings entry.
- LLM prompt includes explicit task/review/ignore policy and at least a few compact examples.
- `dotnet test` passes.
- Windows publish script or equivalent build path succeeds where available.

## Risks

- WPF visual quality cannot be fully validated in WSL; rely on build/static tests and user screenshot follow-up.
- Prompt changes can improve consistency but require real-world feedback or synthetic fixtures to prove precision/recall.

