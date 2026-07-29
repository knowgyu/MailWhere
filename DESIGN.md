# Design

## Source truth
- Status: Active.
- Refreshed: 2026-07-29.
- Primary product surfaces:
  - Tray-resident MailWhere shell (`src/MailWhere.Windows/MainWindow.xaml`).
  - Task board, review candidates, archive, settings, toast notifications.
  - Local mail search window: separate `MailSearchWindow`, launched from the main shell.
- Evidence reviewed:
  - `src/MailWhere.Windows/MainWindow.xaml(.cs)` — primary shell, `메일 검색` launch button, child-window lifecycle, database path.
  - `src/MailWhere.Windows/MailSearchWindow.xaml(.cs)` — implemented local search UI and explicit source-open behavior.
  - `src/MailWhere.Windows/ArchiveWindow.xaml(.cs)` and `ReviewCandidatesWindow.xaml(.cs)` — secondary-window patterns, Korean status copy, Esc/double-click behavior.
  - `src/MailWhere.Windows/App.xaml` and `Resources/*.xaml` — existing WPF resource dictionaries, buttons, cards, brushes, typography, spacing.
  - `src/MailWhere.Core/Search/MailMirrorContracts.cs` — `MailMirrorSearchRequest`, `MailMirrorSearchResult`, locator contract.
  - `src/MailWhere.Storage/SqliteMailMirrorStore.cs` — `SearchAsync`, local SQLite/FTS/LIKE search, result ordering/snippet behavior.
  - `src/MailWhere.OutlookCom/OutlookComMailOpener.cs` — explicit Outlook COM source-open.
  - `.omx/ultragoal/brief.md` and `.omx/ultragoal/goals.json` — bounded search UI contract and verification goals.

## Product stance
- Personality: quiet, competent Korean-first desktop assistant; calm utility over dashboard spectacle.
- Search scope: local SQLite mail mirror only.
- Mailbox posture: read-only; search and source-open must not mutate mailbox contents or mirror rows.
- Supported search controls: query text plus one folder filter with `전체`, `받은 메일`, `보낸 메일`.
- Privacy boundary:
  - No raw body export from the search UI.
  - Bounded snippets only; no visible StoreID, EntryID, source id/hash, prompts, or full recipient lists.
  - Outlook COM is used only after explicit `원본 열기` action.
- Non-goals:
  - No new mail indexing backend.
  - No attachment search.
  - No embedding/vector/RAG search or AI-chat framing.
  - No new dependency, background worker, font package, icon set, or design-system layer.

## Personas jobs
- Primary personas:
  - Local Windows user who keeps MailWhere running in tray and wants a fast path back to source mail.
  - Managed-PC user where Outlook/EDR may be slow, so local-only search versus Outlook-open boundaries must be obvious.
- User jobs:
  - Search mirrored mail by query and All/Inbox/Sent folder filter.
  - Scan subject, sender, folder, date, and short snippet.
  - Open the original Outlook mail only when needed.
- Key contexts:
  - During task triage on the MailWhere board.
  - After a review candidate or notification references a mail thread.
  - On Windows with Classic Outlook available; Linux development can build/test non-visual seams but cannot visually smoke WPF/Outlook.

## Information architecture
- Primary navigation:
  - Main shell header remains the entry point.
  - `메일 검색` appears as a secondary header action alongside `확인 필요`, `보관함`, and `설정`.
- Core screens:
  - `MainWindow`: task board source truth.
  - `ReviewCandidatesWindow`: review queue.
  - `ArchiveWindow`: archived tasks.
  - `SettingsWindow`: configuration/developer tools.
  - `MailSearchWindow`: local mail mirror search.
- Search window hierarchy:
  - Header: title `메일 검색`, 13px muted status `로컬 메일 인덱스에서만 검색합니다.`.
  - Query/filter row: query text box, folder filter (`전체`, `받은 메일`, `보낸 메일`), primary `검색` button.
  - Results: subject first; sender/date/folder second; snippet third; `원본 열기` at row end.
  - Status, empty, loading, and sanitized error copy stay in-window.

## Visual language
- Use existing tokens only: `MwBrush.*`, `MwButton.*`, `MwPanelBorder`, `MwCardListBoxItem`, inherited `MwFont.Base`.
- Use the same rounded header/content card rhythm as `ArchiveWindow` and `ReviewCandidatesWindow`.
- Typography: 20px bold window title, 15px result title, 13px muted status/meta/snippet copy matching current WPF density.
- No custom animation, new icons, new fonts, shadows, or dependency-based controls.

