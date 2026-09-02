# Functional Requirements

## Quota reset timing

- The monitor shall extract both reset and refresh terminology from quota rows, including compact Chinese durations such as `5天11时56分钟后刷新`.
- Relative reset text shall be converted to an absolute local timestamp at scrape time and rendered according to the user's countdown/date threshold settings.
- When a provider explicitly reports that an idle session has no active window, the monitor shall explain when timing begins and shall not invent a reset timestamp.
- Any zero-usage 5-hour quota without reset information shall show that its timer starts on first use; existing reset countdowns and non-5-hour quotas shall remain unchanged.

## Authentication persistence

- WebView2 cookies, including session cookies, shall be persisted for the current Windows user after every scrape outcome, after the login window closes, and before graceful application exit.
- Persisted authentication data shall be protected with Windows DPAPI and replaced atomically.
- Client persistence shall not alter or bypass provider-controlled server expiry.
