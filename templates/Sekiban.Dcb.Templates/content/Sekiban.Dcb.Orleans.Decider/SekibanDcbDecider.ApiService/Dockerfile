FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

COPY ["SekibanDcbDecider.ApiService/SekibanDcbDecider.ApiService.csproj", "SekibanDcbDecider.ApiService/"]
COPY ["SekibanDcbDecider.EventSource/SekibanDcbDecider.EventSource.csproj", "SekibanDcbDecider.EventSource/"]
COPY ["SekibanDcbDecider.Interactions/SekibanDcbDecider.Interactions.csproj", "SekibanDcbDecider.Interactions/"]
COPY ["SekibanDcbDecider.ImmutableModels/SekibanDcbDecider.ImmutableModels.csproj", "SekibanDcbDecider.ImmutableModels/"]
COPY ["SekibanDcbDecider.MeetingRoomModels/SekibanDcbDecider.MeetingRoomModels.csproj", "SekibanDcbDecider.MeetingRoomModels/"]
COPY ["SekibanDcbDecider.ServiceDefaults/SekibanDcbDecider.ServiceDefaults.csproj", "SekibanDcbDecider.ServiceDefaults/"]

RUN dotnet restore "SekibanDcbDecider.ApiService/SekibanDcbDecider.ApiService.csproj"

COPY . .
WORKDIR /src/SekibanDcbDecider.ApiService

RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "SekibanDcbDecider.ApiService.dll"]
