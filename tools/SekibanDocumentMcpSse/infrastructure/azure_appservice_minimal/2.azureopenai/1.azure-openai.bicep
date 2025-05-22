// Create Azure OpenAI resource with GPT-4.1 deployment
param location string = resourceGroup().location
param azureOpenAIName string = 'aoai-${resourceGroup().name}'
param gptDeploymentName string = 'gpt-41'
param embeddingDeploymentName string = 'text-embedding-ada-002'

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

// Deploy GPT-4.1 model (latest GPT-4.1 with enhanced coding and instruction-following capabilities)
resource gptDeployment 'Microsoft.CognitiveServices/accounts/deployments@2023-05-01' = {
  parent: azureOpenAI
  name: gptDeploymentName
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4.1'
      version: '2025-04-14'
    }
    raiPolicyName: 'Microsoft.Default'
    versionUpgradeOption: 'OnceNewDefaultVersionAvailable'
  }
  sku: {
    name: 'Standard'
    capacity: 10
  }
}

// Deploy Text Embedding model
resource embeddingDeployment 'Microsoft.CognitiveServices/accounts/deployments@2023-05-01' = {
  parent: azureOpenAI
  name: embeddingDeploymentName
  properties: {
    model: {
      format: 'OpenAI'
      name: 'text-embedding-ada-002'
      version: '2'
    }
    raiPolicyName: 'Microsoft.Default'
    versionUpgradeOption: 'OnceNewDefaultVersionAvailable'
  }
  sku: {
    name: 'Standard'
    capacity: 10
  }
  dependsOn: [
    gptDeployment
  ]
}

// Output values for use in other modules
output azureOpenAIEndpoint string = azureOpenAI.properties.endpoint
output azureOpenAIApiKey string = azureOpenAI.listKeys().key1
output gptDeploymentName string = gptDeploymentName
output embeddingDeploymentName string = embeddingDeploymentName
output azureOpenAIName string = azureOpenAI.name
