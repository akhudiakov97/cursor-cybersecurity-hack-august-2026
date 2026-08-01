# HoneyGuard

Active-defense security middleware for .NET web applications. HoneyGuard injects
honeypot trap routes into a web API, bans attacking IPs in memory the instant a trap is
hit (0ms latency overhead), and broadcasts the incident to a live dashboard over
Supabase Realtime.

New to .NET? Read [docs/DOTNET-NOTES.md](docs/DOTNET-NOTES.md) for a guided tour of every
concept used in this codebase (middleware, dependency injection, the options pattern,
minimal APIs, `BackgroundService`, and more), each pointing at the exact file it appears in.

## How it works

```
Attacker / Scanner
        |
        v
.NET Web API (this project)
  1. Check IP against in-memory ban list       -> 403 if banned
  2. If the route is a trap                    -> ban IP, return 404
  3. Otherwise                                 -> handle normally
        |
        v  (async, off the request path)
Supabase (Postgres `incidents` table + Realtime)
        |
        v  (WebSocket)
wwwroot/index.html dashboard (Tailwind + supabase-js)
```

## Prerequisites

- .NET 10 SDK
- A Supabase project (this repo already targets one - see `HoneyGuard:SupabaseUrl` in
  [appsettings.json](appsettings.json))
- The Supabase `incidents` table, RLS policy, and Realtime publication, created with:

```sql
create table public.incidents (
  id          bigint generated always as identity primary key,
  ip_address  text not null,
  path        text not null,
  method      text not null,
  user_agent  text,
  trap_name   text,
  action      text not null default 'ip_banned',
  created_at  timestamptz not null default now()
);
create index incidents_created_at_idx on public.incidents (created_at desc);

alter table public.incidents enable row level security;
create policy "public read incidents" on public.incidents
  for select to anon using (true);

alter publication supabase_realtime add table public.incidents;
```

## Setup

1. Restore packages:

   ```bash
   dotnet restore
   ```

2. Set the Supabase **service_role** secret key (never committed to source - see
   `HoneyGuardOptions.SupabaseServiceRoleKey`). Find it under
   **Project Settings > API keys** in the Supabase dashboard, then run:

   ```bash
   dotnet user-secrets set "HoneyGuard:SupabaseServiceRoleKey" "<your-service-role-key>"
   ```

3. Run the app:

   ```bash
   dotnet run
   ```

   By default it listens on `http://localhost:5245`. Open that URL in a browser to see
   the live dashboard.

## Running the 3-minute demo

With the app running and the dashboard open in a browser:

```bash
./demo/attack.sh
```

This simulates an attacker from a spoofed IP (via `X-Forwarded-For`, only trusted in
Development):

1. `GET /api/v1/products` -> `200 OK`
2. `GET /api/v1/admin/config` (a honeypot trap) -> `404 Not Found`, and the IP is banned
3. `GET /api/v1/products` again -> `403 Forbidden`, blocked instantly

Watch the dashboard: it flashes red, plays an alert tone, and the incident appears in the
live feed the instant step 2 happens - no page refresh.

You can also drive the same three steps manually from [hack.http](hack.http) (VS Code
REST Client / JetBrains HTTP Client) or import them into Postman.

Click **Reset Demo** on the dashboard (or `POST /api/dashboard/reset`) to clear every
in-memory ban and run the demo again.

## Project layout

| Path | What it is |
| --- | --- |
| `Configuration/HoneyGuardOptions.cs` | Strongly-typed settings bound from config |
| `Security/BanRegistry.cs` | Thread-safe in-memory ban list |
| `Security/HoneyGuardMiddleware.cs` | Request pipeline logic: block / trap / allow |
| `Security/HoneyGuardServiceCollectionExtensions.cs` | `AddHoneyGuard()` / `UseHoneyGuard()` |
| `Reporting/Incident.cs`, `IncidentQueue.cs`, `IncidentDispatcher.cs` | Async incident reporting to Supabase |
| `Endpoints/ProductsEndpoints.cs` | The real, legitimate API |
| `Endpoints/DashboardEndpoints.cs` | Config + reset endpoints for the dashboard |
| `wwwroot/index.html` | The live threat dashboard |
| `demo/attack.sh` | Scripted 3-step attacker simulation |
| `docs/DOTNET-NOTES.md` | .NET concept walkthrough for newcomers |
