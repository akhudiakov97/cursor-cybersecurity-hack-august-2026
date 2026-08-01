# .NET concepts used in HoneyGuard

This is a guided tour of the ASP.NET Core / .NET concepts this codebase relies on,
written for someone who has never worked in .NET before. Each section names the concept,
says where it lives in this project, and explains what it's doing and why. Every class
mentioned also has `///` doc comments in its own file - hovering over a type or method in
your editor will show you that same explanation inline as you read the code.

## 1. The request pipeline and middleware order

Every incoming HTTP request in ASP.NET Core flows through a chain of components called
**middleware**, configured in [Program.cs](../Program.cs). Each middleware can inspect or
modify the request, decide to short-circuit (respond immediately without calling
anything further down the chain), or call `next()` to hand off to whatever comes after
it. The response flows back out through the same chain in reverse.

Order matters - a lot. Look at the order in `Program.cs`:

```csharp
app.UseHoneyGuard();     // 1. runs first, for every request
app.UseDefaultFiles();   // 2. serves wwwroot/index.html
app.UseStaticFiles();    // 3. serves other static assets
app.MapProductsEndpoints();
app.MapDashboardEndpoints();
```

`HoneyGuard` goes first specifically so a banned IP is blocked, or a trap is sprung,
before the request ever reaches static files or a real endpoint. If it were registered
last, an attacker could reach `/api/v1/products` before HoneyGuard even had a chance to
check them.

The middleware itself lives in
[Security/HoneyGuardMiddleware.cs](../Security/HoneyGuardMiddleware.cs). Its shape - a
constructor that takes a `RequestDelegate next` plus whatever services it needs, and an
`InvokeAsync(HttpContext context)` method - is a convention ASP.NET Core recognizes
automatically. There's no interface to implement; the framework finds `InvokeAsync` by
looking for that method name via reflection when you call `app.UseMiddleware<T>()` (see
`UseHoneyGuard()` in
[Security/HoneyGuardServiceCollectionExtensions.cs](../Security/HoneyGuardServiceCollectionExtensions.cs)).

## 2. Dependency injection and the three lifetimes

.NET's built-in dependency injection (DI) container is where you register "here's how to
build a `Foo`" once, and then anything that needs a `Foo` just asks for it - typically as
a constructor parameter - instead of constructing it manually. This is what lets
`HoneyGuardMiddleware`'s constructor simply list `BanRegistry banRegistry` as a parameter
and receive a working instance without ever calling `new BanRegistry()` anywhere.

Registration happens in `AddHoneyGuard()` in
[Security/HoneyGuardServiceCollectionExtensions.cs](../Security/HoneyGuardServiceCollectionExtensions.cs).
Every registration picks one of three lifetimes, which controls how often a new instance
is created:

- **Singleton** - one instance for the entire application's lifetime, shared by every
  request. Used for [Security/BanRegistry.cs](../Security/BanRegistry.cs) and
  [Reporting/IncidentQueue.cs](../Reporting/IncidentQueue.cs), because both hold state
  (the ban list, the pending-incident queue) that must be visible across every request -
  a new instance per request would mean nothing ever actually stayed banned.
- **Scoped** - one instance per request (not used directly in this project, but common in
  apps with a database context, where you want one connection/unit-of-work per request).
- **Transient** - a brand new instance every single time it's requested (also not used
  here; a good fit for small, stateless, cheap-to-create helper classes).

Getting the lifetime wrong is a classic .NET bug: registering a stateful class like
`BanRegistry` as transient or scoped would silently make bans "disappear" because every
lookup would hit a fresh, empty dictionary.

## 3. The options pattern (`IOptions<T>`) and configuration binding

Rather than reading configuration values as raw strings scattered through the codebase
(`configuration["HoneyGuard:BanDuration"]`), .NET encourages the **options pattern**: you
describe your settings as a plain class, bind JSON configuration onto it once, and then
inject a strongly-typed wrapper wherever you need those settings.

[Configuration/HoneyGuardOptions.cs](../Configuration/HoneyGuardOptions.cs) is that plain
class. The binding happens once, in `AddHoneyGuard()`:

```csharp
services.Configure<HoneyGuardOptions>(configuration.GetSection(HoneyGuardOptions.SectionName));
```

That line reads the `"HoneyGuard"` section out of [appsettings.json](../appsettings.json)
(and [appsettings.Development.json](../appsettings.Development.json), and
`dotnet user-secrets`, which are layered on top in that order - each layer overriding the
one before it) and matches JSON property names to the class's properties.

Anywhere else in the codebase that needs a setting - like
[Security/HoneyGuardMiddleware.cs](../Security/HoneyGuardMiddleware.cs) or
[Reporting/IncidentDispatcher.cs](../Reporting/IncidentDispatcher.cs) - takes an
`IOptions<HoneyGuardOptions> options` constructor parameter and reads `options.Value` to
get the bound instance. The one gotcha worth knowing: `IOptions<T>.Value` is computed
once and cached, so it won't reflect config file changes made while the app is running -
that's what `IOptionsSnapshot<T>` and `IOptionsMonitor<T>` are for, though HoneyGuard
doesn't need either since its settings don't change at runtime.

## 4. Minimal APIs and route registration

ASP.NET Core has two common styles for defining HTTP endpoints: **MVC controllers**
(classes with `[HttpGet]`/`[HttpPost]` attributes on methods) and **minimal APIs**
(routes registered directly with small delegates, no controller class required). This
project uses minimal APIs throughout, since none of the endpoints need the extra
structure controllers provide.

