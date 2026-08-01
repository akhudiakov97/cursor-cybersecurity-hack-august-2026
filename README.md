# HoneyGuard

HoneyGuard is a .NET API honeypot that turns common exploit paths into hidden tripwires.
Touching one silently bans the source IP, returns a decoy `404`, and streams the incident
to a live defender dashboard.

## Live demo

- Defender dashboard: https://cursor-cybersecurity-hack-august-2026-production.up.railway.app/
- Attack simulator: https://cursor-cybersecurity-hack-august-2026-production.up.railway.app/attack.html

## How it works

1. A normal request goes through fine (`200`).
2. A probe hits a fake sensitive route such as `/.env`.
3. Middleware bans the IP and answers with a disguised `404`.
4. Later requests from that IP stop at the edge with `403`.
5. Supabase Realtime pushes the incident to the dashboard.

The Threat Theater can run this sequence automatically or animate any custom method and
route entered by the user.

## Architecture

- `Security/HoneyGuardMiddleware.cs` — detects traps and blocks banned IPs
- `Security/BanRegistry.cs` — keeps bans in memory
- `Reporting/IncidentDispatcher.cs` — reports incidents asynchronously
- `wwwroot/index.html` — displays live incidents
- `wwwroot/attack.html` — visualizes guided and custom attacks
