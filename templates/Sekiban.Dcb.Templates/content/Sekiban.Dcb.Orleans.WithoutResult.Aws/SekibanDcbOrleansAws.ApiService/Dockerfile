FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["SekibanDcbOrleansAws.ApiService/SekibanDcbOrleansAws.ApiService.csproj", "SekibanDcbOrleansAws.ApiService/"]
COPY ["SekibanDcbOrleansAws.Domain/SekibanDcbOrleansAws.Domain.csproj", "SekibanDcbOrleansAws.Domain/"]
COPY ["SekibanDcbOrleansAws.ServiceDefaults/SekibanDcbOrleansAws.ServiceDefaults.csproj", "SekibanDcbOrleansAws.ServiceDefaults/"]

RUN dotnet restore "SekibanDcbOrleansAws.ApiService/SekibanDcbOrleansAws.ApiService.csproj"

COPY . .
WORKDIR /src/SekibanDcbOrleansAws.ApiService

RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "SekibanDcbOrleansAws.ApiService.dll"]
