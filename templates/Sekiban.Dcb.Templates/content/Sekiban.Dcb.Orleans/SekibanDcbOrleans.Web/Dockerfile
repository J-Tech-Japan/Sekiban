FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["SekibanDcbOrleans.Web/SekibanDcbOrleans.Web.csproj", "SekibanDcbOrleans.Web/"]
COPY ["SekibanDcbOrleans.Domain/SekibanDcbOrleans.Domain.csproj", "SekibanDcbOrleans.Domain/"]
COPY ["SekibanDcbOrleans.ServiceDefaults/SekibanDcbOrleans.ServiceDefaults.csproj", "SekibanDcbOrleans.ServiceDefaults/"]

RUN dotnet restore "SekibanDcbOrleans.Web/SekibanDcbOrleans.Web.csproj"

COPY . .
WORKDIR /src/SekibanDcbOrleans.Web
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "SekibanDcbOrleans.Web.dll"]
