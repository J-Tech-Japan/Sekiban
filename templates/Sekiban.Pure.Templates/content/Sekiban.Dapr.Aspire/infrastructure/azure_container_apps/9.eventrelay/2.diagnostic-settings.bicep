param containerAppName string = 'eventrelay-${resourceGroup().name}'
param logAnalyticsWorkspaceResourceId string = 'law-${resourceGroup().name}'

// Reference to the existing EventRelay Container App
resource containerApp 'Microsoft.App/containerApps@2023-05-01' existing = {
  name: containerAppName
}

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2021-06-01' existing = {
  name: logAnalyticsWorkspaceResourceId
}

// Diagnostic settings for EventRelay
resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'diag-${containerAppName}'
  scope: containerApp
  properties: {
    workspaceId: logAnalyticsWorkspace.id
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

output diagnosticSettingsName string = diagnosticSettings.name