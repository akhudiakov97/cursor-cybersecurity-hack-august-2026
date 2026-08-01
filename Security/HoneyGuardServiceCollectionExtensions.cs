using HoneyGuard.Configuration;
using HoneyGuard.Reporting;
using Microsoft.Extensions.DependencyInjection;

namespace HoneyGuard.Security;

/// <summary>
/// "Extension methods" let you add new methods to a type without editing that type's own
/// source - here, `AddHoneyGuard()` reads as if <c>IServiceCollection</c> itself grew a new
/// method, even though it is really just a static method that takes an
/// <c>IServiceCollection</c> as its first parameter, marked with <c>this</c>.
///
/// This is the idiomatic way almost every real ASP.NET Core library packages its setup:
/// think <c>services.AddControllers()</c>, <c>services.AddAuthentication()</c>, or
/// <c>services.AddHttpClient()</c>. Grouping related registrations behind one call keeps
/// Program.cs short and gives the library a single, discoverable entry point.
/// </summary>
public static class HoneyGuardServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything HoneyGuard's middleware needs: the options binding, the ban
    /// registry, and the incident queue plus its background dispatcher.
    /// </summary>
    public static IServiceCollection AddHoneyGuard(this IServiceCollection services, IConfiguration configuration)
    {
        // Binds the "HoneyGuard" section of configuration onto HoneyGuardOptions. Anything
        // that later injects IOptions<HoneyGuardOptions> receives the bound values.
        services.Configure<HoneyGuardOptions>(configuration.GetSection(HoneyGuardOptions.SectionName));

        // AddSingleton: exactly one instance for the whole application's lifetime, shared
        // by every request. Both types hold state (the ban list, the pending-incident
        // queue) that must persist and be visible across requests, so "scoped" (one
        // instance per request) or "transient" (a new instance every time it's asked for)
        // would silently break the whole point of these classes.
        services.AddSingleton<BanRegistry>();
        services.AddSingleton<IncidentQueue>();

        // A typed HttpClient: ASP.NET Core creates and owns the HttpClient used inside
        // IncidentDispatcher via IHttpClientFactory under the hood, which solves a classic
        // .NET pitfall where manually `new`-ing up HttpClient instances (or not disposing
        // them) exhausts sockets or serves stale DNS results over time.
        services.AddHttpClient<IncidentDispatcher>();

        // AddHostedService registers IncidentDispatcher as a long-running background
        // task that the host starts on application startup and stops on shutdown - see
        // the BackgroundService base class it derives from for how that works.
        services.AddHostedService<IncidentDispatcher>();

        return services;
    }

    /// <summary>
    /// Inserts <see cref="HoneyGuardMiddleware"/> into the request pipeline. This is an
    /// extension method on <c>IApplicationBuilder</c> for the same reason
    /// <see cref="AddHoneyGuard"/> extends <c>IServiceCollection</c>: it reads at the call
    /// site in Program.cs as `app.UseHoneyGuard()`, matching the built-in
    /// `app.UseHttpsRedirection()` / `app.UseStaticFiles()` style calls around it.
    ///
    /// Where you call this matters. Middleware runs in the exact order it is added, so
    /// calling this before `app.UseStaticFiles()` and endpoint mapping means every
    /// request - traps included - is inspected before any real handler or file server
    /// gets a chance to run.
    /// </summary>
    public static IApplicationBuilder UseHoneyGuard(this IApplicationBuilder app)
    {
        return app.UseMiddleware<HoneyGuardMiddleware>();
    }
}
