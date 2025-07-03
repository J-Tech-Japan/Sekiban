@description('The Azure region for deploying resources')
param location string = resourceGroup().location

@description('The name of the storage account for Dapr')
param storageAccountName string = 'storage${replace(resourceGroup().name, '-', '')}'

@description('The name of the blob container used for Dapr state store')
param daprStateBlobContainerName string = 'daprstate'

@description('The name of the queue used for Dapr pub/sub')
param daprPubSubQueueName string = 'daprpubsub'

@description('The name of the table used for Dapr state store (if using table storage)')
param daprStateTableName string = 'daprstate'

// Create the storage account for Dapr
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
  }
}

// Define the tableServices resource under the storage account
resource tableServices 'Microsoft.Storage/storageAccounts/tableServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
}

// Create the table used for Dapr state store (if using table storage)
resource daprStateTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-01-01' = {
  parent: tableServices
  name: daprStateTableName
}

// Define the blobServices resource under the storage account
resource blobServices 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
}

// Create the blob container used for Dapr state store
resource daprStateContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobServices
  name: daprStateBlobContainerName
  properties: {
    publicAccess: 'None'
  }
}

// Define the queueServices resource under the storage account
resource queueServices 'Microsoft.Storage/storageAccounts/queueServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
}

// Create the queue used for Dapr pub/sub
resource daprPubSubQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-01-01' = {
  parent: queueServices
  name: daprPubSubQueueName
}

// Get the storage account connection string for output
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'

// Outputs
output storageAccountName string = storageAccount.name
output storageConnectionString string = storageConnectionString
output daprStateBlobContainerName string = daprStateContainer.name
output daprStateTableName string = daprStateTable.name
output daprPubSubQueueName string = daprPubSubQueue.name
output storageAccountKey string = storageAccount.listKeys().keys[0].value