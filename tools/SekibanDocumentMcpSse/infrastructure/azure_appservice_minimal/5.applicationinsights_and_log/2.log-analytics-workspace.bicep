// Create Log Analytics Workspace
param logAnalyticsWorkspaceName string = 'log-${resourceGroup().name}'
param location string = resourceGroup().location
param sku string = 'PerGB2018'
param retentionInDays int = 30

// Create Log Analytics Workspace
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logAnalyticsWorkspaceName
  location: location
  properties: {
    sku: {
      name: sku
    }
    retentionInDays: retentionInDays
  }
}

// Output Log Analytics Workspace ID and Primary Key
output logAnalyticsWorkspaceId string = logAnalyticsWorkspace.id
output logAnalyticsWorkspaceCustomerId string = logAnalyticsWorkspace.properties.customerId
output logAnalyticsWorkspacePrimaryKey string = logAnalyticsWorkspace.listKeys().primarySharedKey