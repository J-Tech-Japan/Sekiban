# Azure Container Apps Deployment Guide

## Important: Deployment Method

**RECOMMENDED: Use `aca_main.bicep` for all deployments.**

The `aca_main.bicep` file orchestrates all infrastructure modules and ensures parameters (such as Orleans clustering type, grain storage type, and queue type) are correctly passed to all dependent modules.

**Individual Bicep files** (e.g., `7.backend/2.container-app.bicep`) may not work correctly when deployed standalone because they rely on parameters that are passed from `aca_main.bicep`. If you must deploy individual modules, you need to manually specify all required parameters.

### Orleans Configuration Parameters

The following parameters control Orleans clustering and storage behavior:

| Parameter | Values | Default | Description |
|-----------|--------|---------|-------------|
| `orleansClusterType` | `cosmos`, `azuretable` | `cosmos` | Orleans cluster membership storage |
| `orleansDefaultGrainType` | `cosmos`, `blob` | `cosmos` | Orleans grain state storage |
| `orleansQueueType` | `eventhub`, `azurestorage` | `eventhub` | Orleans streaming queue |

To deploy with custom parameters:

```bash
# Using az deployment directly with parameters
az deployment group create \
  --resource-group <your-resource-group> \
  --template-file aca_main.bicep \
  --parameters logAnalyticsSharedKey=<key> \
               orleansClusterType=cosmos \
               orleansDefaultGrainType=cosmos \
               orleansQueueType=eventhub
```

---

# Azure Login

1. First you need to login with az login for your target Azure Tenant.

```bash
# Login by specifying tenant ID
az login --tenant <tenant-id>

# Or login using organization domain name
az login --tenant contoso.onmicrosoft.com

# When multiple accounts exist with the same username, use the --use-device-code option
az login --tenant <tenant-id> --use-device-code
```

1. Create Setting File

create your deploy file as mydeploy.local.json
Use only lower case and '-' and number in your resource group name
```json
{
    "resourceGroupName": "your-resource-888",
    "location": "japaneast",
    "backendRelativePath": "../../MyProject.ApiService",
    "frontendRelativePath": "../../MyProject.Web",
    "logincommand": "az login --tenant yourorg.onmicrosoft.com --use-device-code"
}
```

remember file name 'mydeploy' and use in following step

You can now pass either of the following as the first argument to all scripts:

- the environment name (e.g., `mydeploy`) which implies `mydeploy.local.json` in the current directory
- the full or relative path to the `.local.json` file (e.g., `./mydeploy.local.json` or `/full/path/to/mydeploy.local.json`)

1. Create Resource Group

You might need to install jq library to your environment.

<https://jqlang.org/download/>


```bash
# create resource group
chmod +x ./create_resource_group.sh

# (A) Specify environment name
./create_resource_group.sh mydeploy

# (B) Specify .local.json path
./create_resource_group.sh ./mydeploy.local.json
```

1. Purge Key Vault (if needed)

If you've previously deleted a Key Vault with the same name and are encountering issues recreating it, you may need to purge the soft-deleted Key Vault first:

```bash
chmod +x ./purge_keyvault.sh

# (A) Specify environment name
./purge_keyvault.sh mydeploy

# (B) Specify .local.json path
./purge_keyvault.sh ./mydeploy.local.json
```

Note: This script should only be used when a Key Vault has been deleted but is still in a "soft-deleted" state, preventing you from creating a new Key Vault with the same name.

1. Create Log analytics workspace

```bash
chmod +x ./create_log-analytics.sh

# (A) Specify environment name
./create_log-analytics.sh mydeploy

# (B) Specify .local.json path
./create_log-analytics.sh ./mydeploy.local.json
```

1. Create Container Registry

```bash
chmod +x ./create_container_registry.sh

# (A) Specify environment name
./create_container_registry.sh mydeploy

# (B) Specify .local.json path
./create_container_registry.sh ./mydeploy.local.json
```

1. Deploy backend image to ACR

```bash
chmod +x ./code_deploy_backend.sh

# (A) Specify environment name
./code_deploy_backend.sh mydeploy

# (B) Specify .local.json path
./code_deploy_backend.sh ./mydeploy.local.json
```

1. Deploy frontend (Blazor) image to ACR

```bash
chmod +x ./code_deploy_frontend.sh

# (A) Specify environment name
./code_deploy_frontend.sh mydeploy

# (B) Specify .local.json path
./code_deploy_frontend.sh ./mydeploy.local.json
```

1. Deploy WebNext (Next.js) image to ACR

```bash
chmod +x ./code_deploy_webnext.sh

# (A) Specify environment name
./code_deploy_webnext.sh mydeploy

# (B) Specify .local.json path
./code_deploy_webnext.sh ./mydeploy.local.json
```

