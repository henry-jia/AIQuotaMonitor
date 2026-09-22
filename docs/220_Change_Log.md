# Change Log

## [20260922] README what's-new updated for v1.7.1
- **類型**：Docs
- **影響範圍**：README.md, README.en.md
- **內容**：What's-new sections now cover v1.7.1 (acrylic black-patch fix and haze removal), keeping a pointer to the v1.7.0 highlights; main fast-forwarded so the GitHub repo page shows the current README.
- **關聯文件**：`README.md`, `README.en.md`
- **操作人**：Kimi

## [20260922] Release v1.7.1
- **類型**：Bugfix
- **影響範圍**：widget backdrop rendering (transparency), release documentation
- **內容**：Ship the Acrylic transparency fix (transparent render-target clear + post-first-render reapply) and the ACCENT-based acrylic (no luminosity haze, blur persists unfocused) as v1.7.1.
- **關聯文件**：`docs/releases/v1.7.1.md`, `docs/220_Change_Log.md`
- **操作人**：Kimi

## [20260922] Fix widget backdrop black patches and acrylic haze
- **類型**：Bugfix
- **影響範圍**：widget window backdrop (app/Backdrop.cs)
- **內容**：WPF clears the render target with the opaque HwndTarget.BackgroundColor before drawing, so with a DWM backdrop the window started fully black and every partial redraw (card refresh, button hover, wheel zoom) painted its dirty region opaque black until the whole window was black; only a cross-monitor drag (DPI change → backdrop reapply) recovered. The clear color is now transparent and the backdrop is reapplied after the first frame. The widget Acrylic also moved from the system backdrop (fixed luminosity blend = milky haze, degrades to a flat gray when unfocused) to the ACCENT acrylic API — pure blur plus a minimal custom tint, darkening stays with the user-configurable XAML tint; falls back to the system backdrop if the API is unavailable.
- **關聯文件**：`docs/releases/v1.7.1.md`
- **操作人**：Kimi

## [20260906] Show version in the widget title bar
- **類型**：Feature
- **影響範圍**：widget title bar
- **內容**：The widget title bar shows the running version as dim gray text beside the title (from AssemblyVersion, e.g. `v1.7.0`), so the running build is identifiable without opening file properties.
- **關聯文件**：`README.md`, `README.en.md`, `docs/releases/v1.7.0.md`
- **操作人**：Claude

## [20260906] Release v1.7.0
- **類型**：Feature
- **影響範圍**：widget window visibility, taskbar slot, tray interaction, settings UI, i18n, release documentation
- **內容**：Ship taskbar presence (default-on "Show in taskbar" setting) and always-summon tray click as v1.7.0.
- **關聯文件**：`README.md`, `README.en.md`, `docs/releases/v1.7.0.md`
- **操作人**：Claude

## [20260906] Taskbar presence and one-click tray summoning
- **類型**：Feature
- **影響範圍**：widget window visibility (taskbar slot, tray click), settings UI, i18n, documentation
- **內容**：The widget now takes a taskbar slot while visible (default on, "Show in taskbar" setting) so a covered window is one taskbar click away; hiding to tray removes the slot. Tray left click becomes an always-summon action — show if hidden and raise to front in every case, including non-topmost windows (the old double-click toggle never raised them); it never hides, since tray-click focus state is unreliable on Win11. The tray menu's show/hide item stays a literal toggle. (NotifyIcon gotcha: its MouseClick raises MouseEventArgs.Clicks=0 always, so click-count guards must not be used.)
- **關聯文件**：`README.md`, `README.en.md`, `docs/200_Functional_Requirements.md`, `docs/400_Function_List.md`
- **操作人**：Claude

## [20260905] Release v1.6.1 — navigation timeout probes page reality
- **類型**：Bugfix
- **影響範圍**：navigation timeout handling, settings test-scrape output, regression tests, release documentation
- **內容**：On page-load timeout the scraper now probes the page before failing — heavy SPAs (e.g. ChatGPT in a fresh isolated profile) often render content long before the load event fires, so a timed-out navigation with real content continues scraping instead of erroring. Test scrape no longer prints a misleading "no rules configured" line on failure.
- **關聯文件**：`docs/releases/v1.6.1.md`, `docs/220_Change_Log.md`
- **操作人**：Kimi

