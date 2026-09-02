# Change Log

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
