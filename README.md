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

Open [http://localhost:5245](http://localhost:5245), then run the attack simulation in
another terminal:

```bash
./demo/attack.sh
```

Use **[ clear bans ]** on the dashboard to run the demo again.

## Main pieces

- `Security/HoneyGuardMiddleware.cs` — detects traps and blocks banned IPs
- `Security/BanRegistry.cs` — keeps bans in memory
- `Reporting/IncidentDispatcher.cs` — sends incidents to Supabase in the background
- `wwwroot/index.html` — displays incidents through Supabase Realtime
- `demo/attack.sh` — runs the three-request demo

New to .NET? Read [docs/DOTNET-NOTES.md](docs/DOTNET-NOTES.md) for a guided walkthrough
of the code.