Note: You can optionally add `webnextRelativePath` to your `.local.json` file to customize the path:
```json
{
    "resourceGroupName": "your-resource-888",
    "location": "japaneast",
    "backendRelativePath": "../../MyProject.ApiService",
    "frontendRelativePath": "../../MyProject.Web",
    "webnextRelativePath": "../../MyProject.WebNext",
    "logincommand": "az login --tenant yourorg.onmicrosoft.com --use-device-code"
}
```

1. Deploy bicep file (all or each)

a. Deploy All (RECOMMENDED)

**Important:** Always use `aca_main.bicep` for complete deployments. This ensures all Orleans clustering, grain storage, and queue parameters are correctly propagated to all modules.

```bash
chmod +x ./runbicep.sh

# (A) Specify environment name (uses default parameters)
./runbicep.sh mydeploy aca_main.bicep

# (B) Specify .local.json path
./runbicep.sh ./mydeploy.local.json aca_main.bicep
```

To customize Orleans settings, use `az deployment` directly:

```bash
# Get the Log Analytics shared key first
RESOURCE_GROUP="your-resource-group"
SHARED_KEY=$(az monitor log-analytics workspace get-shared-keys \
  --resource-group $RESOURCE_GROUP \
  --workspace-name law-$RESOURCE_GROUP \
  --query primarySharedKey -o tsv)

# Deploy with custom Orleans parameters
az deployment group create \
  --resource-group $RESOURCE_GROUP \
  --template-file aca_main.bicep \
  --parameters logAnalyticsSharedKey=$SHARED_KEY \
               orleansClusterType=cosmos \
               orleansDefaultGrainType=cosmos \
               orleansQueueType=eventhub
```

b. Deploy Each Bicep (NOT RECOMMENDED for production)

**Warning:** Individual Bicep files may not work correctly without the proper parameters that `aca_main.bicep` provides. Only use this for debugging or when you understand the parameter dependencies.

```bash
chmod +x ./runbicep.sh

# (A) Specify environment name
./runbicep.sh mydeploy bicep_you_want

# (B) Specify .local.json path
./runbicep.sh ./mydeploy.local.json bicep_you_want

# for example
./runbicep.sh mydeploy 1.keyvault/create.bicep
./runbicep.sh ./mydeploy.local.json 1.keyvault/create.bicep
```

For backend container app with custom Orleans settings:

```bash
az deployment group create \
  --resource-group $RESOURCE_GROUP \
  --template-file 7.backend/2.container-app.bicep \
  --parameters orleansClusterType=cosmos \
               orleansDefaultGrainType=cosmos \
               orleansQueueType=eventhub
```

1. Give yourself access to KeyVault (optional)

```bash
chmod +x ./user_access_keyvault.sh

# (A) Specify environment name
./user_access_keyvault.sh mydeploy

# (B) Specify .local.json path
./user_access_keyvault.sh ./mydeploy.local.json
```

1. Setup Github Actions (Optional) - Create Azure Credentials

```bash
chmod +x ./generate_azure_credentials.sh

# (A) Specify environment name
./generate_azure_credentials.sh mydeploy

# (B) Specify .local.json path
./generate_azure_credentials.sh ./mydeploy.local.json
```

json will print on the screen, you will keep that json as AZURE_CREDENTIALS_MYDEPLOY in github secrets.

1. Setup Github Actions

you need to include mydeploy.local.json to the git
deploy-backend.yml

```yml
name: "Deploy Backend API"

on:
  workflow_dispatch:
  push:
    branches:
      - main
    paths:
      - "src/MyProject/**"
env:
  DOTNET_VERSION: 9.0.x
  RESOUCE_GROUP_NAME: <resouce_group_name>
  BACKEND_IMAGE_NAME: backend-${{ env.RESOUCE_GROUP_NAME }}

jobs:
  deploy-municipality:
    runs-on: ubuntu-latest
    
    steps:
      - uses: actions/checkout@v3
      
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Azure Login
        uses: azure/login@v1
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS_MYDEPLOY }}

      - name: Prepare Docker buildx
        uses: docker/setup-buildx-action@v1

      - name: Login to ACR
        run: |
          access_token=$(az account get-access-token --query accessToken -o tsv)
          refresh_token=$(curl https://${{ secrets.ACR_LOGIN_SERVER }}/oauth2/exchange -v \
          -d "grant_type=access_token&service=${{ secrets.ACR_LOGIN_SERVER }}&access_token=$access_token" | jq -r .refresh_token)
          # The null GUID 0000... tells the container registry that this is an ACR refresh token during the login flow
          docker login -u 00000000-0000-0000-0000-000000000000 \
          --password-stdin ${{ secrets.ACR_LOGIN_SERVER }} <<< "$refresh_token"

      - name: Build and push Silo image to registry
        uses: docker/build-push-action@v2
        with:
          push: true
          tags: ${{ secrets.ACR_LOGIN_SERVER }}/${{ env.BACKEND_IMAGE_NAME }}:${{ github.sha }}
          file: MyProject.ApiService/Dockerfile

      - name: Update ACA image
        run: |
          az containerapp update \
            --name ${{ env.BACKEND_IMAGE_NAME }} \
            --resource-group ${{ env.RESOUCE_GROUP_NAME }} \
            --image ${{ secrets.ACR_LOGIN_SERVER }}/${{ env.BACKEND_IMAGE_NAME }}:${{ github.sha }}
```

