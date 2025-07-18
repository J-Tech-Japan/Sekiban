param cosmosAccountName string = 'cosmos-${resourceGroup().name}'
var acrName = replace(toLower('acr-${resourceGroup().name}'), '-', '')

// Reference to existing Azure Container Registry
resource acr 'Microsoft.ContainerRegistry/registries@2023-01-01-preview' existing = {
  name: acrName
}

// Get Cosmos DB connection information
resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2023-04-15' existing = {
  name: cosmosAccountName
}

// Environment variables for EventRelay
output envVars array = [
  {
    name: 'ASPNETCORE_ENVIRONMENT'
    value: 'Production'
  }
  {
    name: 'EventRelay__ConsumerGroup'
    value: 'event-relay-group'
  }
  {
    name: 'EventRelay__MaxConcurrency'
    value: '10'
  }
  {
    name: 'EventRelay__ContinueOnProjectorFailure'
    value: 'false'
  }
  {
    name: 'Sekiban__CosmosDB__DatabaseName'
    value: 'SekibanPostgres'
  }
  {
    name: 'Sekiban__CosmosDB__EventContainerName'
    value: 'SekibanEventContainer'
  }
  {
    name: 'Sekiban__CosmosDB__SnapshotContainerName'
    value: 'SekibanSnapshotContainer'
  }
  {
    name: 'Sekiban__CosmosDB__EventCommandProjectionContainerName'
    value: 'SekibanEventCommandProjectionContainer'
  }
  {
    name: 'Dapr__StateStoreName'
    value: 'sekiban-eventstore'
  }
  {
    name: 'Dapr__PubSubName'
    value: 'sekiban-pubsub'
  }
  {
    name: 'Dapr__EventTopicName'
    value: 'events.all'
  }
  {
    name: 'Dapr__DeadLetterTopic'
    value: 'events.dead-letter'
  }
]

// Secret environment variables
output secretVars array = [
  {
    name: 'acr-password'
    value: acr.listCredentials().passwords[0].value
  }
  {
    name: 'cosmos-uri'
    value: 'https://${cosmosAccountName}.documents.azure.com:443/'
  }
  {
    name: 'cosmos-key'
    value: cosmosAccount.listKeys().primaryMasterKey
  }
]