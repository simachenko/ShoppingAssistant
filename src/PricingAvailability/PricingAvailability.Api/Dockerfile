FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["global.json", "Directory.Build.props", "./"]
COPY ["src/", "src/"]
RUN dotnet restore "src/PricingAvailability/PricingAvailability.Api/PricingAvailability.Api.csproj"
RUN dotnet build "src/PricingAvailability/PricingAvailability.Api/PricingAvailability.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "src/PricingAvailability/PricingAvailability.Api/PricingAvailability.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PricingAvailability.Api.dll"]
