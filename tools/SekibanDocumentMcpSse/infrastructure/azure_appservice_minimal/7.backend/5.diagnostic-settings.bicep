// Configure diagnostic settings for App Service
param appServiceName string = '${resourceGroup().name}'
param logAnalyticsWorkspaceName string = 'log-${resourceGroup().name}'
param diagnosticSettingName string = 'AppServiceDiagnostics'

// Reference to existing App Service
resource appService 'Microsoft.Web/sites@2022-09-01' existing = {
  name: appServiceName
}

// Reference to existing Log Analytics workspace
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' existing = {
  name: logAnalyticsWorkspaceName
}

// Configure diagnostic settings
resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: diagnosticSettingName
  scope: appService
  properties: {
    workspaceId: logAnalyticsWorkspace.id
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
        category: 'AppServicePlatformLogs'
        enabled: true
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
