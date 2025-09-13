// Main Bicep file to deploy all resources for Sekiban Orleans Aspire on Azure App Service
// This file orchestrates the deployment of various modules.

targetScope = 'resourceGroup'

@description('Orleansクラスターのタイプ指定（cosmos または 他の種類）')
@allowed(['cosmos', 'azuretable'])
param orleansClusterType string = 'cosmos'
param orleansDefaultGrainType string = 'cosmos'

@description('Queue type for Orleans')
param orleansQueueType string = 'azurestorage' //'eventhub'

// 1. Key Vault
module keyVaultCreate '1.keyvault/create.bicep' = {
  name: 'keyVaultDeployment'
  params: {}
}

// 2. Storages
module storageCreate '2.storages/1.create.bicep' = {
  name: 'storageCreateDeployment'
  params: {}
}

module storageSaveKeyVault '2.storages/2.save-keyvault.bicep' = {
  name: 'storageSaveKeyVaultDeployment'
  params: {}
  dependsOn: [
    keyVaultCreate
    storageCreate
  ]
}

// 3. Cosmos DB
module cosmosCreate '3.cosmos/1.create.bicep' = {
  name: 'cosmosCreateDeployment'
  params: {}
}

module cosmosDatabase '3.cosmos/2.database.bicep' = {
  name: 'cosmosDatabaseDeployment'
  params: {}
  dependsOn: [
    cosmosCreate
  ]
}

module cosmosContainer '3.cosmos/3.container.bicep' = {
  name: 'cosmosContainerDeployment'
  params: {}
  dependsOn: [
    cosmosDatabase
  ]
}

module orleansClusterContainer '3.cosmos/4.orleans-cluster-container.bicep' = {
  name: 'orleansClusterContainerDeployment'
  params: {
    orleansClusterType: orleansClusterType
  }
  dependsOn: [
    cosmosCreate
    cosmosDatabase
  ]
}

module orleansGrainContainer '3.cosmos/5.orleans-grain-container.bicep' = {
  name: 'orleansGrainContainerDeployment'
  params: {
    orleansClusterType: orleansClusterType
    orleansDefaultGrainType: orleansDefaultGrainType
  }
  dependsOn: [
    orleansClusterContainer
  ]
}

module cosmosSaveKeyVault '3.cosmos/6.save-keyvault.bicep' = {
  name: 'cosmosSaveKeyVaultDeployment'
  params: {}
  dependsOn: [
    keyVaultCreate
    cosmosCreate
  ]
}

// 4. VNet
// Note: Excludes add-subnet-general.bicep and deploy-vnet-general.bicep as requested.
module vnetCreate '4.vnet/1.create.bicep' = {
  name: 'vnetCreateDeployment'
  params: {}
}

// 5. Application Insights & Log Analytics
module appInsightsCreate '5.applicationinsights_and_log/1.application-insights.bicep' = {
  name: 'appInsightsCreateDeployment'
  params: {}
}

module logAnalyticsCreate '5.applicationinsights_and_log/2.log-analytics-workspace.bicep' = {
  name: 'logAnalyticsCreateDeployment'
  params: {}
}

// 6. Event Hub
module eventHubCreate '6.eventhub/1.create.bicep' = {
  name: 'eventHubCreateDeployment'
  params: {
    orleansQueueType: orleansQueueType
  }
}

module eventHubSaveKeyVault '6.eventhub/2.save-keyvalult.bicep' = {
  name: 'eventHubSaveKeyVaultDeployment'
  params: {
    orleansQueueType: orleansQueueType
  }
  dependsOn: [
    keyVaultCreate
    eventHubCreate
  ]
}

// 7. Backend App Service
module backendPlan '7.backend/1.plan.bicep' = {
  name: 'backendPlanDeployment'
  params: {}
}

module backendAppServiceCreate '7.backend/2.app-service-create.bicep' = {
  name: 'backendAppServiceCreateDeployment'
  params: {}
  dependsOn: [
    backendPlan
  ]
}

module backendKeyVaultAccess '7.backend/3.key-vault-access.bicep' = {
  name: 'backendKeyVaultAccessDeployment'
  params: {}
  dependsOn: [
    keyVaultCreate
    backendAppServiceCreate
  ]
}

module backendConnectionStrings '7.backend/4.connection-strings.bicep' = {
  name: 'backendConnectionStringsDeployment'
  params: {
    orleansQueueType: orleansQueueType
  }
  dependsOn: [
    keyVaultCreate // Needs Key Vault URI
    backendAppServiceCreate
    cosmosSaveKeyVault
  ]
}

module backendDiagnosticSettings '7.backend/5.diagnostic-settings.bicep' = {
  name: 'backendDiagnosticSettingsDeployment'
  params: {}
  dependsOn: [
    logAnalyticsCreate
    backendAppServiceCreate
  ]
}

module backendAppSettings '7.backend/6.app-settings.bicep' = {
  name: 'backendAppSettingsDeployment'
  params: {
    orleansClusterType: orleansClusterType
    orleansDefaultGrainType: orleansDefaultGrainType
    orleansQueueType: orleansQueueType
  }
  dependsOn: [
    keyVaultCreate
    storageCreate
    cosmosCreate
    appInsightsCreate
    backendAppServiceCreate
    eventHubCreate
  ]
}

module backendVnetIntegration '7.backend/7.vnet-integration.bicep' = {
  name: 'backendVnetIntegrationDeployment'
  params: {}
  dependsOn: [
    vnetCreate
    backendAppServiceCreate
  ]
}

module backendIpAccess '7.backend/8.ipaccess.bicep' = {
  name: 'backendIpAccessDeployment'
  params: {}
  dependsOn: [
    backendVnetIntegration
    backendAppServiceCreate
  ]
}

// 8. Blazor Frontend App Service
module blazorPlan '8.blazor/1.plan.bicep' = {
  name: 'blazorPlanDeployment'
  params: {}
}

module blazorAppService '8.blazor/2.app-service.bicep' = {
  name: 'blazorAppServiceDeployment'
  params: {}
  dependsOn: [
    blazorPlan
  ]
}

module blazorDiagnosticSettings '8.blazor/3.diagnositic-settings.bicep' = {
  name: 'blazorDiagnosticSettingsDeployment'
  params: {}
  dependsOn: [
    logAnalyticsCreate
    blazorAppService
  ]
}

module blazorAppSettings '8.blazor/4.app-settings.bicep' = {
  name: 'blazorAppSettingsDeployment'
  params: {}
  dependsOn: [
    appInsightsCreate
    backendAppServiceCreate // Depends on backend URL output from its creation module
    blazorAppService
  ]
}

module blazorVnetIntegration '8.blazor/5.vnet-integration.bicep' = {
  name: 'blazorVnetIntegrationDeployment'
  params: {}
  dependsOn: [
    vnetCreate
    blazorAppService
  ]
}

// Outputs can be added here if needed, for example:
// output backendHostName string = backendAppServiceCreate.outputs.hostName
// output frontendHostName string = blazorAppService.outputs.hostName
