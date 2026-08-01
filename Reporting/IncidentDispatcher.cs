using System.Net.Http.Json;
using HoneyGuard.Configuration;
using Microsoft.Extensions.Options;

namespace HoneyGuard.Reporting;

/// <summary>
/// A long-running background task that drains <see cref="IncidentQueue"/> and forwards
/// each incident to Supabase, so that talking to the network never happens on the
/// request-handling thread that a real (or fake) HTTP request is waiting on.
///
/// Deriving from <see cref="BackgroundService"/> is the standard .NET way to run
/// long-lived work alongside your web application. It implements <c>IHostedService</c>
/// for you and just asks you to fill in <see cref="ExecuteAsync"/> with a loop; the host
/// takes care of starting that loop on application startup and requesting cancellation
/// on shutdown (via the <see cref="CancellationToken"/> it passes in). It is registered
/// with <c>services.AddHostedService&lt;IncidentDispatcher&gt;()</c> in
/// <see cref="Security.HoneyGuardServiceCollectionExtensions.AddHoneyGuard"/>.
/// </summary>
public sealed class IncidentDispatcher(
    HttpClient httpClient,
    IncidentQueue incidentQueue,
    IOptions<HoneyGuardOptions> options,
    ILogger<IncidentDispatcher> logger) : BackgroundService
{
    /// <summary>
    /// The main loop of the background service. <c>ReadAllAsync</c> asynchronously waits
    /// for the next incident to arrive on the channel - it does not spin or poll, the
    /// thread is released back to the pool while there is nothing to send - which is why
    /// this can sit idle for the whole lifetime of the app costing essentially nothing.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (Incident incident in incidentQueue.Reader.ReadAllAsync(stoppingToken))
        {
            await SendIncidentAsync(incident, stoppingToken);
        }
    }

    /// <summary>
    /// POSTs a single incident to Supabase's auto-generated REST API (PostgREST) for the
    /// `incidents` table. Any failure is caught and logged here rather than allowed to
    /// escape: an unhandled exception inside <see cref="ExecuteAsync"/> would crash the
    /// background service entirely, silently ending incident reporting for the rest of
    /// the app's lifetime.
    /// </summary>
    private async Task SendIncidentAsync(Incident incident, CancellationToken cancellationToken)
    {
        HoneyGuardOptions settings = options.Value;

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, $"{settings.SupabaseUrl}/rest/v1/incidents");

            // Supabase's REST layer (PostgREST) expects both of these headers: "apikey"
            // identifies which Supabase project we're calling, and the Bearer token
            // determines which Postgres role we act as. Using the service_role key here
            // is what lets this write succeed even though the table's Row Level Security
            // policy only grants read access to anonymous callers (see the dashboard,
            // which uses the anon key and can only SELECT).
            request.Headers.Add("apikey", settings.SupabaseServiceRoleKey);
            request.Headers.Add("Authorization", $"Bearer {settings.SupabaseServiceRoleKey}");

            // Without this, PostgREST replies with the row it just inserted, which we
            // have no use for - asking it to return nothing saves a bit of bandwidth.
            request.Headers.Add("Prefer", "return=minimal");

            request.Content = JsonContent.Create(new
            {
                ip_address = incident.IpAddress,
                path = incident.Path,
                method = incident.Method,
                user_agent = incident.UserAgent,
                trap_name = incident.TrapName,
                created_at = incident.OccurredAtUtc,
            });

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "Supabase rejected incident for {IpAddress}: {StatusCode} {Body}",
                    incident.IpAddress,
                    response.StatusCode,
                    body);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A dropped incident is far better than a crashed background service - the
            // in-memory ban still took effect instantly regardless of whether this
            // network call succeeds, so a Supabase outage never weakens the actual
            // defense, only the dashboard's visibility into it.
            logger.LogError(exception, "Failed to report incident for {IpAddress} to Supabase", incident.IpAddress);
        }
    }
}
