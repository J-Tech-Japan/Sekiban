param appServiceName string
param location string
param appServicePlanId string
param keyVaultName string
param environmentName string
param applicationInsightsInstrumentationKey string
param applicationInsightsConnectionString string

param aadClientIdSecretName string
param aadTenantIdSecretName string
param aadClientSecretSecretName string
param aadAudienceSecretName string
param aadDomainSecretName string

param SekibanConnectionStringName string
param SekibanBlobConnectionStringName string
param QueueConnectionStringName string

param SekibanConnectionStringSecretName string
param SekibanBlobConnectionStringSecretName string
param QueueConnectionStringSecretName string

param DivideScanAreaFunctionTriggerQueueName string
param municipalityApiBaseUrl string
param SekibanInfrastructure string

// Deploy the basic App Service
module appServiceCreate 'app-service-create.bicep' = {
  name: 'appServiceCreateDeployment'
  params: {
    appServiceName: appServiceName
    location: location
    appServicePlanId: appServicePlanId
  }
}

// Configure connection strings
module connectionStrings 'app-service-connection-strings.bicep' = {
  name: 'connectionStringsDeployment'
  params: {
    appServiceName: appServiceName
    keyVaultName: keyVaultName
    SekibanConnectionStringName: SekibanConnectionStringName
    SekibanBlobConnectionStringName: SekibanBlobConnectionStringName
    QueueConnectionStringName: QueueConnectionStringName
    SekibanConnectionStringSecretName: SekibanConnectionStringSecretName
    SekibanBlobConnectionStringSecretName: SekibanBlobConnectionStringSecretName
    QueueConnectionStringSecretName: QueueConnectionStringSecretName
  }
  dependsOn: [
    appServiceCreate
  ]
}

// Configure app settings
module appSettings 'app-service-app-settings.bicep' = {
  name: 'appSettingsDeployment'
  params: {
    appServiceName: appServiceName
    keyVaultName: keyVaultName
    environmentName: environmentName
    applicationInsightsInstrumentationKey: applicationInsightsInstrumentationKey
    applicationInsightsConnectionString: applicationInsightsConnectionString
    aadClientIdSecretName: aadClientIdSecretName
    aadTenantIdSecretName: aadTenantIdSecretName
    aadClientSecretSecretName: aadClientSecretSecretName
    aadAudienceSecretName: aadAudienceSecretName
    aadDomainSecretName: aadDomainSecretName
    DivideScanAreaFunctionTriggerQueueName: DivideScanAreaFunctionTriggerQueueName
    municipalityApiBaseUrl: municipalityApiBaseUrl
    SekibanInfrastructure: SekibanInfrastructure
  }
  dependsOn: [
    appServiceCreate
  ]
}

// Pass through outputs from the create module
output name string = appServiceCreate.outputs.name
output url string = appServiceCreate.outputs.url
output webAppResourceId string = appServiceCreate.outputs.webAppResourceId
output principalId string = appServiceCreate.outputs.principalId
output tenantId string = appServiceCreate.outputs.tenantId
