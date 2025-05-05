param appServiceName string = 'frontend-${resourceGroup().name}'
param location string = resourceGroup().location
param appServicePlanSku object = {
  name: 'B1'
  tier: 'Basic'
  capacity: 1
}
// Create App Service Plan only if it doesn't exist
resource newAppServicePlan 'Microsoft.Web/serverfarms@2022-03-01' = {
  name: 'asp-${appServiceName}'
  location: location
  sku: {
    name: appServicePlanSku.name
    tier: appServicePlanSku.tier
    capacity: appServicePlanSku.capacity
  }
  properties: {
    reserved: true
  }
  kind: 'linux'
}

// Use either existing or new App Service Plan
output appServicePlanId string = newAppServicePlan.id
