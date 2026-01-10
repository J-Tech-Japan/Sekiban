FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["SekibanDcbOrleans.ApiService/SekibanDcbOrleans.ApiService.csproj", "SekibanDcbOrleans.ApiService/"]
COPY ["SekibanDcbOrleans.EventSource/SekibanDcbOrleans.EventSource.csproj", "SekibanDcbOrleans.EventSource/"]
COPY ["SekibanDcbOrleans.Interactions/SekibanDcbOrleans.Interactions.csproj", "SekibanDcbOrleans.Interactions/"]
COPY ["SekibanDcbOrleans.ImmutableModels/SekibanDcbOrleans.ImmutableModels.csproj", "SekibanDcbOrleans.ImmutableModels/"]
COPY ["SekibanDcbOrleans.MeetingRoomModels/SekibanDcbOrleans.MeetingRoomModels.csproj", "SekibanDcbOrleans.MeetingRoomModels/"]
COPY ["SekibanDcbOrleans.ServiceDefaults/SekibanDcbOrleans.ServiceDefaults.csproj", "SekibanDcbOrleans.ServiceDefaults/"]

RUN dotnet restore "SekibanDcbOrleans.ApiService/SekibanDcbOrleans.ApiService.csproj"

COPY . .
WORKDIR /src/SekibanDcbOrleans.ApiService

RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "SekibanDcbOrleans.ApiService.dll"]
