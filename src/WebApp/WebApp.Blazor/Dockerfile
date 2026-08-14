FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
# Containers never edit appsettings on disk, so config reload-on-change is pure waste — and each
# watcher consumes an inotify instance from the kernel's per-user limit (128 by default), which is
# shared across every container on the host. With enough services running, CreateBuilder itself
# crashes with "configured user limit (128) on the number of inotify instances has been reached".
ENV DOTNET_hostBuilder__reloadConfigOnChange=false \
    DOTNET_USE_POLLING_FILE_WATCHER=true

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["global.json", "Directory.Build.props", "./"]
COPY ["src/", "src/"]
RUN dotnet restore "src/WebApp/WebApp.Blazor/WebApp.Blazor.csproj"
RUN dotnet build "src/WebApp/WebApp.Blazor/WebApp.Blazor.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "src/WebApp/WebApp.Blazor/WebApp.Blazor.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "WebApp.Blazor.dll"]
