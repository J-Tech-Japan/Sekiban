// Create App Service for MCP
param appServiceName string = resourceGroup().name
param location string = resourceGroup().location
param appServicePlanName string = 'plan-${resourceGroup().name}'
param linuxFxVersion string = 'DOTNETCORE|8.0'

// Reference to existing App Service Plan
resource appServicePlan 'Microsoft.Web/serverfarms@2022-09-01' existing = {
  name: appServicePlanName
}

// Create App Service
resource appService 'Microsoft.Web/sites@2022-09-01' = {
  name: appServiceName
  location: location
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: linuxFxVersion
      alwaysOn: true
      minTlsVersion: '1.2'
      http20Enabled: true
      webSocketsEnabled: true
      ftpsState: 'Disabled'
      healthCheckPath: '/healthz'
    }
  }
  identity: {
    type: 'SystemAssigned'
  }
}

// Output App Service properties
output appServiceName string = appService.name
output appServiceHostName string = appService.properties.defaultHostName
output appServicePrincipalId string = appService.identity.principalId
