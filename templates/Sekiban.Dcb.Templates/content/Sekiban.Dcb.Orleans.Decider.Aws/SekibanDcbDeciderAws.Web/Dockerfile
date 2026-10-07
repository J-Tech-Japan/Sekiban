FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["SekibanDcbTemplateVersion.props", "./"]

COPY ["SekibanDcbDeciderAws.Web/SekibanDcbDeciderAws.Web.csproj", "SekibanDcbDeciderAws.Web/"]
COPY ["SekibanDcbDeciderAws.EventSource/SekibanDcbDeciderAws.EventSource.csproj", "SekibanDcbDeciderAws.EventSource/"]
COPY ["SekibanDcbDeciderAws.Interactions/SekibanDcbDeciderAws.Interactions.csproj", "SekibanDcbDeciderAws.Interactions/"]
COPY ["SekibanDcbDeciderAws.ImmutableModels/SekibanDcbDeciderAws.ImmutableModels.csproj", "SekibanDcbDeciderAws.ImmutableModels/"]
COPY ["SekibanDcbDeciderAws.MeetingRoomModels/SekibanDcbDeciderAws.MeetingRoomModels.csproj", "SekibanDcbDeciderAws.MeetingRoomModels/"]
COPY ["SekibanDcbDeciderAws.ServiceDefaults/SekibanDcbDeciderAws.ServiceDefaults.csproj", "SekibanDcbDeciderAws.ServiceDefaults/"]

RUN dotnet restore "SekibanDcbDeciderAws.Web/SekibanDcbDeciderAws.Web.csproj"

COPY . .
WORKDIR /src/SekibanDcbDeciderAws.Web
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
COPY --from=build /app/publish .
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "SekibanDcbDeciderAws.Web.dll"]
