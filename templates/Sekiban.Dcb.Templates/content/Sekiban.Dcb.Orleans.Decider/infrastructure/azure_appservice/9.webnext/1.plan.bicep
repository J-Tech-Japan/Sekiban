param appServicePlanName string = 'asp-webnext-${resourceGroup().name}'
param location string = resourceGroup().location

@description('SKU for the App Service Plan')
param skuName string = 'B1'

@description('Tier for the App Service Plan')
param skuTier string = 'Basic'

resource appServicePlan 'Microsoft.Web/serverfarms@2022-03-01' = {
  name: appServicePlanName
  location: location
  kind: 'linux'
  sku: {
    name: skuName
    tier: skuTier
  }
  properties: {
    reserved: true // Required for Linux
  }
}

output appServicePlanId string = appServicePlan.id
output appServicePlanName string = appServicePlan.name
