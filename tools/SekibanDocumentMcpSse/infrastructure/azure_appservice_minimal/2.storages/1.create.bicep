// Create Storage Account
param storageAccountName string = 'st${uniqueString(resourceGroup().id)}'
param location string = resourceGroup().location

// Storage Account name must be between 3 and 24 characters in length and use numbers and lower-case letters only.
resource storageAccount 'Microsoft.Storage/storageAccounts@2022-09-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
  }
}

// Output storage account properties
output storageAccountName string = storageAccount.name
output storageAccountId string = storageAccount.id
output storageAccountConnectionString string = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'