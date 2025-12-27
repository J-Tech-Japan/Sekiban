# Virtual Network and Subnet Templates Usage Guide

This document explains how to use the two Bicep templates:
- `deploy-vnet.bicep`: Creates a new Virtual Network with configurable subnets
- `add-subnet.bicep`: Adds new subnets to an existing Virtual Network

## 1. Creating a New Virtual Network with Subnets

Use the `deploy-vnet.bicep` template to create a new VNET with custom subnets:

```bicep
module newVnet 'deploy-vnet.bicep' = {
  name: 'newVnetDeployment'
  params: {
    baseName: 'myproject'
    location: 'japaneast'
    vnetAddressPrefix: '10.0.0.0/16'
    subnetConfigs: [
      {
        name: 'frontend-subnet'
        addressPrefix: '10.0.0.0/24'
      }
      {
        name: 'api-subnet'
        addressPrefix: '10.0.1.0/24'
      }
      {
        name: 'backend-subnet'
        addressPrefix: '10.0.2.0/24'
      }
    ]
  }
}

// Access the created subnet IDs
var frontendSubnetId = newVnet.outputs.subnetIds['frontend-subnet']
var apiSubnetId = newVnet.outputs.subnetIds['api-subnet']
var backendSubnetId = newVnet.outputs.subnetIds['backend-subnet']
```

### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| location | string | Resource group location | Azure region for resource deployment |
| baseName | string | Resource group name | Base name used to generate VNET name |
| vnetAddressPrefix | string | '10.0.0.0/16' | CIDR address space for the VNET |
| subnetConfigs | array | [] | Array of subnet configuration objects |

## 2. Adding Subnets to an Existing Virtual Network

Use the `add-subnet.bicep` template to add subnets to an existing VNET:

```bicep
module addSubnets 'add-subnet.bicep' = {
  name: 'addSubnetsDeployment'
  params: {
    location: 'japaneast'
    vnetName: 'existing-vnet-name'
    subnetConfigs: [
      {
        name: 'new-service-subnet'
        addressPrefix: '10.0.3.0/24'
      },
      {
        name: 'database-subnet'
        addressPrefix: '10.0.4.0/24'
        // Custom delegation example
        delegations: [
          {
            name: 'sqlDelegation'
            properties: {
              serviceName: 'Microsoft.Sql/servers'
            }
          }
        ]
      }
    ]
  }
}

// Access the new subnet IDs
var newServiceSubnetId = addSubnets.outputs.subnetIds['new-service-subnet']
var databaseSubnetId = addSubnets.outputs.subnetIds['database-subnet']
```

### Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| location | string | Azure region for resource deployment |
| vnetName | string | Name of the existing virtual network |
| subnetConfigs | array | Array of subnet configuration objects to add |

## 3. Subnet Configuration Options

Each subnet configuration object can include:

```bicep
{
  name: string                      // Required: Name of the subnet
  addressPrefix: string             // Required: CIDR notation for subnet
  delegations: array                // Optional: Service delegations
  serviceEndpoints: array           // Optional: Service endpoints
}
```

### Default values

If not specified, each subnet will automatically get the following defaults:

1. **Delegations**: Microsoft.Web/serverFarms
2. **Service Endpoints**: Microsoft.Web in all locations

## 4. Combining Both Templates

You can first create a VNET and then add more subnets later:

```bicep
// Step 1: Deploy new VNET
module newVnet 'deploy-vnet.bicep' = {
  name: 'initialVnetDeployment'
  params: {
    baseName: 'myproject'
    subnetConfigs: [
      {
        name: 'initial-subnet'
        addressPrefix: '10.0.0.0/24'
      }
    ]
  }
}

// Step 2: Add more subnets later
module moreSubnets 'add-subnet.bicep' = {
  name: 'additionalSubnets'
  dependsOn: [
    newVnet
  ]
  params: {
    vnetName: newVnet.outputs.vnetName
    subnetConfigs: [
      {
        name: 'second-subnet'
        addressPrefix: '10.0.1.0/24'
      }
    ]
  }
}
```
