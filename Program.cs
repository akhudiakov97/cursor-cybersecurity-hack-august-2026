using HoneyGuard.Endpoints;
using HoneyGuard.Security;

// WebApplicationBuilder is where every service the app needs gets registered before the
// app actually starts handling requests. Think of it as the "configuration phase".
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Registers HoneyGuard's own services (the ban registry, the incident queue, and its
// background dispatcher). See HoneyGuardServiceCollectionExtensions.AddHoneyGuard for
// exactly what this adds and why each piece uses the DI lifetime that it does.
builder.Services.AddHoneyGuard(builder.Configuration);

// Building the app switches from "configuration phase" to "the app now exists" - after
// this line, you assemble the request pipeline instead of registering more services.
WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Pipeline order matters: middleware runs in exactly the order it is added here, wrapped
// around whatever comes after it. HoneyGuard goes first so that a banned IP or a
// honeypot trap gets handled before the request ever reaches static files or a real
// endpoint further down this list.
app.UseHoneyGuard();

// Serves wwwroot/index.html (the dashboard) as the default document, plus any other
// static assets placed in wwwroot/.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapProductsEndpoints();
app.MapDashboardEndpoints();

app.Run();
