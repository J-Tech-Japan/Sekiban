@description('The environment name suffix to add to resource names')
param environmentName string

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
param appServiceName string = 'map-scan-api-${environmentName}'

@description('The resource group containing the virtual network')
param vnetResourceGroup string = resourceGroup().name

@description('Key Vault name')
param keyVaultName string = 'kv-${appServiceName}'

@description('Enable VNet integration')
param enableVnetIntegration bool = false

@description('The name of the virtual network')
param vnetName string

@description('The name of the subnet for the MapScan API')
param subnetName string

// AAD params
param aadClientIdSecretName string = 'AadClientId'
param aadTenantIdSecretName string = 'AadTenantId'
param aadClientSecretSecretName string = 'AadClientSecret'
param aadAudienceSecretName string = 'AadAudience'
param aadDomainSecretName string = 'AadDomain'

// Connection string params
param SekibanConnectionStringName string = 'SekibanCosmos'
param SekibanBlobConnectionStringName string = 'SekibanBlob'
param QueueConnectionStringName string = 'QueueStorage'

param SekibanConnectionStringSecretName string = 'MapScanCosmosDbConnectionString'
param SekibanBlobConnectionStringSecretName string = 'MapScanBlobConnectionString'
param QueueConnectionStringSecretName string = 'MapScanQueueConnectionString'

param DivideScanAreaFunctionTriggerQueueName string = 'divide-scanarea'
param municipalityApiBaseUrl string
param SekibanInfrastructure string = 'Cosmos'

@description('App Service Plan already exists')
param appServicePlanExists bool = false

// Construct subnet ID string using provided parameters
var mapScanApiSubnetId = enableVnetIntegration
  ? '/subscriptions/${subscription().subscriptionId}/resourceGroups/${vnetResourceGroup}/providers/Microsoft.Network/virtualNetworks/${vnetName}/subnets/${subnetName}'
  : ''

// Reference to existing Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// Deploy App Service Plan
module appServicePlanModule 'plan.bicep' = {
  name: 'appServicePlanDeployment'
  params: {
    appServiceName: appServiceName
    location: location
    appServicePlanSku: appServicePlanSku
    appServicePlanExists: appServicePlanExists
  }
}

// Deploy Application Insights
module applicationInsightsModule 'application-insights.bicep' = {
  name: 'appInsightsDeployment'
  params: {
    appServiceName: appServiceName
    location: location
  }
}

// Deploy Log Analytics Workspace
module logAnalyticsModule 'log-analytics-workspace.bicep' = {
  name: 'logAnalyticsDeployment'
  params: {
    appServiceName: appServiceName
    location: location
  }
}

// Deploy App Service
module appServiceModule 'app-service.bicep' = {
  name: 'appServiceDeployment'
  params: {
    appServiceName: appServiceName
    location: location
    appServicePlanId: appServicePlanModule.outputs.appServicePlanId
    keyVaultName: keyVaultName
    environmentName: environmentName
    applicationInsightsInstrumentationKey: applicationInsightsModule.outputs.instrumentationKey
    applicationInsightsConnectionString: applicationInsightsModule.outputs.connectionString
    aadClientIdSecretName: aadClientIdSecretName
    aadTenantIdSecretName: aadTenantIdSecretName
    aadClientSecretSecretName: aadClientSecretSecretName
    aadAudienceSecretName: aadAudienceSecretName
    aadDomainSecretName: aadDomainSecretName
    SekibanConnectionStringName: SekibanConnectionStringName
    SekibanBlobConnectionStringName: SekibanBlobConnectionStringName
    QueueConnectionStringName: QueueConnectionStringName
    SekibanConnectionStringSecretName: SekibanConnectionStringSecretName
    SekibanBlobConnectionStringSecretName: SekibanBlobConnectionStringSecretName
    QueueConnectionStringSecretName: QueueConnectionStringSecretName
    DivideScanAreaFunctionTriggerQueueName: DivideScanAreaFunctionTriggerQueueName
    municipalityApiBaseUrl: municipalityApiBaseUrl
    SekibanInfrastructure: SekibanInfrastructure
  }
}

// Deploy Diagnostic Settings
module diagnosticSettingsModule 'diagnostic-settings.bicep' = {
  name: 'diagnosticSettingsDeployment'
  params: {
    appServiceName: appServiceName
    webAppResourceId: appServiceModule.outputs.webAppResourceId
    logAnalyticsWorkspaceId: logAnalyticsModule.outputs.workspaceId
  }
}

// Deploy VNet Integration if enabled
module vnetIntegrationModule 'vnet-integration.bicep' = if (enableVnetIntegration) {
  name: 'vnetIntegrationDeployment'
  params: {
    webAppResourceId: appServiceModule.outputs.webAppResourceId
    subnetResourceId: mapScanApiSubnetId
  }
}

// Deploy Key Vault Access Policy
module keyVaultAccessModule 'key-vault-access.bicep' = {
  name: 'keyVaultAccessDeployment'
  params: {
    keyVaultName: keyVaultName
    appServicePrincipalId: appServiceModule.outputs.principalId
    appServiceTenantId: appServiceModule.outputs.tenantId
  }
}

// Outputs
output mapScanApiAppServiceName string = appServiceModule.outputs.name
output mapScanApiAppServiceUrl string = appServiceModule.outputs.url
output applicationInsightsInstrumentationKey string = applicationInsightsModule.outputs.instrumentationKey
