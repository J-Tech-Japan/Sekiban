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

// 2. Service Bus (for Dapr Pub/Sub)
module serviceBusCreate '2.servicebus/1.create.bicep' = {
  name: 'serviceBusCreateDeployment'
  params: {}
}

module serviceBusSaveKeyVault '2.servicebus/2.save-keyvault.bicep' = {
  name: 'serviceBusSaveKeyVaultDeployment'
  params: {}
  dependsOn: [
    keyVaultCreate
    serviceBusCreate
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

// 3.5 Dapr-specific Cosmos DB resources
module cosmosDaprDatabase '3.cosmos/5.dapr-database.bicep' = {
  name: 'cosmosDaprDatabaseDeployment'
  params: {}
  dependsOn: [
    cosmosCreate
  ]
}

module cosmosDaprContainer '3.cosmos/6.dapr-container.bicep' = {
  name: 'cosmosDaprContainerDeployment'
  params: {}
  dependsOn: [
    cosmosDaprDatabase
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
    serviceBusCreate
    cosmosCreate
    appInsightsCreate
    vnetCreate
  ]
}

// 7. Dapr Components (deployed after managed environment)
module daprStateStoreComponent '6.dapr-components/1.cosmos-statestore-component.bicep' = {
  name: 'daprStateStoreComponentDeployment'
  params: {}
  dependsOn: [
    keyVaultCreate
    cosmosCreate
    cosmosSaveKeyVault
    managedEnv
  ]
}

// Create Dapr pub/sub component
module daprPubSubComponent '6.dapr-components/2.servicebus-pubsub-component.bicep' = {
  name: 'daprPubSubComponentDeployment'
  params: {}
  dependsOn: [
    keyVaultCreate
    serviceBusCreate
    serviceBusSaveKeyVault
    managedEnv
  ]
}

// Outputs
output keyVaultName string = keyVaultCreate.outputs.keyVaultName
output managedEnvironmentId string = managedEnv.outputs.id
output managedEnvironmentName string = managedEnv.outputs.name
output acrName string = replace(toLower('acr-${resourceGroup().name}'), '-', '')
output acrLoginServer string = '${replace(toLower('acr-${resourceGroup().name}'), '-', '')}.azurecr.io'