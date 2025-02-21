

## pack

dotnet pack OrleansSekibanTemplate.csproj -c Release --output ./nupkg_output   

## install locally

dotnet new install ./nupkg_output/OrleansSekiban.Template.1.0.0.nupkg   

## remove if already installed
dotnet new uninstall OrleansSekiban.Template  

## local test
dotnet new install ./nupkg_output/OrleansSekiban.Template.1.0.0.nupkg 

## release nuget
dotnet nuget push ./nupkg_output/OrleansSekiban.Template.1.0.0.nupkg --api-key <YOURKEY> --source https://api.nuget.org/v3/index.json

## how to install from nuget.
dotnet new install OrleansSekiban.Template

## how to make project
dotnet new sekiban-orleans -n YourProjectName