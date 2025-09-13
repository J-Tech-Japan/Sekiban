param baseName string = resourceGroup().name

param basicTables array = [
  'AppTraces'
]

param analyticsTable array = [
  'AppDependencies'
  'AppPerformanceCounters'
]

param logAnalyticsWorkspaceResourceId string = 'law-${baseName}'

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2021-06-01' existing = {
  name: logAnalyticsWorkspaceResourceId
}

resource basicTablesResource 'Microsoft.OperationalInsights/workspaces/tables@2022-10-01' = [for t in basicTables: {
  parent: logAnalyticsWorkspace
  name: t
  properties: {
    plan: 'Basic'
  }
}]

resource analyticsTablesResource 'Microsoft.OperationalInsights/workspaces/tables@2022-10-01' = [for t in analyticsTable: {
  parent: logAnalyticsWorkspace
  name: t
  properties: {
    plan: 'Analytics'
    retentionInDays: 30
    totalRetentionInDays: 30
  }
}]
