FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /src

COPY *.sln ./
COPY DaprSekiban.ApiService/*.csproj ./DaprSekiban.ApiService/
COPY DaprSekiban.Domain/*.csproj ./DaprSekiban.Domain/
COPY DaprSekiban.ServiceDefaults/*.csproj ./DaprSekiban.ServiceDefaults/
COPY DaprSekiban.AppHost/*.csproj ./DaprSekiban.AppHost/
COPY DaprSekiban.Web/*.csproj ./DaprSekiban.Web/
COPY DaprSekiban.Unit/*.csproj ./DaprSekiban.Unit/
COPY DaprSekiban.EventRelay/*.csproj ./DaprSekiban.EventRelay/

RUN dotnet restore

COPY . .

# Change to the project directory before publish
WORKDIR /src/DaprSekiban.EventRelay

# Publish to default location first
RUN dotnet publish -c Release \
    /p:CopyLocalLockFileAssemblies=true \
    /p:PreserveCompilationContext=true \
    /p:PublishTrimmed=false \
    /p:IsTransformWebConfigDisabled=true

# Copy from the default publish location
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

WORKDIR /app
COPY --from=build /src/DaprSekiban.EventRelay/bin/Release/net9.0/publish .

# Expose the port that Dapr will use to communicate with the app
EXPOSE 5020

# Set ASP.NET Core to listen on port 5020 on all interfaces (0.0.0.0)
ENV ASPNETCORE_URLS=http://0.0.0.0:5020

ENTRYPOINT ["dotnet", "DaprSekiban.EventRelay.dll"]