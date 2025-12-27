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

1. Deploy frontend image to ACR

```bash
chmod +x ./code_deploy_frontend.sh

# (A) Specify environment name
./code_deploy_frontend.sh mydeploy

# (B) Specify .local.json path
./code_deploy_frontend.sh ./mydeploy.local.json
```

1. Deploy bicep file (all or each)

a. Deploy All

```bash
chmod +x ./runbicep.sh

# (A) Specify environment name
./runbicep.sh mydeploy aca_main.bicep

# (B) Specify .local.json path
./runbicep.sh ./mydeploy.local.json aca_main.bicep
```

b. Deploy Each Bicep

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
