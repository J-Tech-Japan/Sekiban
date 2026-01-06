@description('Location for all resources.')
param location string = resourceGroup().location

@description('Name of the existing virtual network')
param vnetName string = 'vn-${resourceGroup().name}'

module newVnet 'deploy-vnet-general.bicep' = {
  name: 'newVnetDeployment'
  params: {
    vnetName: vnetName
    location: location
    vnetAddressPrefix: '10.0.0.0/16'
    subnetConfigs: [
      {
        name: 'frontend-subnet'
        addressPrefix: '10.0.0.0/24'
      }
      {
        name: 'backend-subnet'
        addressPrefix: '10.0.1.0/24'
      }
    ]
  }
}
