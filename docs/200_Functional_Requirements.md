# Functional Requirements

## Quota value semantics

- When a quota value is explicitly labeled as remaining (`25% left`, `剩余 73%`, `Remaining: 73%`), the monitor shall convert it to the used percent (100 − value) automatically at parse time.
- The manual "value is remaining percent" toggle shall remain available for unlabeled remaining values and shall combine with automatic detection into at most one conversion, never two.

## Quota reset timing

- The monitor shall extract both reset and refresh terminology from quota rows, including compact Chinese durations such as `5天11时56分钟后刷新`.
- Relative reset text shall be converted to an absolute local timestamp at scrape time and rendered according to the user's countdown/date threshold settings.
- When a provider explicitly reports that an idle session has no active window, the monitor shall explain when timing begins and shall not invent a reset timestamp.
- Any zero-usage 5-hour quota without reset information shall show that its timer starts on first use; existing reset countdowns and non-5-hour quotas shall remain unchanged.

## Authentication persistence

- WebView2 cookies, including session cookies, shall be persisted for the current Windows user after every scrape outcome, after the login window closes, and before graceful application exit.
- Persisted authentication data shall be protected with Windows DPAPI and replaced atomically.
- Client persistence shall not alter or bypass provider-controlled server expiry.

## Multi-account sessions

- Services share the default browser profile (one sign-in per domain) unless "isolated session" is enabled on the service; an isolated service shall use its own WebView2 profile and its own encrypted cookie store, both keyed by the stable service id, so multiple accounts at the same provider can be monitored side by side.
- The interactive login window and the scraper shall always operate on the same session for a given service, and session saves shall cover every initialized profile.

## Bonus usage resets

- Providers that grant extra usage resets (Codex "Usage limit resets", GLM "用量重置额度") shall be detected on the usage page without extra configuration, reporting scope, count, and per-entry expiry when available.
- When expiry is only visible behind a "manage resets" dialog (GLM), the scraper shall open the dialog, read the per-entry expiry, and close it again; any failure in this scan shall not affect the quota result.
- The card shall show the total available count and the earliest expiry (formatted like other reset times, warning color inside 24 hours); already-expired entries shall be hidden.

## Codex credits

- Standard ChatGPT / Codex usage pages, including the legacy `https://chatgpt.com/#settings/Usage` hash route, shall scan the current Credits balance and automatic-reload switch without extra configuration or navigation. Credit usage-history entries are not balance values; scan failures shall not affect quota results.
- The Credits monitor shall appear directly below the weekly quota (or at the card end if no weekly label is recognized), showing current balance, locally observed consumption, and automatic-reload state.
- Because ChatGPT provides no fixed Credits total, the bar shall represent the remaining share of the highest balance observed locally. That reference is persisted with the last successful result and rises automatically after a reload or grant; it shall not be presented as an official percentage.
- A failed best-effort Credits scan shall retain the previous balance snapshot, and stale whole-card fallback shall include Credits.

## Window summoning

- While the widget is visible it may take a taskbar slot ("Show in taskbar", default on) so a window covered by others is one taskbar click away; hiding to tray shall remove the slot.
- A single left click on the tray icon shall always summon: show if hidden and raise to the front in every case, including non-topmost windows above the current foreground window; it shall never hide. Hiding stays on the title-bar hide button, the widget context menu, and the tray menu's show/hide item (a literal toggle, not focus-dependent).
