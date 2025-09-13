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
    delegations: subnet.?delegations ?? [
      {
        name: 'delegation'
        properties: {
          serviceName: 'Microsoft.Web/serverFarms'
        }
      }
    ]
    serviceEndpoints: subnet.?serviceEndpoints ?? [
      {
        service: 'Microsoft.Web'
        locations: [
          '*'
        ]
      }
    ]
  }
}]
