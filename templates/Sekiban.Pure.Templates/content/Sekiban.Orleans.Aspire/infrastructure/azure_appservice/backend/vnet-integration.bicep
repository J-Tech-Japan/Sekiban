param webAppResourceId string
param subnetResourceId string

// VNet Integration
resource vnetIntegration 'Microsoft.Web/sites/networkConfig@2022-03-01' = {
  name: '${split(webAppResourceId, '/')[8]}/virtualNetwork'
  properties: {
    subnetResourceId: subnetResourceId
    swiftSupported: true
  }
}
