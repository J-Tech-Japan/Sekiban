# Build stage
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG TARGETARCH
WORKDIR /source

# Copy only project files first for better layer caching
COPY src/Sekiban.Dcb.Core/*.csproj src/Sekiban.Dcb.Core/
COPY src/Sekiban.Dcb.Core.Model/*.csproj src/Sekiban.Dcb.Core.Model/

COPY internalUsages/DcbOrleans.ServiceDefaults/*.csproj internalUsages/DcbOrleans.ServiceDefaults/
COPY internalUsages/Dcb.Domain/*.csproj internalUsages/Dcb.Domain/
COPY internalUsages/DcbOrleans.Web/*.csproj internalUsages/DcbOrleans.Web/

# Restore dependencies (target net9.0 only for Docker builds)
# Use TargetFrameworks=net9.0 to override multi-targeting and avoid net10.0 SDK requirement
RUN dotnet restore internalUsages/DcbOrleans.Web/DcbOrleans.Web.csproj \
    -a $TARGETARCH \
    /p:TargetFramework=net9.0 \
    /p:TargetFrameworks=net9.0

# Copy all source code
COPY src/ src/
COPY internalUsages/DcbOrleans.ServiceDefaults/ internalUsages/DcbOrleans.ServiceDefaults/
COPY internalUsages/Dcb.Domain/ internalUsages/Dcb.Domain/
COPY internalUsages/DcbOrleans.Web/ internalUsages/DcbOrleans.Web/

# Build and publish
# Use TargetFrameworks=net9.0 to override multi-targeting and avoid net10.0 SDK requirement
RUN dotnet publish internalUsages/DcbOrleans.Web/DcbOrleans.Web.csproj \
    -c Release \
    -a $TARGETARCH \
    -f net9.0 \
    /p:TargetFrameworks=net9.0 \
    -o /app

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

# Copy published app
COPY --from=build /app .

# Configure ports
EXPOSE 8081

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8081/health || exit 1

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8081
ENV ASPNETCORE_ENVIRONMENT=Production

# Run as non-root user
USER $APP_UID

ENTRYPOINT ["dotnet", "DcbOrleans.Web.dll"]
