@description('Location for all resources.')
param location string = resourceGroup().location

@description('Name of the existing virtual network')
param vnetName string

@description('Array of subnet configurations to add')
param subnetConfigs array

resource existingVNet 'Microsoft.Network/virtualNetworks@2022-07-01' existing = {
  name: vnetName
}

resource subnetResources 'Microsoft.Network/virtualNetworks/subnets@2022-07-01' = [for subnet in subnetConfigs: {
  name: subnet.name
  parent: existingVNet
  properties: {
    addressPrefix: subnet.addressPrefix
    delegations: contains(subnet, 'delegations') ? subnet.delegations : [
      {
        name: 'delegation'
        properties: {
          serviceName: 'Microsoft.Web/serverFarms'
        }
      }
    ]
    serviceEndpoints: contains(subnet, 'serviceEndpoints') ? subnet.serviceEndpoints : [
      {
        service: 'Microsoft.Web'
        locations: [
          '*'
        ]
      }
    ]
  }
}]

@description('Map of subnet names to their resource IDs')
output subnetIds object = {for (subnet, i) in subnetConfigs: subnet.name => subnetResources[i].id}
