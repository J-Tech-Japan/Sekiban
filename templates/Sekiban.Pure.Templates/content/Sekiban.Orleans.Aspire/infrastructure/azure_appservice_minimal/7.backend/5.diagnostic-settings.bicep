param appServiceName string = 'backend-${resourceGroup().name}'
param logAnalyticsWorkspaceName string = 'law-${resourceGroup().name}'

var logAnalyticsWorkspaceId = resourceId('Microsoft.OperationalInsights/workspaces', logAnalyticsWorkspaceName)

resource webApp 'Microsoft.Web/sites@2022-09-01' existing = {
  name: appServiceName
}

resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'diag-${appServiceName}'
  scope: webApp
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        category: 'AppServiceHTTPLogs'
        enabled: true
      }
      {
        category: 'AppServiceConsoleLogs'
        enabled: true
      }
      {
        category: 'AppServiceAppLogs'
        enabled: true
      }
      {
        category: 'AppServiceAuditLogs'
        enabled: false
      }
      {
        category: 'AppServiceIPSecAuditLogs'
        enabled: false
      }
      {
        category: 'AppServicePlatformLogs'
        enabled: false
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}
