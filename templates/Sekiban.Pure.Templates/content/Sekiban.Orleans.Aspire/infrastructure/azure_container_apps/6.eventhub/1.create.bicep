@description('Event Hub instance name')
param eventHubName string =  'eventhub-${resourceGroup().name}'

@description('Target region for resources')
param location string = resourceGroup().location

@description('Event Hub Namespace name')
param namespaceName string = 'ehns-${resourceGroup().name}'

@description('Queue type for Orleans')
param orleansQueueType string = 'azurestorage' //'eventhub'

@description('Event Hub capacity (throughput units)')
param skuCapacity int = 1

@description('Event Hub SKU name')
param skuName string = 'Basic'

@description('Authorization rule name for Event Hub client')
param authorizationRuleName string = 'EventHubClientAuthRule'

// Create Event Hub Namespace
resource namespace 'Microsoft.EventHub/namespaces@2022-10-01-preview' = if (orleansQueueType == 'eventhub') {
  name: namespaceName
  location: location
  sku: {
    name: skuName
    tier: skuName
    capacity: skuCapacity
  }
  properties: {
    isAutoInflateEnabled: false
    maximumThroughputUnits: 0
  }
}

// Create Event Hub
resource eventHub 'Microsoft.EventHub/namespaces/eventhubs@2022-10-01-preview' = if (orleansQueueType == 'eventhub') {
  parent: namespace
  name: eventHubName
  properties: {
    messageRetentionInDays: 1
    partitionCount: 2
  }
}

// Create access rights for Event Hub
resource authorizationRule 'Microsoft.EventHub/namespaces/authorizationRules@2022-10-01-preview' = if (orleansQueueType == 'eventhub') {
  parent: namespace
  name: authorizationRuleName
  properties: {
    rights: [
      'Listen'
      'Send'
      'Manage'
    ]
  }
}

// Outputs
output eventHubNamespaceName string = orleansQueueType == 'eventhub' ? namespace.name : ''
output eventHubName string = orleansQueueType == 'eventhub' ? eventHub.name : ''
