param appServiceName string
param location string
param appServicePlanSku object
param appServicePlanExists bool

// Reference existing App Service Plan if it exists
resource existingAppServicePlan 'Microsoft.Web/serverfarms@2022-03-01' existing = if (appServicePlanExists) {
  name: 'asp-${appServiceName}'
}

// Create App Service Plan only if it doesn't exist
resource newAppServicePlan 'Microsoft.Web/serverfarms@2022-03-01' = if (!appServicePlanExists) {
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
}

// Use either existing or new App Service Plan
output appServicePlanId string = appServicePlanExists ? existingAppServicePlan.id : newAppServicePlan.id