## [20260905] Release v1.6.0
- **類型**：Feature
- **影響範圍**：session isolation, bonus-reset scraping, card display, settings UI, reset-time parsing, regression tests, release documentation
- **內容**：Ship per-service isolated sessions for multi-account monitoring and Codex/GLM bonus usage-reset display as v1.6.0.
- **關聯文件**：`README.md`, `README.en.md`, `docs/releases/v1.6.0.md`, `docs/200_Functional_Requirements.md`, `docs/400_Function_List.md`
- **操作人**：Kimi

## [20260905] Multi-account sessions and bonus usage-reset display
- **類型**：Feature
- **影響範圍**：session isolation (per-service WebView2 profiles), bonus-reset scraping (Codex/GLM), card display, settings UI, reset-time parsing, regression tests, documentation
- **內容**：Services can enable an "isolated session" (own WebView2 profile + encrypted cookie store keyed by service id) so multiple accounts at the same provider show their own quotas; the login window uses the same session as the scraper. Bonus usage resets are now scraped and shown: Codex "Usage limit resets" entries with their per-entry expiry, and GLM "用量重置额度" counts with expiry read from the "重置管理" dialog (opened and closed automatically, best-effort). Cards show total count plus earliest expiry with a 24-hour warning color; expired entries are hidden. Added explicit English month-name date parsing ("Oct 4, 9:57 AM") after BCL TryParse proved unreliable for that shape.
- **關聯文件**：`README.md`, `README.en.md`, `docs/200_Functional_Requirements.md`, `docs/400_Function_List.md`
- **操作人**：Kimi

## [20260902] Auto-convert remaining-labeled quota values to used
- **類型**：Bugfix
- **影響範圍**：quota parsing (percent type), auto-extract script payload, settings tooltips, regression tests, documentation
- **內容**：ChatGPT's Usage page switched from "X% used" to "X% left"; the scraper now detects remaining wording (`left`/`remaining`/`剩余`/`avail`) adjacent to the parsed percent and converts it to used (100 − x). The manual Invert toggle combines as at most one conversion (OR), never double. Selector-mode rules fall back to the container text. Fraction-type rules are unaffected.
- **關聯文件**：`README.md`, `README.en.md`, `docs/200_Functional_Requirements.md`, `docs/400_Function_List.md`
- **操作人**：Claude

## [20260825] Release v1.5.0
- **類型**：Feature
- **影響範圍**：desktop UI, quota parsing, WebView2 session persistence, shutdown lifecycle, regression tests, release documentation
- **內容**：Ship reliable encrypted session persistence, Volcengine Coding Plan refresh timing, shared 5-hour idle hints, refined Acrylic tint and pin control, and bounded shutdown behavior as v1.5.0.
- **關聯文件**：`README.md`, `README.en.md`, `docs/releases/v1.5.0.md`, `docs/200_Functional_Requirements.md`, `docs/400_Function_List.md`, `docs/verify/20260825-session-and-volc-reset.md`
- **操作人**：Codex

## [20260825] Generalize the idle hint to all 5-hour quotas
- **類型**：Feature
- **影響範圍**：quota result decoration, window-label inference, regression tests, documentation
- **內容**：Show the first-use timer hint for any unused 5-hour quota with no reset information, while preserving real countdowns and excluding longer windows; recognize hyphenated English labels such as `5-hour`.
- **關聯文件**：`README.md`, `README.en.md`, `docs/200_Functional_Requirements.md`, `docs/400_Function_List.md`
- **操作人**：Codex

## [20260825] Restore Volcengine reset timing and strengthen session persistence
- **類型**：Bugfix
- **影響範圍**：quota parsing, WebView2 authentication persistence, regression tests, documentation
- **內容**：Recognize Volcengine Coding Plan `后刷新` countdowns and compact `时` durations; explain idle current sessions without inventing a timestamp; persist encrypted cookies on all scrape outcomes, login close, and graceful exit using atomic replacement.
- **關聯文件**：`README.md`, `README.en.md`, `docs/200_Functional_Requirements.md`, `docs/400_Function_List.md`
- **操作人**：Codex
