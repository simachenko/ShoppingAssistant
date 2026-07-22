var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var catalogDb = postgres.AddDatabase("catalogdb");
var pricingDb = postgres.AddDatabase("pricingdb");
var advisorDb = postgres.AddDatabase("advisordb");

var catalogApi = builder.AddProject<Projects.ProductCatalog_Api>("catalog-api")
    .WithReference(catalogDb)
    .WaitFor(catalogDb);

var pricingApi = builder.AddProject<Projects.PricingAvailability_Api>("pricing-api")
    .WithReference(pricingDb)
    .WaitFor(pricingDb);

var advisorApi = builder.AddProject<Projects.ProductAdvisor_Api>("advisor-api")
    .WithReference(advisorDb)
    .WaitFor(advisorDb)
    .WithReference(catalogApi)
    .WithReference(pricingApi);

var gatewayApi = builder.AddProject<Projects.Gateway_Api>("gateway-api")
    .WithReference(catalogApi)
    .WithReference(pricingApi)
    .WithReference(advisorApi);

builder.AddProject<Projects.WebApp_Blazor>("webapp")
    .WithReference(gatewayApi)
    .WithExternalHttpEndpoints();

builder.Build().Run();
