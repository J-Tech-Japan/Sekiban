# Azure Login 

```bash
# Login by specifying tenant ID
az login --tenant <tenant-id>

az login --tenant smartresourcejp.onmicrosoft.com

# Or login using organization domain name
az login --tenant contoso.onmicrosoft.com

# When multiple accounts exist with the same username, use the --use-device-code option
az login --tenant <tenant-id> --use-device-code
```



```bash
# create resource group
az group create --name myResourceGroup --location eastus

# command to use bicep and specify resource group
az deployment group create \
  --resource-group myResourceGroup \
  --template-file main.bicep \
  --parameters @parameters.json

# You can also specify parameters inline
az deployment group create \
  --resource-group myResourceGroup \
  --template-file main.bicep \
  --parameters appServicePlanName=myAppServicePlan webAppName=myWebApp
```

```bash

# command to use bicep and specify resource group
az deployment group create \
  --resource-group myResourceGroup \
  --template-file main.bicep

```