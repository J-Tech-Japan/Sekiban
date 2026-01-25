@description('The Azure region for deploying resources')
param location string = resourceGroup().location

@description('The name of the storage account for Orleans')
param storageAccountName string = 'st${uniqueString(resourceGroup().id)}'

@description('The name of the table used for Orleans clustering')
param clusteringTableName string = 'myprojectclustering'

@description('The name of the blob container used for Orleans grain state')
param grainStateBlobContainerName string = 'myprojectgrainstate'

@description('The name of the queue used for Orleans streaming')
param queueName string = 'myprojectqueue'

// Create the storage account for Orleans
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

// Create the table used for Orleans clustering
resource clusteringTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-01-01' = {
  parent: tableServices
  name: clusteringTableName
}

// Define the blobServices resource under the storage account
resource blobServices 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
}

// Create the blob container used for Orleans grain state
resource grainStateContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobServices
  name: grainStateBlobContainerName
  properties: {
    publicAccess: 'None'
  }
}

// Define the queueServices resource under the storage account
resource queueServices 'Microsoft.Storage/storageAccounts/queueServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
}

// Create the queue used for Orleans streaming
resource orleansQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-01-01' = {
  parent: queueServices
  name: queueName
}

// Get the storage account connection string for output
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'

// Outputs
output storageAccountName string = storageAccount.name
output storageConnectionString string = storageConnectionString // Output connection string directly if needed, but avoid storing in KV here
output clusteringTableName string = clusteringTable.name
output grainStateBlobContainerName string = grainStateContainer.name
output queueName string = orleansQueue.name
