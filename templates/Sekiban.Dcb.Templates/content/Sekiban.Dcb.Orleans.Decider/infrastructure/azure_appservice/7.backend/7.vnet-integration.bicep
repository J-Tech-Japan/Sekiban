param appServiceName string = 'backend-${resourceGroup().name}'
@description('Name of the existing virtual network')
param vnetName string = 'vn-${resourceGroup().name}'

param subnetName string = 'backend-subnet'

// get existing web app
var webAppResourceId = resourceId('Microsoft.Web/sites', appServiceName)

// VNet Integration
resource vnetIntegration 'Microsoft.Web/sites/networkConfig@2022-03-01' = {
  name: '${split(webAppResourceId, '/')[8]}/virtualNetwork'
  properties: {
    subnetResourceId: resourceId('Microsoft.Network/virtualNetworks/subnets', vnetName, subnetName)
    swiftSupported: true
  }
}
