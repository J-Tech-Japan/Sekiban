@description('The Azure region for deploying resources')
param location string = resourceGroup().location

@description('The App Service Plan SKU')
param appServicePlanSku object = {
  name: 'B1'
  tier: 'Basic'
  capacity: 1
  kind: 'linux'
}

@description('The name of the App Service')
param appServiceName string = 'backend-${resourceGroup().name}'

@description('The resource group containing the virtual network')
param vnetResourceGroup string = resourceGroup().name

@description('Key Vault name')
param keyVaultName string = 'kv-${resourceGroup().name}'

@description('Enable VNet integration')
param enableVnetIntegration bool = false

@description('The name of the virtual network')
param vnetName string

@description('The name of the subnet for the MapScan API')
param subnetName string

param SekibanConnectionStringName string = 'SekibanCosmos'
param SekibanBlobConnectionStringName string = 'SekibanBlob'
param QueueConnectionStringName string = 'QueueStorage'

param SekibanConnectionStringSecretName string = 'MapScanCosmosDbConnectionString'
param SekibanBlobConnectionStringSecretName string = 'MapScanBlobConnectionString'
param QueueConnectionStringSecretName string = 'MapScanQueueConnectionString'

param DivideScanAreaFunctionTriggerQueueName string = 'divide-scanarea'
param municipalityApiBaseUrl string
param SekibanInfrastructure string = 'Cosmos'

// Construct subnet ID string using provided parameters
var mapScanApiSubnetId = enableVnetIntegration
  ? '/subscriptions/${subscription().subscriptionId}/resourceGroups/${vnetResourceGroup}/providers/Microsoft.Network/virtualNetworks/${vnetName}/subnets/${subnetName}'
  : ''

// Reference to existing Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}
// Define App Service Plan - use existing if it exists, otherwise create new
@description('App Service Plan already exists')
param appServicePlanExists bool = false


// Reference existing App Service Plan if it exists
resource existingAppServicePlan 'Microsoft.Web/serverfarms@2022-03-01' existing = if (appServicePlanExists) {
  name: 'asp-${appServiceName}'
}

// Create App Service Plan only if it doesn't exist
resource newAppServicePlan 'Microsoft.Web/serverfarms@2022-03-01' = if (!appServicePlanExists) {
  name: 'asp-${appServiceName}'
  location: location
  // SKU定義を1箇所に統合
  sku: {
    name: appServicePlanSku.name
    tier: appServicePlanSku.tier
    capacity: appServicePlanSku.capacity
  }
  properties: {
    reserved: true
  }
}

// Create Application Insights
resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'ai-${appServiceName}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    Request_Source: 'rest'
    IngestionMode: 'ApplicationInsights'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// Create Log Analytics Workspace
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2021-06-01' = {
  name: 'law-${appServiceName}'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

// Use either existing or new App Service Plan
var appServicePlanId = appServicePlanExists ? existingAppServicePlan.id : newAppServicePlan.id

// Create the App Service (Web App)
resource webApp 'Microsoft.Web/sites@2022-09-01' = {
  name: appServiceName
  location: location
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|9.0'
      netFrameworkVersion: 'v9.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      httpLoggingEnabled: true
      logsDirectorySizeLimit: 35
      detailedErrorLoggingEnabled: true
      connectionStrings: [
        {
          name: SekibanConnectionStringName
          connectionString: '@Microsoft.KeyVault(SecretUri=https://${keyVaultName}.vault.azure.net/secrets/${SekibanConnectionStringSecretName}/)'
          type: 'Custom'
        }
        {
          name: SekibanBlobConnectionStringName
          connectionString: '@Microsoft.KeyVault(SecretUri=https://${keyVaultName}.vault.azure.net/secrets/${SekibanBlobConnectionStringSecretName}/)'
          type: 'Custom'
        }
        {
          name: QueueConnectionStringName
          connectionString: '@Microsoft.KeyVault(SecretUri=https://${keyVaultName}.vault.azure.net/secrets/${QueueConnectionStringSecretName}/)'
          type: 'Custom'
        }        
      ]
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: environmentName == 'prod' ? 'Production' : 'Production'
        }
        {
          name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
          value: applicationInsights.properties.InstrumentationKey
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: applicationInsights.properties.ConnectionString
        }
        {
          name: 'ApplicationInsightsAgent_EXTENSION_VERSION'
          value: '~3'
        }
        {
          name: 'AzureAd__Instance'
          value: 'https://login.microsoftonline.com/'
        }
        {
          name: 'AzureAd__Domain'
          value: '@Microsoft.KeyVault(SecretUri=https://${keyVaultName}.vault.azure.net/secrets/${aadDomainSecretName}/)'
        }
        {
          name: 'AzureAd__TenantId'
          value: '@Microsoft.KeyVault(SecretUri=https://${keyVaultName}.vault.azure.net/secrets/${aadTenantIdSecretName}/)'
        }
        {
          name: 'AzureAd__ClientId'
          value: '@Microsoft.KeyVault(SecretUri=https://${keyVaultName}.vault.azure.net/secrets/${aadClientIdSecretName}/)'
        }
        {
          name: 'AzureAd__ClientSecret'
          value: '@Microsoft.KeyVault(SecretUri=https://${keyVaultName}.vault.azure.net/secrets/${aadClientSecretSecretName}/)'
        }
        {
          name: 'AzureAd__Audience'
          value: '@Microsoft.KeyVault(SecretUri=https://${keyVaultName}.vault.azure.net/secrets/${aadAudienceSecretName}/)'
        }
        {
          name: 'MunicipalityApiBaseUrl'
          value: municipalityApiBaseUrl
        }
        {
          name:'DivideScanAreaFunctionTriggerQueueName'
          value: DivideScanAreaFunctionTriggerQueueName
        }
        {
          name: 'Sekiban__Infrastructure'
          value: SekibanInfrastructure
        }
      ]
    }
  }
  identity: {
    type: 'SystemAssigned'
  }
}

// App Service diagnostic settings
resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'diag-${appServiceName}'
  scope: webApp
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
        category: 'AppServiceAuditLogs'
        enabled: true
      }
      {
        category: 'AppServiceIPSecAuditLogs'
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

// VNet Integration
resource vnetIntegration 'Microsoft.Web/sites/networkConfig@2022-03-01' = if (enableVnetIntegration) {
  parent: webApp
  name: 'virtualNetwork'
  properties: {
    subnetResourceId: mapScanApiSubnetId
    swiftSupported: true
  }
}

// Grant the App Service access to Key Vault secrets
resource keyVaultAccessPolicy 'Microsoft.KeyVault/vaults/accessPolicies@2023-07-01' = {
  parent: keyVault
  name: 'add'
  properties: {
    accessPolicies: [
      {
        tenantId: webApp.identity.tenantId
        objectId: webApp.identity.principalId
        permissions: {
          secrets: [
            'get'
            'list'
          ]
          keys: [
            'get'
            'list'
          ]
          certificates: [
            'get'
            'list'
          ]
        }
      }
    ]
  }
}

// Outputs
output mapScanApiAppServiceName string = webApp.name
output mapScanApiAppServiceUrl string = 'https://${webApp.properties.defaultHostName}'
output applicationInsightsInstrumentationKey string = applicationInsights.properties.InstrumentationKey
