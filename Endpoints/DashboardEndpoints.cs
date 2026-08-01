using HoneyGuard.Configuration;
using HoneyGuard.Security;
using Microsoft.Extensions.Options;

namespace HoneyGuard.Endpoints;

/// <summary>
/// Small support endpoints for the static dashboard page in wwwroot/index.html - it is
/// plain HTML/JS with no build step, so it cannot read appsettings.json or user-secrets
/// directly. These routes hand it just enough information to connect to Supabase itself.
/// </summary>
public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        // The dashboard calls this once on page load to learn which Supabase project to
        // connect to. Only the publishable anon key is exposed here - never the
        // service_role key - because this response is visible to anyone who loads the
        // page. That is safe specifically because the anon key's own database
        // permissions are already locked down by the Row Level Security policy on the
        // incidents table (read-only, no insert/update/delete).
        app.MapGet("/api/dashboard/config", (IOptions<HoneyGuardOptions> options) =>
        {
            HoneyGuardOptions settings = options.Value;
            return Results.Ok(new
            {
                supabaseUrl = settings.SupabaseUrl,
                supabaseAnonKey = settings.SupabaseAnonKey,
            });
        })
        .WithName("GetDashboardConfig");

        // Lets the "Reset Demo" button on the dashboard clear every in-memory ban so the
        // three-step attack story (200 -> 404 -> 403) can be replayed without restarting
        // the whole application. Restricted to Development so this can never be used to
        // un-ban a real attacker in production.
        app.MapPost("/api/dashboard/reset", (BanRegistry banRegistry, IWebHostEnvironment environment) =>
        {
            if (!environment.IsDevelopment())
            {
                return Results.NotFound();
            }

            banRegistry.Clear();
            return Results.Ok(new { message = "All bans cleared." });
        })
        .WithName("ResetDemo");
    }
}
