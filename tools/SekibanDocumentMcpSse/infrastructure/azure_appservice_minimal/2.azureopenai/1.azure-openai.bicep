// Create Azure OpenAI resource framework - models to be deployed manually
param location string = resourceGroup().location
param azureOpenAIName string = 'aoai-${resourceGroup().name}'

// Create Azure OpenAI account
resource azureOpenAI 'Microsoft.CognitiveServices/accounts@2023-05-01' = {
  name: azureOpenAIName
  location: location
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: azureOpenAIName
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
    }
  }
}

// Note: Models will be deployed manually through Azure Portal or Azure CLI
// Example deployment commands:
// az cognitiveservices account deployment create --name <azureOpenAIName> --resource-group <resourceGroup> --deployment-name <deploymentName> --model-name <modelName> --model-version <version> --model-format OpenAI --sku-capacity <capacity> --sku-name Standard

// Output values for use in other modules
output azureOpenAIEndpoint string = azureOpenAI.properties.endpoint
output azureOpenAIApiKey string = azureOpenAI.listKeys().key1
output azureOpenAIName string = azureOpenAI.name
