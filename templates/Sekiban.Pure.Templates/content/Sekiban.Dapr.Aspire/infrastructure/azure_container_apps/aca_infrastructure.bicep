// Infrastructure-only Bicep file to deploy base resources for Sekiban Dapr Aspire on Azure Container Apps
// This file deploys everything except the Container Apps themselves
// Use this first, then push images, then deploy aca_apps.bicep

targetScope = 'resourceGroup'

@description('The type of Dapr state store to use')
@allowed(['azureblobstorage', 'azuretablestorage'])
param daprStateStoreType string = 'azureblobstorage'

@description('The type of Dapr pub/sub to use')
@allowed(['azurestoragequeues', 'azureservicebus'])
param daprPubSubType string = 'azureservicebus'

// Remove the logAnalyticsSharedKey parameter as it will be retrieved within the module

// 1. Key Vault
module keyVaultCreate '1.keyvault/create.bicep' = {
  name: 'keyVaultDeployment'
  params: {}
}

// 2. Storages (for Dapr State Store and Pub/Sub)
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

// 3. Cosmos DB (for Sekiban Event Store)
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

module cosmosSaveKeyVault '3.cosmos/4.save-keyvault.bicep' = {
  name: 'cosmosSaveKeyVaultDeployment'
  params: {}
  dependsOn: [
    keyVaultCreate
    cosmosCreate
  ]
}

// 4. VNet
module vnetCreate '4.vnet/1.create.bicep' = {
  name: 'vnetCreateDeployment'
  params: {}
}

// 5. Application Insights & Log Analytics
module appInsightsCreate '5.applicationinsights/1.application-insights.bicep' = {
  name: 'appInsightsCreateDeployment'
  params: {}
}

// 6. Managed Environment (Container Apps Environment)
module managedEnv '7.backend/1.managed-env.bicep' = {
  name: 'managedEnvDeployment'
  params: {}
  dependsOn: [
    storageCreate
    cosmosCreate
    appInsightsCreate
    vnetCreate
  ]
}

// 7. Dapr Components (deployed after managed environment)
module daprStateStoreComponent '6.dapr-components/1.statestore-component.bicep' = {
  name: 'daprStateStoreComponentDeployment'
  params: {
    daprStateStoreType: daprStateStoreType
  }
  dependsOn: [
    keyVaultCreate
    storageCreate
    storageSaveKeyVault
    managedEnv
  ]
}

// Create Dapr pub/sub component based on type
module daprPubSubComponent '6.dapr-components/2.pubsub-component.bicep' = {
  name: 'daprPubSubComponentDeployment'
  params: {
    daprPubSubType: daprPubSubType
  }
  dependsOn: [
    keyVaultCreate
    storageCreate
    storageSaveKeyVault
    managedEnv
  ]
}

// Create Service Bus if using it for pub/sub
module serviceBus '6.dapr-components/pubsub-servicebus.bicep' = if (daprPubSubType == 'azureservicebus') {
  name: 'serviceBusDeployment'
  params: {}
  dependsOn: [
    managedEnv
  ]
}

// Outputs
output keyVaultName string = keyVaultCreate.outputs.keyVaultName
output managedEnvironmentId string = managedEnv.outputs.id
output managedEnvironmentName string = managedEnv.outputs.name
output acrName string = replace(toLower('acr-${resourceGroup().name}'), '-', '')
output acrLoginServer string = '${replace(toLower('acr-${resourceGroup().name}'), '-', '')}.azurecr.io'