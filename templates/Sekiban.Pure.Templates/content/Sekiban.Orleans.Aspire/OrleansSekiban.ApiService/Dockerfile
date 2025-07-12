FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /src

COPY *.sln ./
COPY MyProject.ApiService/*.csproj ./MyProject.ApiService/
COPY MyProject.Domain/*.csproj ./MyProject.Domain/
COPY MyProject.ServiceDefaults/*.csproj ./MyProject.ServiceDefaults/
COPY MyProject.AppHost/*.csproj ./MyProject.AppHost/
COPY MyProject.Web/*.csproj ./MyProject.Web/
COPY MyProject.Unit/*.csproj ./MyProject.Unit/
COPY MyProject.Playwright/*.csproj ./MyProject.Playwright/

RUN dotnet restore

COPY . .

RUN dotnet publish MyProject.ApiService/MyProject.ApiService.csproj -c Release -o /app/publish \
    /p:CopyLocalLockFileAssemblies=true \
    /p:PreserveCompilationContext=true \
    /p:PublishTrimmed=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MyProject.ApiService.dll"]
