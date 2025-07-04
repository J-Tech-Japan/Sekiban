@description('The name of the ACR.')
param acrNameBase string = 'acr-${resourceGroup().name}'
var acrName = replace(toLower(acrNameBase), '-', '')

@description('The location where the ACR should be created.')
param location string = resourceGroup().location

resource acr 'Microsoft.ContainerRegistry/registries@2023-01-01-preview' = {
  name: acrName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: true
  }
}

output acrLoginServer string = acr.properties.loginServer
output acrName string = acr.name
