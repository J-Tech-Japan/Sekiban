// Create App Service Plan for MCP
param appServicePlanName string = 'plan-${resourceGroup().name}'
param location string = resourceGroup().location
param sku object = {
  name: 'B1'
  capacity: 1
}

// Create App Service Plan
resource appServicePlan 'Microsoft.Web/serverfarms@2022-09-01' = {
  name: appServicePlanName
  location: location
  sku: sku
  kind: 'linux'
  properties: {
    reserved: true
  }
}

// Output App Service Plan ID
output appServicePlanId string = appServicePlan.id
