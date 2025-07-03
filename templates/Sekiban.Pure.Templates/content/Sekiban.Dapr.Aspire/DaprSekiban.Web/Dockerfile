FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy only the project files needed for the Web project
COPY DaprSekiban.Web/*.csproj ./DaprSekiban.Web/
COPY DaprSekiban.Domain/*.csproj ./DaprSekiban.Domain/
COPY DaprSekiban.ServiceDefaults/*.csproj ./DaprSekiban.ServiceDefaults/

# Restore only the Web project and its dependencies
RUN dotnet restore DaprSekiban.Web/DaprSekiban.Web.csproj

COPY . .
RUN dotnet publish DaprSekiban.Web/DaprSekiban.Web.csproj -c Release -o /app/publish --self-contained false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

# Expose the port for the web app
EXPOSE 8080

ENTRYPOINT ["dotnet", "DaprSekiban.Web.dll"]