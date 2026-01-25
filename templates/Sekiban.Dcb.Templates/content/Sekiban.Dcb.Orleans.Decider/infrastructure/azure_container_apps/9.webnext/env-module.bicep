param containerAppNameBackend string = 'backend-${resourceGroup().name}'

resource containerAppBackend 'Microsoft.App/containerApps@2023-05-01' existing = {
  name: containerAppNameBackend
}

// Variables set to Container env
// API_BASE_URL is used by the tRPC server-side code to communicate with the backend API
var envVars = [
  {
    name: 'NODE_ENV'
    value: 'production'
  }
  {
    name: 'API_BASE_URL'
    value: 'https://${containerAppBackend.properties.configuration.ingress.fqdn}'
  }
  {
    name: 'PORT'
    value: '3000'
  }
  {
    name: 'HOSTNAME'
    value: '0.0.0.0'
  }
]

output envVars array = envVars
