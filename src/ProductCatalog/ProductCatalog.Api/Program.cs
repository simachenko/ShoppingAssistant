using Microsoft.EntityFrameworkCore;
using ProductCatalog.Application;
using ProductCatalog.Application.Abstractions;
using ProductCatalog.Infrastructure;
using ProductCatalog.Infrastructure.Repositories;
using ProductCatalog.Infrastructure.SeedData;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<CatalogDbContext>("catalogdb");

builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<ProductCatalogService>();

var app = builder.Build();

app.UseCorrelationId();
app.MapDefaultEndpoints();

// Demo/local-dev convenience: apply migrations and seed a fixed dataset if empty. Guarded by
// config so this never runs unintentionally against a real environment (e.g., Render/Neon).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await db.Database.MigrateAsync();

    if (app.Configuration.GetValue<bool>("SeedDemoData"))
    {
        // Per-row idempotent (not just "table is empty"), so the demo dataset can grow over time
        // without wiping/duplicating whatever a previous seed run already inserted.
        var existingBrandIds = (await db.Brands.Select(b => b.BrandId).ToListAsync()).ToHashSet();
        var existingCategoryIds = (await db.Categories.Select(c => c.CategoryId).ToListAsync()).ToHashSet();
        var existingProductIds = (await db.Products.Select(p => p.ProductId).ToListAsync()).ToHashSet();

        db.Brands.AddRange(DemoSeedData.Brands.Where(b => !existingBrandIds.Contains(b.BrandId)));
        db.Categories.AddRange(DemoSeedData.Categories.Where(c => !existingCategoryIds.Contains(c.CategoryId)));
        db.Products.AddRange(DemoSeedData.Products.Where(p => !existingProductIds.Contains(p.ProductId)));
        await db.SaveChangesAsync();
    }
}

// GET /api/catalog/products?category=&q=&page=&pageSize= — search (contracts/catalog-api.md)
app.MapGet("/api/catalog/products", async (
    string? category, string? q, ProductCatalogService service, CancellationToken ct,
    int page = 1, int pageSize = ProductCatalogService.DefaultPageSize) =>
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

// GET /api/catalog/products/{productId} — single product detail (contracts/catalog-api.md)
app.MapGet("/api/catalog/products/{productId:guid}", async (
    Guid productId, ProductCatalogService service, CancellationToken ct) =>
{
    var product = await service.GetProductDetailAsync(productId, ct);
    return product is null ? Results.NotFound() : Results.Ok(product);
});

// GET /api/catalog/categories/{categoryId} — category (incl. ComparableAttributeKeys) (contracts/catalog-api.md)
app.MapGet("/api/catalog/categories/{categoryId:guid}", async (
    Guid categoryId, ProductCatalogService service, CancellationToken ct) =>
{
    var category = await service.GetCategoryAsync(categoryId, ct);
    return category is null ? Results.NotFound() : Results.Ok(category);
});

app.Run();

public partial class Program;
