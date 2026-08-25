# Verification: session persistence and Volcengine reset timing

Date: 2026-08-25 (Asia/Shanghai)

## Deterministic regression runner

Command:

```powershell
dotnet run --project tests/AIQuotaMonitor.RegressionTests/AIQuotaMonitor.RegressionTests.csproj -c Release
```

Result: `PASS 20/20`.

Coverage includes reset/refresh terminology, compact Chinese `时` durations, false-positive rejection, same-line extraction, generalized 5-hour idle hints (Chinese and English labels, real countdown preservation, non-5-hour exclusion, idempotence), DPAPI create/replace/read, corrupt ciphertext rejection, UTC/local expiry boundaries, and bounded completion/timeout behavior for graceful shutdown.

## Isolated WebView2 session smoke

Command:

```powershell
dotnet app/bin/Release/net10.0-windows/AIQuotaMonitor.dll --test-session-persistence <temp-output.json>
```

Result:

```json
{
  "success": true,
  "saved": true,
  "restored": true,
  "sessionPreserved": true,
  "expiryPreserved": true,
  "expiryDeltaSeconds": 0,
  "errorCode": null
}
```

The smoke uses an isolated temporary WebView2 profile and synthetic `example.invalid` cookies. It does not read or modify real account cookies.

## Isolated Volcengine-equivalent DOM smoke

Command:

```powershell
dotnet app/bin/Release/net10.0-windows/AIQuotaMonitor.dll --test-volc-dom <temp-output.json>
```

Result:

```json
{
  "success": true,
  "currentSession": "当前时段暂无调用",
  "weekly": "5天11时56分钟后刷新",
  "monthly": "28天11时56分钟后刷新",
  "errorCode": null
}
```

This runs the production WebView JavaScript extractor against sibling DOM nodes matching the visible Coding Plan layout.

## Build and WPF render

- `dotnet build app/AIQuotaMonitor.csproj -c Release --no-restore`: succeeded, 0 errors; existing `NU1510` warning only.
- `dotnet list app/AIQuotaMonitor.csproj package --vulnerable --include-transitive`: no vulnerable packages reported by the configured sources.
- `--test-shot` WPF render: PNG generated and visually inspected successfully.

## Remaining manual check

A real Volcengine-account login → application restart → authenticated scrape was not performed because it depends on the user's live provider session. Server-side session expiry remains provider-controlled and intentionally cannot be bypassed.
