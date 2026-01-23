# Build stage
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG TARGETARCH
WORKDIR /source

# Copy only project files first for better layer caching
COPY src/Sekiban.Dcb.Core/*.csproj src/Sekiban.Dcb.Core/
COPY src/Sekiban.Dcb.Core.Model/*.csproj src/Sekiban.Dcb.Core.Model/
COPY src/Sekiban.Dcb.WithoutResult/*.csproj src/Sekiban.Dcb.WithoutResult/
COPY src/Sekiban.Dcb.Orleans.Core/*.csproj src/Sekiban.Dcb.Orleans.Core/
COPY src/Sekiban.Dcb.Orleans.WithoutResult/*.csproj src/Sekiban.Dcb.Orleans.WithoutResult/
COPY src/Sekiban.Dcb.DynamoDB/*.csproj src/Sekiban.Dcb.DynamoDB/
COPY src/Sekiban.Dcb.BlobStorage.S3/*.csproj src/Sekiban.Dcb.BlobStorage.S3/

COPY internalUsages/DcbOrleans.ServiceDefaults/*.csproj internalUsages/DcbOrleans.ServiceDefaults/
COPY internalUsages/Dcb.Domain.WithoutResult/*.csproj internalUsages/Dcb.Domain.WithoutResult/
COPY internalUsages/DcbOrleansDynamoDB.WithoutResult.ApiService/*.csproj internalUsages/DcbOrleansDynamoDB.WithoutResult.ApiService/

# Restore dependencies (target net9.0 only for Docker builds)
RUN dotnet restore internalUsages/DcbOrleansDynamoDB.WithoutResult.ApiService/DcbOrleansDynamoDB.WithoutResult.ApiService.csproj \
    -a $TARGETARCH \
    /p:TargetFramework=net9.0

# Copy all source code
COPY src/ src/
COPY internalUsages/DcbOrleans.ServiceDefaults/ internalUsages/DcbOrleans.ServiceDefaults/
COPY internalUsages/Dcb.Domain.WithoutResult/ internalUsages/Dcb.Domain.WithoutResult/
COPY internalUsages/DcbOrleansDynamoDB.WithoutResult.ApiService/ internalUsages/DcbOrleansDynamoDB.WithoutResult.ApiService/

# Build and publish
RUN dotnet publish internalUsages/DcbOrleansDynamoDB.WithoutResult.ApiService/DcbOrleansDynamoDB.WithoutResult.ApiService.csproj \
    -c Release \
    -a $TARGETARCH \
    -f net9.0 \
    -o /app

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

# Copy published app
COPY --from=build /app .

# Configure ports
# HTTP API (external via ALB)
EXPOSE 8080
# Orleans Silo-to-Silo (internal only)
EXPOSE 11111
# Orleans Gateway (internal only)
EXPOSE 30000

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:8080/api/health || exit 1

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_gcServer=1
ENV DOTNET_GCHeapHardLimit=0x40000000

# Run as non-root user
USER $APP_UID

ENTRYPOINT ["dotnet", "DcbOrleansDynamoDB.WithoutResult.ApiService.dll"]
