param appServiceName string
param keyVaultName string
param environmentName string
param applicationInsightsInstrumentationKey string
param applicationInsightsConnectionString string

param aadClientIdSecretName string
param aadTenantIdSecretName string
param aadClientSecretSecretName string
param aadAudienceSecretName string
param aadDomainSecretName string

param DivideScanAreaFunctionTriggerQueueName string
param municipalityApiBaseUrl string
param SekibanInfrastructure string

// Reference to the existing App Service
resource webApp 'Microsoft.Web/sites@2022-09-01' existing = {
  name: appServiceName
}

// Update the App Service with app settings
resource appSettingsConfig 'Microsoft.Web/sites/config@2022-09-01' = {
  parent: webApp
  name: 'appsettings'
  properties: {
    ASPNETCORE_ENVIRONMENT: environmentName == 'prod' ? 'Production' : 'Production'
    APPINSIGHTS_INSTRUMENTATIONKEY: applicationInsightsInstrumentationKey
    APPLICATIONINSIGHTS_CONNECTION_STRING: applicationInsightsConnectionString
    ApplicationInsightsAgent_EXTENSION_VERSION: '~3'
    'AzureAd__Instance': 'https://login.microsoftonline.com/'
    'AzureAd__Domain': '@Microsoft.KeyVault(SecretUri=https://${keyVaultName}.vault.azure.net/secrets/${aadDomainSecretName}/)'
    'AzureAd__TenantId': '@Microsoft.KeyVault(SecretUri=https://${keyVaultName}.vault.azure.net/secrets/${aadTenantIdSecretName}/)'
    'AzureAd__ClientId': '@Microsoft.KeyVault(SecretUri=https://${keyVaultName}.vault.azure.net/secrets/${aadClientIdSecretName}/)'
    'AzureAd__ClientSecret': '@Microsoft.KeyVault(SecretUri=https://${keyVaultName}.vault.azure.net/secrets/${aadClientSecretSecretName}/)'
    'AzureAd__Audience': '@Microsoft.KeyVault(SecretUri=https://${keyVaultName}.vault.azure.net/secrets/${aadAudienceSecretName}/)'
    MunicipalityApiBaseUrl: municipalityApiBaseUrl
    DivideScanAreaFunctionTriggerQueueName: DivideScanAreaFunctionTriggerQueueName
    'Sekiban__Infrastructure': SekibanInfrastructure
  }
}
