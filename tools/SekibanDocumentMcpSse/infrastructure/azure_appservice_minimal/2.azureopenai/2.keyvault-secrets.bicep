// Store Azure OpenAI configuration in Key Vault
param keyVaultName string = 'kv-${resourceGroup().name}'
param azureOpenAIEndpoint string
param azureOpenAIApiKey string
param gptDeploymentName string
param embeddingDeploymentName string

// Reference to existing Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// Store Azure OpenAI Endpoint
resource azureOpenAIEndpointSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'AzureOpenAIEndpoint'
  properties: {
    value: azureOpenAIEndpoint
    contentType: 'text/plain'
  }
}

// Store Azure OpenAI API Key
resource azureOpenAIApiKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'AzureOpenAIApiKey'
  properties: {
    value: azureOpenAIApiKey
    contentType: 'text/plain'
  }
}

// Store GPT Deployment Name
resource azureOpenAIDeploymentNameSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'AzureOpenAIDeploymentName'
  properties: {
    value: gptDeploymentName
    contentType: 'text/plain'
  }
}

// Store Embedding Deployment Name
resource azureOpenAIEmbeddingDeploymentNameSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'AzureOpenAIEmbeddingDeploymentName'
  properties: {
    value: embeddingDeploymentName
    contentType: 'text/plain'
  }
}

// Output secret URIs for reference
output azureOpenAIEndpointSecretUri string = azureOpenAIEndpointSecret.properties.secretUri
output azureOpenAIApiKeySecretUri string = azureOpenAIApiKeySecret.properties.secretUri
output azureOpenAIDeploymentNameSecretUri string = azureOpenAIDeploymentNameSecret.properties.secretUri
output azureOpenAIEmbeddingDeploymentNameSecretUri string = azureOpenAIEmbeddingDeploymentNameSecret.properties.secretUri
