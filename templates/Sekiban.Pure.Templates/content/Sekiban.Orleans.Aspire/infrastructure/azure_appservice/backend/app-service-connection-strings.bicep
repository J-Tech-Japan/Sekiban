param appServiceName string
param keyVaultName string

param SekibanConnectionStringName string
param SekibanBlobConnectionStringName string
param QueueConnectionStringName string

param SekibanConnectionStringSecretName string
param SekibanBlobConnectionStringSecretName string
param QueueConnectionStringSecretName string

// Reference to the existing App Service
resource webApp 'Microsoft.Web/sites@2022-09-01' existing = {
  name: appServiceName
}

// Update the App Service with connection strings
resource connectionStringsConfig 'Microsoft.Web/sites/config@2022-09-01' = {
  parent: webApp
  name: 'connectionstrings'
  properties: {
    ${SekibanConnectionStringName}: {
      value: '@Microsoft.KeyVault(SecretUri=https://${keyVaultName}.vault.azure.net/secrets/${SekibanConnectionStringSecretName}/)'
      type: 'Custom'
    }
    ${SekibanBlobConnectionStringName}: {
      value: '@Microsoft.KeyVault(SecretUri=https://${keyVaultName}.vault.azure.net/secrets/${SekibanBlobConnectionStringSecretName}/)'
      type: 'Custom'
    }
    ${QueueConnectionStringName}: {
      value: '@Microsoft.KeyVault(SecretUri=https://${keyVaultName}.vault.azure.net/secrets/${QueueConnectionStringSecretName}/)'
      type: 'Custom'
    }
  }
}
