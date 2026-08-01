# HoneyGuard

HoneyGuard adds hidden trap routes to a .NET API. If someone probes one, their IP is
blocked immediately and the incident appears on a live dashboard.

The block happens in memory, so it does not depend on Supabase. Supabase only stores
incidents and sends them to the dashboard in real time.

## Demo flow

1. A normal API request returns `200 OK`.
2. The attacker probes `/api/v1/admin/config` and receives a disguised `404 Not Found`.
3. HoneyGuard bans the IP.
4. The attacker's next valid request returns `403 Forbidden`.
5. The dashboard updates instantly without a refresh.

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
tab and click **[ run attack sequence ]** — no terminal needed. See
[Attack it from the browser](#attack-it-from-the-browser) below for how that page works.

Use **[ clear bans ]** on either page to run the demo again.

## Attack it from the browser

`wwwroot/attack.html` is a self-service "attacker console" so the whole demo — normal
request, trap probe, ban, block — can be run end-to-end from two browser tabs, with no
terminal required.

A browser cannot set the `X-Forwarded-For` header itself (`fetch` treats it as forbidden),
which is what `demo/attack.sh` relies on to simulate a public attacker IP. Instead, the
attacker page rolls a fresh `203.0.113.x` address on every page load and sends it in a
custom `X-HoneyGuard-Demo-Ip` header. The server only trusts that header when
`HoneyGuard:DemoMode` is turned on (see `Configuration/HoneyGuardOptions.cs`) — it must
stay off for any deployment where IPs need to mean something real, since the header value
is just a claim the caller makes about itself.

Open the dashboard and the attacker console side by side to watch incidents land in real
time as you probe.

## Light and dark mode

Both `wwwroot/index.html` and `wwwroot/attack.html` support light and dark themes via a
`[ dark ]` / `[ light ]` toggle in the title bar. The choice is stored in
`localStorage` and applied before the page paints (`wwwroot/theme.js`), so there is no
flash of the wrong theme on load. With no stored preference, the page follows the
browser's `prefers-color-scheme`.

## Deploy to Railway

The repo includes a `Dockerfile`, so Railway can build and deploy it directly:

1. Create a new Railway project from this GitHub repo. Railway detects the `Dockerfile`
   and builds it automatically — no other configuration is required.
2. Set these service variables:
   - `HoneyGuard__SupabaseServiceRoleKey` — the Supabase `service_role` key (secret, never commit this)
   - `HoneyGuard__DemoMode` = `true` — lets `/attack.html` simulate attacker IPs, and lets **[ clear bans ]** work outside Development
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
- `wwwroot/index.html` — displays incidents through Supabase Realtime
- `wwwroot/attack.html` — self-service attacker console for the browser
- `wwwroot/theme.js` — shared light/dark theme toggle for both pages
- `demo/attack.sh` — runs the three-request demo from a terminal
- `Dockerfile` — builds the app for deployment (e.g. Railway)

New to .NET? Read [docs/DOTNET-NOTES.md](docs/DOTNET-NOTES.md) for a guided walkthrough
of the code.
