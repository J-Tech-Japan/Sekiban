FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["SekibanDcbDeciderAws.ApiService/SekibanDcbDeciderAws.ApiService.csproj", "SekibanDcbDeciderAws.ApiService/"]
COPY ["SekibanDcbDeciderAws.EventSource/SekibanDcbDeciderAws.EventSource.csproj", "SekibanDcbDeciderAws.EventSource/"]
COPY ["SekibanDcbDeciderAws.Interactions/SekibanDcbDeciderAws.Interactions.csproj", "SekibanDcbDeciderAws.Interactions/"]
COPY ["SekibanDcbDeciderAws.ImmutableModels/SekibanDcbDeciderAws.ImmutableModels.csproj", "SekibanDcbDeciderAws.ImmutableModels/"]
COPY ["SekibanDcbDeciderAws.MeetingRoomModels/SekibanDcbDeciderAws.MeetingRoomModels.csproj", "SekibanDcbDeciderAws.MeetingRoomModels/"]
COPY ["SekibanDcbDeciderAws.ServiceDefaults/SekibanDcbDeciderAws.ServiceDefaults.csproj", "SekibanDcbDeciderAws.ServiceDefaults/"]

RUN dotnet restore "SekibanDcbDeciderAws.ApiService/SekibanDcbDeciderAws.ApiService.csproj"

COPY . .
WORKDIR /src/SekibanDcbDeciderAws.ApiService

RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

# Install curl for health checks
RUN apk add --no-cache curl

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .

EXPOSE 8080
EXPOSE 11111
EXPOSE 30000

ENTRYPOINT ["dotnet", "SekibanDcbDeciderAws.ApiService.dll"]
