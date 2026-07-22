using ProductCatalog.Application;
using ProductCatalog.Application.Abstractions;
using ProductCatalog.Infrastructure;
using ProductCatalog.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<CatalogDbContext>("catalogdb");

builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<ProductCatalogService>();

var app = builder.Build();

app.UseCorrelationId();
app.MapDefaultEndpoints();

// GET /api/catalog/products?category=&q=&page=&pageSize= — search (contracts/catalog-api.md)
app.MapGet("/api/catalog/products", async (
    string? category, string? q, int page, int pageSize, ProductCatalogService service, CancellationToken ct) =>
{
    page = page <= 0 ? 1 : page;
    pageSize = pageSize <= 0 ? ProductCatalogService.DefaultPageSize : pageSize;

    if (pageSize > ProductCatalogService.MaxPageSize)
    {
        return Results.BadRequest($"pageSize must not exceed {ProductCatalogService.MaxPageSize}.");
    }

    var result = await service.SearchProductsAsync(category, q, page, pageSize, ct);
    return Results.Ok(result);
});

app.Run();

public partial class Program;
