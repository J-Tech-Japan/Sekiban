param appServiceName string = 'webnext-${resourceGroup().name}'
param backendAppServiceName string = 'backend-${resourceGroup().name}'
param applicationInsightsName string = 'ai-${resourceGroup().name}'

// Reference existing App Services
resource webApp 'Microsoft.Web/sites@2022-09-01' existing = {
  name: appServiceName
}

resource backendWebApp 'Microsoft.Web/sites@2022-09-01' existing = {
  name: backendAppServiceName
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: applicationInsightsName
}

// Configure app settings for Next.js
resource appSettingsConfig 'Microsoft.Web/sites/config@2022-09-01' = {
  parent: webApp
  name: 'appsettings'
  properties: {
    NODE_ENV: 'production'
    API_BASE_URL: 'https://${backendWebApp.properties.defaultHostName}'
    PORT: '3000'
    HOSTNAME: '0.0.0.0'
    APPLICATIONINSIGHTS_CONNECTION_STRING: applicationInsights.properties.ConnectionString
    WEBSITE_NODE_DEFAULT_VERSION: '~20'
    // Disable Oryx build - standalone has all dependencies bundled
    SCM_DO_BUILD_DURING_DEPLOYMENT: 'false'
    ENABLE_ORYX_BUILD: 'false'
  }
}
