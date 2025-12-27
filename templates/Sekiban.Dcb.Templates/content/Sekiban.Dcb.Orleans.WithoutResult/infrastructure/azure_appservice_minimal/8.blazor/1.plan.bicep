param shareAppServicePlan bool = true
param appServicePlanName string = shareAppServicePlan ? 'asp-${resourceGroup().name}' : 'asp-${resourceGroup().name}'
param location string = resourceGroup().location
param appServicePlanSku object = {
  name: 'B1'
  tier: 'Basic'
  capacity: 1
}
// Create App Service Plan only if shareAppServicePlan is false
resource newAppServicePlan 'Microsoft.Web/serverfarms@2022-03-01' = if (!shareAppServicePlan) {
  name: appServicePlanName
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

// Get existing App Service Plan if shareAppServicePlan is true
resource existingAppServicePlan 'Microsoft.Web/serverfarms@2022-03-01' existing = if (shareAppServicePlan) {
  name: appServicePlanName
}

// Use either existing or new App Service Plan
output appServicePlanId string = shareAppServicePlan ? existingAppServicePlan.id : newAppServicePlan.id
