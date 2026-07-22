using Microsoft.EntityFrameworkCore;
using PricingAvailability.Application;
using PricingAvailability.Application.Abstractions;
using PricingAvailability.Infrastructure;
using PricingAvailability.Infrastructure.Repositories;
using PricingAvailability.Infrastructure.SeedData;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<PricingDbContext>("pricingdb");

builder.Services.AddScoped<IOfferRepository, OfferRepository>();
builder.Services.AddScoped<PricingService>();

var app = builder.Build();

app.UseCorrelationId();
app.MapDefaultEndpoints();

// Demo/local-dev convenience: apply migrations and seed a fixed dataset if empty. Guarded by
// config so this never runs unintentionally against a real environment (e.g., Render/Neon).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
    await db.Database.MigrateAsync();

    if (app.Configuration.GetValue<bool>("SeedDemoData"))
    {
        // Per-row idempotent (not just "table is empty"), so the demo dataset can grow over time
        // without wiping/duplicating whatever a previous seed run already inserted.
        var existingOfferIds = (await db.Offers.Select(o => o.OfferId).ToListAsync()).ToHashSet();
        db.Offers.AddRange(DemoSeedData.Offers.Where(o => !existingOfferIds.Contains(o.OfferId)));
        await db.SaveChangesAsync();
    }
}

// GET /api/pricing/offers/{productId} — single lookup (contracts/pricing-api.md)
app.MapGet("/api/pricing/offers/{productId:guid}", async (Guid productId, PricingService service, CancellationToken ct) =>
{
    var offer = await service.GetOfferAsync(productId, ct);
    return offer is null ? Results.NotFound() : Results.Ok(offer);
});

// GET /api/pricing/offers?productIds=id1,id2,... — batch lookup for candidate sets
app.MapGet("/api/pricing/offers", async (string productIds, PricingService service, CancellationToken ct) =>
{
    var ids = productIds
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(Guid.Parse)
        .ToArray();

    if (ids.Length == 0)
    {
        return Results.BadRequest("At least one productId is required.");
    }

    if (ids.Length > PricingService.MaxBatchSize)
    {
        return Results.BadRequest($"At most {PricingService.MaxBatchSize} productIds are allowed per call.");
    }

    var result = await service.GetOffersAsync(ids, ct);
    return Results.Ok(result);
});

app.Run();

public partial class Program;
