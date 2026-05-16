# Visual QA workflow update — 2026-05-16

## Product direction

MailWhere is now treated as a **tray-first assistant**:

- The app starts in the Windows tray instead of opening the main window.
- The main recurring surface is the scheduled **오늘 업무 보드**.
- The tray menu keeps a mandatory **열기** entry for the full shell and a separate **오늘 업무 보기** entry for quick board recall.
- The main window is supplemental for settings, review candidates, diagnostics, and manual checks; users should not need to keep it open for long sessions.

## Board/card decisions

The visual QA screenshots showed crowded card actions and duplicate Main/DailyBoard behavior. The first-pass fix is:

- Keep user-facing actions simple: **열기**, **나중에**, **수정**, **보관**.
- Keep `나중에` distinct from `보관`: snoozed tasks are temporarily absent until their chosen time; archived tasks leave active lists and do not resurface.
- Replace visible close/remove variants with `보관` in primary UI. Legacy `Done`/`Dismissed` storage values remain readable for backward compatibility but are not primary actions.
- Support a bounded edit dialog for AI-derived tasks: title, simple category (`할 일`, `일정`, `기다리는 중`), and optional due date.
- Make future-snoozed and archived items absent from active board/list queries; due snoozed items return when their snooze time has passed.

## Scheduled board behavior

At the configured daily board time, MailWhere now opens or updates the board window directly. Notification is a fallback only when the board surface fails to open. A successful scheduled board open marks the daily shown date, so the same day is not repeatedly surfaced.

## Copy polish

Primary UI copy avoids developer-facing implementation/gate terms. User-facing wording now favors:

- `지금 메일 확인`
- `새 메일 자동 확인`
- `자동 확인 준비 완료`
- `업무 보드`, `오늘 업무`, `보관`, `나중에`

Internal diagnostics and legacy tests may still refer to underlying gate concepts where needed for safety/compatibility.

## Verification anchors

- Core route tests cover archived/future-snoozed visibility.
- SQLite tests cover archive persistence, task details editing, and due snooze reappearance.
- Windows build verifies the WPF surfaces and new edit dialog compile.

## Cleanup pass

The Ralph cleanup pass stayed bounded to the changed files. Fallback-like branches were classified before cleanup:

- Scheduled board notification fallback: preserved as a grounded fail-safe because it records the board error class and does not mark the day shown unless a fallback notification succeeds.
- Tray background startup catch: preserved as a grounded fail-safe because it reports the startup error and opens the shell instead of silently failing in the tray.

Small polish fixes from the pass:

- Replaced remaining primary-surface "hidden" wording with neutral "not shown again / temporarily not visible" wording.
- Kept the edit dialog error copy Korean-first instead of surfacing a domain exception message.
