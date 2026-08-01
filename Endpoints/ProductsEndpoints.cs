namespace HoneyGuard.Endpoints;

/// <summary>
/// The application's real, legitimate API - what an attacker's initial "normal" request
/// hits before they start probing for weaknesses. This is a "minimal API": instead of a
/// Controller class with [HttpGet] attributes (the older ASP.NET Core MVC style), routes
/// are just registered as small delegates directly against the app. For a handful of
/// simple JSON endpoints like these, minimal APIs mean less ceremony - no separate
/// controller class, attribute routing, or action-result wrapping types to learn first.
/// </summary>
public static class ProductsEndpoints
{
    private static readonly Product[] SampleCatalog =
    [
        new Product(1, "Mechanical Keyboard", 129.99m),
        new Product(2, "Ultrawide Monitor", 449.00m),
        new Product(3, "USB-C Dock", 89.50m),
    ];

    /// <summary>
    /// Registers the product routes on the app. Called once from Program.cs. Grouping
    /// related routes behind a static "MapXyz" method (rather than inlining every
    /// `app.MapGet(...)` call directly in Program.cs) is a common minimal-API convention
    /// for keeping Program.cs readable as an application grows past a couple of routes.
    /// </summary>
    public static void MapProductsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/products", () => SampleCatalog)
            .WithName("GetProducts");

        app.MapGet("/api/v1/products/{id:int}", (int id) =>
        {
            Product? product = Array.Find(SampleCatalog, p => p.Id == id);
            return product is not null ? Results.Ok(product) : Results.NotFound();
        })
        .WithName("GetProductById");
    }
}

/// <summary>
/// A plain data-transfer record describing one catalog item returned by the API.
/// </summary>
public sealed record Product(int Id, string Name, decimal Price);
