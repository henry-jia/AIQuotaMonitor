# Function List

## Provider quota monitoring

- Automatic quota-row location across the main document, same-origin frames, and open shadow roots.
- Percentage/fraction extraction with reset or refresh countdown recognition.
- Automatic remaining-to-used conversion for values explicitly labeled `left`/`remaining`/`剩余` (ChatGPT-style `25% left` rows).
- Volcengine Coding Plan support for current-session, weekly, and monthly refresh states.
- Shared idle-state hint for zero-usage 5-hour windows that have not started a reset timer.
- Bonus usage resets (Codex "Usage limit resets", GLM "用量重置额度") with per-entry expiry, including opening GLM's "重置管理" dialog to read it.
- Configurable countdown versus exact-date rendering.

## Session continuity

- Shared WebView2 profile for scraping and interactive login; per-service "isolated session" option with its own profile and cookie store for multiple accounts at the same provider.
- DPAPI-encrypted session-cookie export and startup restore.
- Save checkpoints after scrape outcomes, login completion, and graceful exit.
- Provider server-expiry detection and sign-in prompt.
