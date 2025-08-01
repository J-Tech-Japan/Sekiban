param containerAppName string = 'backend-${resourceGroup().name}'
param logAnalyticsWorkspaceName string = 'law-${resourceGroup().name}'

var logAnalyticsWorkspaceId = resourceId('Microsoft.OperationalInsights/workspaces', logAnalyticsWorkspaceName)

resource containerApp 'Microsoft.App/containerApps@2022-03-01' existing = {
  name: containerAppName
}

resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'diag-${containerAppName}'
  scope: containerApp
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}
