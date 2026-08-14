using Xunit;

namespace ProductCatalog.Api.Tests;

/// <summary>
/// Puts every Catalog contract-test class in one xUnit collection, sharing a single
/// <see cref="CatalogApiTestFixture"/>.
/// </summary>
/// <remarks>
/// This is a correctness fix, not a tidy-up. With <c>IClassFixture</c> each class got its own
/// fixture and xUnit ran the classes in parallel, so several fixtures raced to write the same
/// <b>process-global</b> environment variable (<c>ConnectionStrings__catalogdb</c>, which
/// <see cref="CatalogApiFactory"/> must set because Program.cs reads it eagerly). Whichever write
/// landed last won for every host built afterwards, so a test could run against another class's
/// container — one not seeded yet, or seeded twice. That produced pass rates wandering between
/// 3/21 and 21/21 on unchanged code, and duplicate-key failures during seeding.
/// <para>
/// A collection also serializes these classes and starts one container instead of five, which is
/// substantially faster.
/// </para>
/// </remarks>
[CollectionDefinition(Name)]
public sealed class CatalogApiCollection : ICollectionFixture<CatalogApiTestFixture>
{
    public const string Name = "catalog-api";
}
