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
      // Managed Environment requires subnet which have a size of at least /23
      {
        name: 'aca-subnet'
        addressPrefix: '10.0.0.0/23'
      }
    ]
  }
}
