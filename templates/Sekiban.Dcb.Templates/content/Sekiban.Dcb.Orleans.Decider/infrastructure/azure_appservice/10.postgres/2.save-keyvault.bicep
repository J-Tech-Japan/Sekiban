@description('PostgreSQL Flexible Server name')
param serverName string = 'psql-${resourceGroup().name}'

@description('Database name')
param databaseName string = 'identitydb'

@description('Key Vault name')
param keyVaultName string = 'kv-${resourceGroup().name}'

@description('Administrator login')
param administratorLogin string = 'identityadmin'

@secure()
param administratorLoginPassword string

// Reference existing PostgreSQL server
resource postgresServer 'Microsoft.DBforPostgreSQL/flexibleServers@2023-03-01-preview' existing = {
  name: serverName
}

// Reference existing Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// Build connection string
var connectionString = 'Host=${postgresServer.properties.fullyQualifiedDomainName};Database=${databaseName};Username=${administratorLogin};Password=${administratorLoginPassword};SSL Mode=Require;Trust Server Certificate=true'

// Save connection string to Key Vault
resource identityPostgresSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'IdentityPostgresConnectionString'
  properties: {
    value: connectionString
  }
}

output secretName string = identityPostgresSecret.name
