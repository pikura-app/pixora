# Security Policy

## Supported Versions

| Version | Supported |
|---------|-----------|
| 1.7.x   | ✅ Active  |
| < 1.7   | ❌ No longer supported |

## Reporting a Vulnerability

**Please do not open a public GitHub issue for security vulnerabilities.**

Report vulnerabilities privately via [GitHub Security Advisories](https://github.com/pikura-app/pikura/security/advisories/new).
Include as much detail as possible:

- Steps to reproduce
- Affected version(s)
- Potential impact
- Any suggested fix (optional)

We aim to acknowledge reports within **48 hours** and provide a resolution or mitigation within **14 days** depending on severity.

## Scope

The following areas are considered in-scope:

- **Credential storage** — `PHPSESSID` and OAuth refresh tokens are encrypted at rest using Windows DPAPI (`CurrentUser` scope) on Windows, and AES-256-GCM with a PBKDF2-derived key on macOS/Linux. Vulnerabilities that allow these to be extracted from disk are in scope.
- **Playwright/Chromium login flow** — the embedded Chromium browser used for Pixiv authentication. Anything that could leak session cookies beyond the local machine is in scope.
- **IPC channel** — the named-pipe channel between the UI and the background agent (`Pikura.Agent`). Privilege escalation or injection via this channel is in scope.
- **Settings file** — `%AppData%\Pikura\settings.json` and `accounts.json`. Path traversal or unintended write access is in scope.
- **Dependency vulnerabilities** — known CVEs in bundled NuGet packages (Playwright, SkiaSharp, Avalonia, etc.) that have an available fix.

## Out of Scope

- Vulnerabilities in Pixiv's own servers or API — report those directly to Pixiv.
- Rate limiting or account suspension caused by misuse of Pikura's download settings.
- Issues that require physical access to an already-compromised machine.
- Social engineering attacks.

## Credential Handling

Pikura never transmits credentials to any server other than `pixiv.net` and `i.pximg.net`. Specifically:

- `PHPSESSID` and refresh tokens are stored **encrypted on disk** and held **plaintext only in memory** during the active session.
- No telemetry, analytics, or crash reporting services receive any credential data.
- The Playwright browser profile is stored locally under `%AppData%\Pikura\.playwright` and is never synced externally.