See [Endpoints/ProductsEndpoints.cs](../Endpoints/ProductsEndpoints.cs):

```csharp
app.MapGet("/api/v1/products", () => SampleCatalog);
```

That single line registers a route, a handler, and (implicitly) JSON serialization of
whatever the handler returns. Grouping related routes behind an extension method like
`MapProductsEndpoints(this WebApplication app)` keeps `Program.cs` from turning into one
giant file as more endpoints get added - it's the same "extension method as a namespace
for related functionality" trick described below.

## 5. Extension methods (`AddHoneyGuard`, `UseHoneyGuard`, `MapProductsEndpoints`)

An **extension method** is a static method that appears to "add" a new method to an
existing type, without editing that type's source code. The trick is the `this` keyword
on the first parameter:

```csharp
public static IServiceCollection AddHoneyGuard(this IServiceCollection services, ...)
```

Because of that `this`, you can call `builder.Services.AddHoneyGuard(...)` as if
`AddHoneyGuard` were a method built into `IServiceCollection` itself, even though it's
really just a regular static method defined in
[Security/HoneyGuardServiceCollectionExtensions.cs](../Security/HoneyGuardServiceCollectionExtensions.cs).
This is exactly how ASP.NET Core's own `AddControllers()`, `AddHttpClient()`, and
`UseStaticFiles()` are implemented, and it's why HoneyGuard's own setup
(`AddHoneyGuard()` / `UseHoneyGuard()`) is written the same way - it should feel exactly
as familiar to use as the framework's built-in calls.

## 6. `ConcurrentDictionary` and thread safety

A single ASP.NET Core application typically handles many requests at the same time, on
different threads. [Security/BanRegistry.cs](../Security/BanRegistry.cs) is a singleton
shared by every one of those concurrent requests, so its internal ban list must be safe
to read and write from multiple threads simultaneously - a plain `Dictionary<TKey,TValue>`
would corrupt its internal state or throw under that kind of concurrent access.

`System.Collections.Concurrent.ConcurrentDictionary<TKey,TValue>` solves this by handling
its own internal locking, so operations like `dictionary[key] = value` or
`TryGetValue(...)` are safe to call from any number of threads at once without you having
to write `lock` statements yourself.

## 7. `System.Threading.Channels` for producer/consumer hand-off

[Reporting/IncidentQueue.cs](../Reporting/IncidentQueue.cs) uses a `Channel<Incident>` to
pass incidents from the request pipeline (the "producer") to a background task (the
"consumer") that actually talks to Supabase.

A common beginner shortcut for "do this without blocking the request" is
`_ = Task.Run(() => DoSomethingAsync())` - fire off a task and don't wait for it. That
pattern has two real problems: nothing observes the task, so an exception thrown inside
it disappears silently instead of getting logged; and there's no way to apply
back-pressure if work arrives faster than it can be processed.

A `Channel<T>` is a queue purpose-built for this hand-off. The producer side
(`TryWrite`) is instant and never blocks - exactly what a request-handling thread needs.
The consumer side is an `async` loop (`await foreach (var item in reader.ReadAllAsync())`)
that asynchronously waits for new items, in a place (a `BackgroundService`, see below)
where an exception can be caught and logged without crashing anything else. The channel
is also **bounded** (a fixed capacity), so if Supabase is ever unreachable for a while,
old incidents get dropped instead of memory growing without limit.

## 8. `BackgroundService` for long-running background work

[Reporting/IncidentDispatcher.cs](../Reporting/IncidentDispatcher.cs) needs to run for
the entire lifetime of the application, continuously waiting for new incidents and
sending them to Supabase. `Microsoft.Extensions.Hosting.BackgroundService` is the
standard base class for exactly this: you override `ExecuteAsync(CancellationToken)`
with your loop, and the ASP.NET Core host takes care of starting it when the application
starts and signalling the `CancellationToken` when the application is shutting down.

It's registered with `services.AddHostedService<IncidentDispatcher>()` rather than
`AddSingleton` - `AddHostedService` is what tells the host "start this thing running in
the background," on top of also making it available through DI like any other service.

## 9. Typed `HttpClient` via `IHttpClientFactory`

`IncidentDispatcher` needs an `HttpClient` to POST incidents to Supabase. Manually
writing `new HttpClient()` in .NET is a well-known trap: doing that repeatedly can
exhaust the machine's available sockets, and a long-lived single instance can end up
using stale DNS results forever. `IHttpClientFactory` (added implicitly here via
`services.AddHttpClient<IncidentDispatcher>()` in
[Security/HoneyGuardServiceCollectionExtensions.cs](../Security/HoneyGuardServiceCollectionExtensions.cs))
manages a pool of `HttpClient`s under the hood and hands `IncidentDispatcher` a
ready-to-use, correctly-recycled `HttpClient` through its constructor - the class never
has to think about the lifecycle itself.

## 10. `async`/`await`, all the way down

Every operation in this codebase that involves waiting for something outside the process
- an HTTP call to Supabase, reading from a `Channel` - is `async` and awaited with
`await`, rather than calling a blocking equivalent (like `.Result` or `.Wait()`). This
matters because blocking a thread while it waits for network I/O ties up that thread for
nothing; `await` instead frees the thread to go handle other requests while the I/O is in
flight, and picks the method back up on a thread from the pool once the result is ready.
`Program.cs`, `HoneyGuardMiddleware.InvokeAsync`, and `IncidentDispatcher.ExecuteAsync`
are all part of this same async chain from the moment a request arrives to the moment an
incident is safely stored in Supabase.
