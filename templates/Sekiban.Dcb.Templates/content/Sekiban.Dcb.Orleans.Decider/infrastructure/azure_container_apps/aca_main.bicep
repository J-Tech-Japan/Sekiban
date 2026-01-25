// Main Bicep file to deploy all resources for Sekiban Orleans Aspire on Azure App Service
// This file orchestrates the deployment of various modules.

targetScope = 'resourceGroup'

@description('Orleansクラスターのタイプ指定（cosmos または 他の種類）')
@allowed(['cosmos', 'azuretable'])
param orleansClusterType string = 'cosmos'
param orleansDefaultGrainType string = 'cosmos'

@description('Queue type for Orleans')
param orleansQueueType string = 'eventhub' //'azurestorage'

@secure()
param logAnalyticsSharedKey string

@secure()
@description('PostgreSQL administrator password for Identity database')
param postgresAdminPassword string = newGuid()

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
module appInsightsCreate '5.applicationinsights/1.application-insights.bicep' = {
  name: 'appInsightsCreateDeployment'
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

// 10. PostgreSQL Flexible Server (for Identity database)
module postgresCreate '10.postgres/1.create.bicep' = {
  name: 'postgresCreateDeployment'
  params: {
    administratorLoginPassword: postgresAdminPassword
  }
}

module postgresSaveKeyVault '10.postgres/2.save-keyvault.bicep' = {
  name: 'postgresSaveKeyVaultDeployment'
  params: {
    administratorLoginPassword: postgresAdminPassword
  }
  dependsOn: [
    keyVaultCreate
    postgresCreate
  ]
}

// 7. Backend App Container
module managedEnv '7.backend/1.managed-env.bicep' = {
  name: 'managedEnvDeployment'
  params: {
    logAnalyticsSharedKey: logAnalyticsSharedKey
  }
  dependsOn: [
    storageCreate
    cosmosCreate
    appInsightsCreate
    eventHubCreate
    vnetCreate
  ]
}

module backendContainerApp '7.backend/2.container-app.bicep' = {
  name: 'backendContainerAppDeployment'
  params: {
    orleansQueueType: orleansQueueType
    orleansClusterType: orleansClusterType
    orleansDefaultGrainType: orleansDefaultGrainType
  }
  dependsOn: [
    managedEnv
    cosmosSaveKeyVault
    postgresSaveKeyVault
  ]
}

module backendKeyVaultAccess '7.backend/3.key-vault-access.bicep' = {
  name: 'backendKeyVaultAccessDeployment'
  params: {}
  dependsOn: [
    backendContainerApp
  ]
}

module backendDiagnosticSettings '7.backend/4.diagnostic-settings.bicep' = {
  name: 'backendDiagnosticSettingsDeployment'
  params: {}
  dependsOn: [
    backendContainerApp
  ]
}

// 7. Frontend App Container
module blazorContainerApp '8.blazor/1.container-app.bicep' = {
  name: 'blazorContainerAppDeployment'
  params: {}
  dependsOn: [
    appInsightsCreate
    vnetCreate
    backendContainerApp
  ]
}

module blazorDiagnosticSettings '8.blazor/2.diagnositic-settings.bicep' = {
  name: 'blazorDiagnosticSettingsDeployment'
  params: {}
  dependsOn: [
    blazorContainerApp
  ]
}

// 9. WebNext (Next.js) App Container
module webnextContainerApp '9.webnext/1.container-app.bicep' = {
  name: 'webnextContainerAppDeployment'
  params: {}
  dependsOn: [
    vnetCreate
    backendContainerApp
  ]
}

module webnextDiagnosticSettings '9.webnext/2.diagnostic-settings.bicep' = {
  name: 'webnextDiagnosticSettingsDeployment'
  params: {}
  dependsOn: [
    webnextContainerApp
  ]
}

// Outputs can be added here if needed, for example:
// output backendHostName string = backendAppServiceCreate.outputs.hostName
// output frontendHostName string = blazorAppService.outputs.hostName
