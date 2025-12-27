param appServiceName string = 'backend-${resourceGroup().name}'
param vnetName string = 'vn-${resourceGroup().name}'

// Reference to the existing App Service
resource webApp 'Microsoft.Web/sites@2022-09-01' existing = {
  name: appServiceName
}

var frontendSubnetId = '/subscriptions/${subscription().subscriptionId}/resourceGroups/${resourceGroup().name}/providers/Microsoft.Network/virtualNetworks/${vnetName}/subnets/frontend-subnet'


// IP restrictions configuration for the web app
resource webAppConfig 'Microsoft.Web/sites/config@2022-03-01' = {
  parent: webApp
  name: 'web'
  properties: {
    // Updated IP restrictions based on VNet integration requirement
    ipSecurityRestrictions: [
      {
        // Allow traffic from the frontend subnet via VNet integration
        vnetSubnetResourceId: frontendSubnetId // Use the constructed subnet ID
        action: 'Allow'
        priority: 100 // Matches screenshot priority
        name: 'AllowFrontendSubnetVNet' // Corresponds to 'frontendsubnet' rule in screenshot
      }
      {
        // Deny all other traffic
        ipAddress: '0.0.0.0/0' // Corresponds to 'all' rule in screenshot
        action: 'Deny'
        priority: 200 // Matches screenshot priority
        name: 'DenyAllOthers' // Kept original name
      }
    ]
    // Ensure scm site uses same restrictions if needed (optional, add scmIpSecurityRestrictions array if required)
    // scmIpSecurityRestrictions: [ ... ]
    // ipSecurityRestrictionsDefaultAction: 'Deny' // Optional: Explicitly set default action if needed
  }
}
