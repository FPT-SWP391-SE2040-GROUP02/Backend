# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy CPM props and global.json for caching restore layer
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/LegacyVault.Domain/LegacyVault.Domain.csproj src/LegacyVault.Domain/
COPY src/LegacyVault.Application/LegacyVault.Application.csproj src/LegacyVault.Application/
COPY src/LegacyVault.Infrastructure/LegacyVault.Infrastructure.csproj src/LegacyVault.Infrastructure/
COPY src/LegacyVault.Api/LegacyVault.Api.csproj src/LegacyVault.Api/

# Restore dependencies for Api
RUN dotnet restore src/LegacyVault.Api/LegacyVault.Api.csproj

# Copy source code
COPY src/ src/

# Build and publish to /app/publish
RUN dotnet publish src/LegacyVault.Api/LegacyVault.Api.csproj -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create directory for local encrypted asset storage if local provider is used
RUN mkdir -p /app/App_Data/asset-content

# Copy compiled binaries from build stage
COPY --from=build /app/publish .

# Environment defaults for container
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=8080
ENV AllowedHosts="*"

EXPOSE 8080

ENTRYPOINT ["dotnet", "LegacyVault.Api.dll"]
