FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["SekibanDcbOrleansAws.Web/SekibanDcbOrleansAws.Web.csproj", "SekibanDcbOrleansAws.Web/"]
COPY ["SekibanDcbOrleansAws.Domain/SekibanDcbOrleansAws.Domain.csproj", "SekibanDcbOrleansAws.Domain/"]
COPY ["SekibanDcbOrleansAws.ServiceDefaults/SekibanDcbOrleansAws.ServiceDefaults.csproj", "SekibanDcbOrleansAws.ServiceDefaults/"]

RUN dotnet restore "SekibanDcbOrleansAws.Web/SekibanDcbOrleansAws.Web.csproj"

COPY . .
WORKDIR /src/SekibanDcbOrleansAws.Web
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8081 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
COPY --from=build /app/publish .
EXPOSE 8081
ENTRYPOINT ["dotnet", "SekibanDcbOrleansAws.Web.dll"]
