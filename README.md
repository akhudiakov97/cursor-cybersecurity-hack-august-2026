# HoneyGuard

HoneyGuard hides trap routes inside a .NET API. Probe one, and it blocks your IP
instantly — no waiting, no database round-trip — and the attack shows up live on a
dashboard.

## Try it — takes 30 seconds

- Defender dashboard: https://cursor-cybersecurity-hack-august-2026-production.up.railway.app/
- Attack simulator: https://cursor-cybersecurity-hack-august-2026-production.up.railway.app/attack.html

Open both side by side. In the Threat Theater, choose an attacker IP and click **Start
3-step attack**. The cinematic stepper shows the normal request, decoy hit, and automatic
block while the incident lands on the Defense Center. You can also send an exact HTTP
method and API path from the operator console.

## What happens

1. A normal request goes through fine (`200`).
2. The "attacker" probes a hidden trap route and gets a disguised `404 Not Found` — but
   is banned the instant they touch it.
3. Their next request is blocked (`403 Forbidden`).
4. The dashboard updates instantly, no refresh needed.

## Run it

You need the .NET 10 SDK and the Supabase project configured in `appsettings.json`.

Store the Supabase `service_role` key locally:

```bash
dotnet user-secrets set "HoneyGuard:SupabaseServiceRoleKey" "<your-service-role-key>"
```

Start the app:

```bash
dotnet run
```

Open [http://localhost:5245](http://localhost:5245), then either run the attack
simulation from a terminal:

```bash
./demo/attack.sh
```

or open [http://localhost:5245/attack.html](http://localhost:5245/attack.html) in another
tab and click **Start 3-step attack** — no terminal needed. See
[Attack it from the browser](#attack-it-from-the-browser) below for how that page works.

Click **Clear active bans** on either page to replay with the same attacker IP.

## Attack it from the browser

`wwwroot/attack.html` is a self-service Threat Theater, so the whole demo — normal request,
trap probe, ban, block — can be run end-to-end from two browser tabs with no terminal.
Choose a simulated IPv4 address manually or generate one with a click, then use **Start
3-step attack** for the guided visual story.

The operator console also accepts an exact HTTP method and same-origin API path. Send
requests manually, or use the one-click presets for a normal endpoint and common hidden
traps. The response status and body stay visible so it is easy to explore how the ban
affects subsequent requests.

A browser cannot set the `X-Forwarded-For` header itself (`fetch` treats it as forbidden),
which is what `demo/attack.sh` relies on to simulate a public attacker IP. Instead, the
attacker page sends a simulated IP in a custom `X-HoneyGuard-Demo-Ip` header. The server
only trusts that header when `HoneyGuard:DemoMode` is turned on (see
`Configuration/HoneyGuardOptions.cs`) — it must stay off for any deployment where IPs need
to mean something real, since the header value is just a claim the caller makes about
itself.

Open the dashboard and the attack simulator side by side to watch incidents land in real
time as you probe.

## Deploy to Railway

The repo includes a `Dockerfile`, so Railway can build and deploy it directly:

1. Create a new Railway project from this GitHub repo. Railway detects the `Dockerfile`
   and builds it automatically — no other configuration is required.
2. Set these service variables:
   - `HoneyGuard__SupabaseServiceRoleKey` — the Supabase `service_role` key (secret, never commit this)
   - `HoneyGuard__DemoMode` = `true` — lets `/attack.html` simulate attacker IPs, and lets **Clear active bans** work outside Development
   - `HoneyGuard__TrustForwardedForHeader` = `true` — trusts Railway's edge proxy to set `X-Forwarded-For` honestly
   - `HoneyGuard__BanDuration` = `00:02:00` (optional) — shorter bans make repeat demos faster
   - `ASPNETCORE_ENVIRONMENT` = `Production`
3. Deploy. Railway assigns a public URL and injects `$PORT`, which the `Dockerfile`'s
   `CMD` already binds to via `ASPNETCORE_HTTP_PORTS`.

Two things to know about running this in a hosted demo:

- Bans live in an in-memory singleton (`Security/BanRegistry.cs`), so keep the service at
  **one replica**. Multiple replicas would each track their own bans independently, and
  any redeploy clears all bans (which is fine for a demo, but worth knowing).
- `DemoMode` and `TrustForwardedForHeader` both mean "trust what the caller/proxy claims
  about its own IP". That is exactly what makes the demo self-service, and exactly why
  neither should be enabled for an app defending something real.

## Main pieces

- `Security/HoneyGuardMiddleware.cs` — detects traps and blocks banned IPs
- `Security/BanRegistry.cs` — keeps bans in memory
- `Reporting/IncidentDispatcher.cs` — sends incidents to Supabase in the background
- `wwwroot/index.html` — defender dashboard, displays incidents through Supabase Realtime
- `wwwroot/attack.html` — self-service attack simulator for the browser
- `demo/attack.sh` — runs the three-request demo from a terminal
- `Dockerfile` — builds the app for deployment (e.g. Railway)

New to .NET? Read [docs/DOTNET-NOTES.md](docs/DOTNET-NOTES.md) for a guided walkthrough
of the code.
