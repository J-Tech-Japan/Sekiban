@secure()
param logAnalyticsSharedKey string

param managedEnvName string = 'aca-${resourceGroup().name}'
param location string = resourceGroup().location
param logAnalyticsWorkspaceResourceId string = 'law-${resourceGroup().name}'

@description('Name of the existing virtual network')
param vnetName string = 'vn-${resourceGroup().name}'
param subnetName string = 'aca-subnet'

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2021-06-01' existing = {
  name: logAnalyticsWorkspaceResourceId
}

// Create App Container Managed Environment only if it doesn't exist
resource managedEnv 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: managedEnvName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsWorkspace.properties.customerId
        sharedKey: logAnalyticsSharedKey
      }
    }
    vnetConfiguration: {
      infrastructureSubnetId: resourceId('Microsoft.Network/virtualNetworks/subnets', vnetName, subnetName)
    }
  }
}

// Use either existing or new Container Managed Environment
output managedEnvId string = managedEnv.id
