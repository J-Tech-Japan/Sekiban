@description('Event Hub Namespace name')
param eventHubNamespaceName string = 'ehns-${resourceGroup().name}'

@description('Authorization rule name for Event Hub client')
param authorizationRuleName string = 'EventHubClientAuthRule'

@description('Existing Key Vault name')
param keyVaultName string = 'kv-${resourceGroup().name}'

@description('Queue type for Orleans')
param orleansQueueType string = 'azurestorage' //'eventhub'

// Reference existing Event Hub Namespace
resource namespace 'Microsoft.EventHub/namespaces@2022-10-01-preview' existing = if (orleansQueueType == 'eventhub') {
  name: eventHubNamespaceName
}

// Reference existing authorization rule
resource authRule 'Microsoft.EventHub/namespaces/authorizationRules@2022-10-01-preview' existing = if (orleansQueueType == 'eventhub') {
  parent: namespace
  name: authorizationRuleName
}

// Reference existing Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// Save Event Hub connection string to Key Vault
resource eventHubConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (orleansQueueType == 'eventhub') {
  parent: keyVault
  name: 'EventHubConnectionString'
  properties: {
    value: authRule.listKeys().primaryConnectionString
  }
}

// Save Event Hub primary key to Key Vault
resource eventHubPrimaryKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (orleansQueueType == 'eventhub') {
  parent: keyVault
  name: 'EventHubPrimaryKey'
  properties: {
    value: authRule.listKeys().primaryKey
  }
}

// Save Event Hub namespace name to Key Vault
resource eventHubNamespaceNameSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (orleansQueueType == 'eventhub') {
  parent: keyVault
  name: 'EventHubNamespaceName'
  properties: {
    value: namespace.name
  }
}

// Outputs
output eventHubConnectionStringSecretName string = orleansQueueType == 'eventhub' ? eventHubConnectionStringSecret.name : ''
output eventHubPrimaryKeySecretName string = orleansQueueType == 'eventhub' ? eventHubPrimaryKeySecret.name : ''
output eventHubNamespaceNameSecretName string = orleansQueueType == 'eventhub' ? eventHubNamespaceNameSecret.name : ''
output keyVaultName string = keyVault.name
