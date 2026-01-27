param appServiceName string = 'webnext-${resourceGroup().name}'
param vnetName string = 'vnet-${resourceGroup().name}'
param subnetName string = 'snet-webnext-${resourceGroup().name}'
param location string = resourceGroup().location

@description('Address prefix for the webnext subnet')
param subnetAddressPrefix string = '10.0.4.0/24'

// Reference existing VNet
resource vnet 'Microsoft.Network/virtualNetworks@2023-04-01' existing = {
  name: vnetName
}

// Add subnet for webnext
resource subnet 'Microsoft.Network/virtualNetworks/subnets@2023-04-01' = {
  parent: vnet
  name: subnetName
  properties: {
    addressPrefix: subnetAddressPrefix
    delegations: [
      {
        name: 'delegation'
        properties: {
          serviceName: 'Microsoft.Web/serverFarms'
        }
      }
    ]
  }
}

// Reference existing App Service
resource webApp 'Microsoft.Web/sites@2022-09-01' existing = {
  name: appServiceName
}

// VNet integration for the App Service
resource vnetIntegration 'Microsoft.Web/sites/virtualNetworkConnections@2022-09-01' = {
  parent: webApp
  name: 'vnetIntegration'
  properties: {
    vnetResourceId: subnet.id
    isSwift: true
  }
}