deploy-frontend.yml

```yml
name: "Deploy Frontend"

on:
  workflow_dispatch:
  push:
    branches:
      - main
    paths:
      - "src/MyProject/**"
env:
  DOTNET_VERSION: 9.0.x
  RESOUCE_GROUP_NAME: <resouce_group_name>
  FRONTEND_IMAGE_NAME: frontend-${{ env.RESOUCE_GROUP_NAME }}

jobs:
  deploy-municipality:
    runs-on: ubuntu-latest
    
    steps:
      - uses: actions/checkout@v3
      
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Azure Login
        uses: azure/login@v1
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS_MYDEPLOY }}

      - name: Prepare Docker buildx
              uses: docker/setup-buildx-action@v1

      - name: Login to ACR
        run: |
          access_token=$(az account get-access-token --query accessToken -o tsv)
          refresh_token=$(curl https://${{ secrets.ACR_LOGIN_SERVER }}/oauth2/exchange -v \
          -d "grant_type=access_token&service=${{ secrets.ACR_LOGIN_SERVER }}&access_token=$access_token" | jq -r .refresh_token)
          # The null GUID 0000... tells the container registry that this is an ACR refresh token during the login flow
          docker login -u 00000000-0000-0000-0000-000000000000 \
          --password-stdin ${{ secrets.ACR_LOGIN_SERVER }} <<< "$refresh_token"

      - name: Build and push Silo image to registry
        uses: docker/build-push-action@v2
        with:
          push: true
          tags: ${{ secrets.ACR_LOGIN_SERVER }}/${{ env.FRONTEND_IMAGE_NAME }}:${{ github.sha }}
          file: MyProject.Web/Dockerfile

      - name: Update ACA image
        run: |
          az containerapp update \
            --name ${{ env.FRONTEND_IMAGE_NAME }} \
            --resource-group ${{ env.RESOUCE_GROUP_NAME }} \
            --image ${{ secrets.ACR_LOGIN_SERVER }}/${{ env.FRONTEND_IMAGE_NAME }}:${{ github.sha }}
```

deploy-webnext.yml (Next.js Frontend)

```yml
name: "Deploy WebNext (Next.js)"

on:
  workflow_dispatch:
  push:
    branches:
      - main
    paths:
      - "src/MyProject.WebNext/**"
env:
  NODE_VERSION: 22.x
  RESOUCE_GROUP_NAME: <resouce_group_name>
  WEBNEXT_IMAGE_NAME: webnext-${{ env.RESOUCE_GROUP_NAME }}

jobs:
  deploy-webnext:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v3

      - uses: actions/setup-node@v4
        with:
          node-version: ${{ env.NODE_VERSION }}

      - name: Azure Login
        uses: azure/login@v1
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS_MYDEPLOY }}

      - name: Prepare Docker buildx
        uses: docker/setup-buildx-action@v1

      - name: Login to ACR
        run: |
          access_token=$(az account get-access-token --query accessToken -o tsv)
          refresh_token=$(curl https://${{ secrets.ACR_LOGIN_SERVER }}/oauth2/exchange -v \
          -d "grant_type=access_token&service=${{ secrets.ACR_LOGIN_SERVER }}&access_token=$access_token" | jq -r .refresh_token)
          # The null GUID 0000... tells the container registry that this is an ACR refresh token during the login flow
          docker login -u 00000000-0000-0000-0000-000000000000 \
          --password-stdin ${{ secrets.ACR_LOGIN_SERVER }} <<< "$refresh_token"

      - name: Build and push Next.js image to registry
        uses: docker/build-push-action@v2
        with:
          push: true
          tags: ${{ secrets.ACR_LOGIN_SERVER }}/${{ env.WEBNEXT_IMAGE_NAME }}:${{ github.sha }}
          file: MyProject.WebNext/Dockerfile

      - name: Update ACA image
        run: |
          az containerapp update \
            --name ${{ env.WEBNEXT_IMAGE_NAME }} \
            --resource-group ${{ env.RESOUCE_GROUP_NAME }} \
            --image ${{ secrets.ACR_LOGIN_SERVER }}/${{ env.WEBNEXT_IMAGE_NAME }}:${{ github.sha }}
```
