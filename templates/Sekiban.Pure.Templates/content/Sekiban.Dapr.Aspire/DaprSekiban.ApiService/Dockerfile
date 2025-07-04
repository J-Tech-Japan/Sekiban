FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /src

COPY *.sln ./
COPY DaprSekiban.ApiService/*.csproj ./DaprSekiban.ApiService/
COPY DaprSekiban.Domain/*.csproj ./DaprSekiban.Domain/
COPY DaprSekiban.ServiceDefaults/*.csproj ./DaprSekiban.ServiceDefaults/
COPY DaprSekiban.AppHost/*.csproj ./DaprSekiban.AppHost/
COPY DaprSekiban.Web/*.csproj ./DaprSekiban.Web/
COPY DaprSekiban.Unit/*.csproj ./DaprSekiban.Unit/

RUN dotnet restore

COPY . .

RUN dotnet publish DaprSekiban.ApiService/DaprSekiban.ApiService.csproj -c Release -o /app/publish \
    /p:CopyLocalLockFileAssemblies=true \
    /p:PreserveCompilationContext=true \
    /p:PublishTrimmed=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

WORKDIR /app
COPY --from=build /app/publish .

# Expose the port that Dapr will use to communicate with the app
EXPOSE 8080

# Set ASP.NET Core to listen on port 8080 on all interfaces (0.0.0.0)
ENV ASPNETCORE_URLS=http://0.0.0.0:8080

ENTRYPOINT ["dotnet", "DaprSekiban.ApiService.dll"]