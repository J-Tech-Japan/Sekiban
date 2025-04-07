param appServiceName string
param location string
param appServicePlanId string

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
