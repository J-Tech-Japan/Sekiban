@description('Location for all resources.')
param location string = resourceGroup().location

@description('Base name for all resources')
param vnetName string = 'vn-${resourceGroup().name}' 

@description('Virtual network address prefix')
param vnetAddressPrefix string = '10.0.0.0/16'

@description('Array of subnet configurations')
param subnetConfigs array = []

resource vnet 'Microsoft.Network/virtualNetworks@2022-07-01' = {
  name: vnetName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        vnetAddressPrefix
      ]
    }
    subnets: [for subnet in subnetConfigs: {
      name: subnet.name
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
  }
}

@description('Name of the VNet')
output vnetName string = vnetName

@description('Resource ID of the VNet')
output vnetId string = vnet.id
