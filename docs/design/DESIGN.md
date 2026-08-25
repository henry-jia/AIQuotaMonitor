# AIQuotaMonitor UI Design Anchor

Direction: Modern Minimal with Tech Utility density. This records the existing product direction; it does not introduce a new brand.

## 1. Color

- `surface-window-fallback`: `oklch(0.18 0.01 285)` / `#14141B`.
- `surface-glass-tint`: `oklch(0.18 0.01 285 / 40%)` / `#6614141B`; use only for the DWM Acrylic window shell.
- `surface-card`: white at 5% over the shell / `#0DFFFFFF`.
- `hairline-glass`: white at 20% / `#33FFFFFF`.
- `text-primary`: `#F2F2F5`; `text-secondary`: `#8A8A95`.
- `action-primary`: `oklch(0.68 0.16 255)` / `#4F8CFF`.
- State colors remain semantic: normal, near baseline, ahead of baseline, and critical.

## 2. Typography

- Display and body: `Segoe UI Variable`, falling back to `Segoe UI`; CJK uses the system fallback.
- Numeric and utility labels favor tabular alignment where available.
- Scale: 10.5px utility, 13px card/title, with semibold reserved for hierarchy.

## 3. Spacing

- Base rhythm: 4px and 8px.
- Shell inset: 16px horizontal, 12-14px vertical.
- Card inset: 14px horizontal, 12px vertical.
- Avoid arbitrary spacing outside the 4/8px rhythm unless optical alignment requires it.

## 4. Layout

- The widget is content-sized, borderless, and fixed-layout.
- Horizontal cards are 240px wide; vertical cards have a 250px minimum width.
- The command strip stays visually subordinate to quota data.

## 5. Components

- Glass shell: DWM Acrylic blur, restrained tint, one hairline border, and a subtle top reflection.
- Cards: translucent solid layer for legibility; cards do not add a second blur.
- Icon buttons: transparent by default, white hover wash, subdued pressed state, visible disabled state.
- Progress bars: color communicates quota state; baseline ticks remain secondary.

## 6. Motion

- Motion is functional and brief. Refresh and state changes may animate; decorative ambient motion is excluded.
- Preserve direct drag response and avoid generic fade-in effects across the dashboard.

## 7. Voice

- Short, factual, and operational: quota, reset time, expiry, refresh, and pause.
- Chinese and English labels should carry the same information density.

## 8. Brand

- Point of view: a quiet desktop instrument for monitoring scarce AI quota.
- Attributes: precise, calm, compact.
- Not: playful, promotional, ornamental.

## 9. Anti-patterns

- No decorative glassmorphism on every card; blur belongs to the window shell.
- No purple-blue gradients, glow halos, decorative emoji, or pure-black surfaces.
- No uniform oversized radius; shell/cards use 8px, controls use 4-6px.
- Do not reduce contrast to make transparency more obvious.