## Components
- Existing components reused:
  - `MwButton.Base`, `MwButton.Primary`, `MwButton.Secondary`, `MwButton.Ghost`, `MwButton.Danger`.
  - `MwPanelBorder`, `MwCardListBoxItem`.
  - WPF `TextBlock`, `TextBox`, `ComboBox`, `ListBox`, `Button`.
- New/changed components:
  - `MailSearchWindow.xaml`.
  - `MailSearchWindow.xaml.cs`.
  - Main shell `메일 검색` launch button and single-child-window lifecycle.
- State contract:
  - Idle/blank query: query box focused; helper text `검색어를 입력하면 로컬 메일 인덱스에서 찾습니다.` visible.
  - Loading: search button disabled; status/helper `검색 중입니다…`; a repeated search cancels the prior search before starting the new one.
  - Results: status `N개를 찾았습니다.`; first result selected.
  - Empty result: `검색 결과가 없습니다. 다른 검색어를 입력해 보세요.`.
  - Missing index: `메일 검색 인덱스가 아직 없습니다. 먼저 지금 메일 확인을 실행하세요.`.
  - Search error: `검색하지 못했습니다: {ErrorClass}`.
  - Source-open busy/success/error: `Outlook에서 원본 메일을 여는 중입니다…`, `원본 메일을 열었습니다.`, or sanitized failure status.

## Accessibility
- Keyboard/focus behavior:
  - Initial focus: query `TextBox`.
  - `Enter` in query box: run search.
  - `Enter` on selected result: open source when `CanOpen`.
  - Double-click selected result: open source when `CanOpen`.
  - `Esc`: close window.
  - Access keys: `_검색`, `_원본 열기`, `_닫기(Esc)`.
- Busy/disabled behavior:
  - `검색` is disabled while a search is busy.
  - Blank queries do not search; they restore helper text and leave `검색` enabled.
  - Result list/open action is disabled during source-open.
  - `원본 열기` is disabled when `MailMirrorLocator.IsValid` is false.
- Screen-reader semantics:
  - Header status uses polite live text.
  - Status text reports helper/count/error/open result.
  - Result rows present subject, sender/date/folder, snippet in that order.
- No animation requirement.

## Responsive behavior
- Windows desktop WPF window; minimum width fits query, folder filter, and search action without horizontal result scrolling.
- Result list fills remaining height.
- Query column gets priority width; filter and buttons keep fixed/auto widths.
- Existing button/list hover behavior is sufficient; no touch-specific controls.

## Content voice
- Tone: calm, concise, Korean-first, operational.
- Terminology: `메일 검색`, `로컬 메일 인덱스`, `원본 열기`, `전체`, `받은 메일`, `보낸 메일`.
- Microcopy rules:
  - Avoid AI/RAG language in the search UI.
  - Avoid exposing implementation names like FTS5, StoreID, EntryID to normal users.
  - Error copy includes sanitized error class only, no subject/body/address/locator.

## Implementation constraints
- WPF on .NET; use existing XAML/code-behind patterns and app-level resource dictionaries from `App.xaml`.
- No `package.json` or web frontend surface.
- Search uses `SqliteMailMirrorStore.SearchAsync` only.
- Search request uses query, selected folder (`null`, `Inbox`, `Sent`), and fixed 50-result limit.
- Check the database file exists before opening to avoid creating an empty search database UI.
- Keep search cancellable so repeated Enter/search clicks do not stack stale work.
- Search itself must not require or touch Outlook COM.
- Source-open may call `OutlookComMailOpener.OpenAsync(storeId, entryId)` only after explicit user action from a valid result.
- No StoreID/EntryID/source id/hash appears on screen or in diagnostics/status.

## Verification expectations
- Automated local verification:
  - Existing mail mirror storage/CLI tests remain the main behavioral safety net.
  - Build/static verification required before completion.
  - `git diff --check` must pass.
- Manual Windows visual/Outlook smoke remains external to Linux:
  - Main shell shows `메일 검색` without crowding header controls.
  - Search window opens centered/owned and query box receives focus.
  - Blank, loading, no-results, SQLite error, and Outlook-open error states are readable.
  - Korean/English search terms display snippets without clipped controls.
  - `Enter`, result double-click, `Esc`, and access-key behavior work.
  - `원본 열기` is disabled when no valid locator exists and opens Outlook only when clicked/Enter on a selected valid result.
