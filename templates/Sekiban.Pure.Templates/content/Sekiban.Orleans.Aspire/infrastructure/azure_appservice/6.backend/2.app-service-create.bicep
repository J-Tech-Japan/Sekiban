param appServiceName string = 'backend-${resourceGroup().name}'
param location string = resourceGroup().location
param appServicePlanName string = 'asp-${appServiceName}'

// Reference to existing App Service Plan
resource appServicePlan 'Microsoft.Web/serverfarms@2022-03-01' existing = {
  name: appServicePlanName
}

// Get the App Service Plan ID
var appServicePlanId = appServicePlan.id

// Create the basic App Service (Web App)
resource webApp 'Microsoft.Web/sites@2022-09-01' = {
  name: appServiceName
  location: location
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|9.0'
      netFrameworkVersion: 'v9.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      httpLoggingEnabled: true
      logsDirectorySizeLimit: 35
      detailedErrorLoggingEnabled: true
      vnetPrivatePortsCount: 2
      webSocketsEnabled: true
    }
  }
  identity: {
    type: 'SystemAssigned'
  }
}

output name string = webApp.name
output url string = 'https://${webApp.properties.defaultHostName}'
output webAppResourceId string = webApp.id
output principalId string = webApp.identity.principalId
output tenantId string = webApp.identity.tenantId
