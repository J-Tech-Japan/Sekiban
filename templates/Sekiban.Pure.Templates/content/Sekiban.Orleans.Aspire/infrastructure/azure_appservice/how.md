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

2. Create Setting File

create your deploy file as mydeploy.local.json
Use only lower case and '-' and number in your resource group name
```json
{
    "resourceGroupName": "your-resource-888",
    "location": "japaneast",
    "backendRelativePath": "../../OrleansSekiban.ApiService",
    "frontendRelativePath": "../../OrleansSekiban.Web",
    "logincommand": "az login --tenant yourorg.onmicrosoft.com --use-device-code"
}
```

remember file name 'mydeploy' and use in following step

3. Create Resource Group

You might need to install jq library to your environment.

https://jqlang.org/download/


```bash
# create resource group
chmod +x ./create_resource_group.sh
./create_resource_group.sh mydeploy   
```

4. Purge Key Vault (if needed)

If you've previously deleted a Key Vault with the same name and are encountering issues recreating it, you may need to purge the soft-deleted Key Vault first:

```bash
chmod +x ./purge_keyvault.sh
./purge_keyvault.sh mydeploy
```

Note: This script should only be used when a Key Vault has been deleted but is still in a "soft-deleted" state, preventing you from creating a new Key Vault with the same name.

5. Deploy bicep file (all or each)

a. Deploy All

```bash

chmod +x ./runbicep.sh
./runbicep.sh mydeploy main.bicep
```

b. Deploy Each Bicep

```bash

chmod +x ./runbicep.sh
./runbicep mydeploy bicep_you_want
# for example
./runbicep mydeploy 1.keyvault/create.bicep

```

6. Give yourself access to KeyVault (optional)

```bash
chmod +x ./user_access_keyvault.sh
./user_access_keyvault.sh mydeploy   
```

7. Deploy Backend Code

You might need to install zip command. e.g. `choco install zip`

```bash
chmod +x ./code_deploy_backend.sh
./code_deploy_backend.sh mydeploy   
```


8. Deploy Frontend Code

```bash
chmod +x ./code_deploy_frontend.sh
./code_deploy_frontend.sh mydeploy   
```

9. Setup Github Actions (Optional) - Create Azure Credentials

```bash
chmod +x ./generate_azure_credentials.sh
./generate_azure_credentials.sh mydeploy   
```

json will print on the screen, you will keep that json as AZURE_CREDENTIALS_MYDEPLOY in github secrets.


10. Setup Github Actions

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
      - "src/OrleansSekiban/**"
env:
  DOTNET_VERSION: 9.0.x

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
          
      - name: Deploy Municipality Resources
        run: |
          chmod +x src/deploy/code_deploy_backend.sh
          src/deploy/code_deploy_backend mydeploy
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
      - "src/OrleansSekiban/**"
env:
  DOTNET_VERSION: 9.0.x

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
          
      - name: Deploy Municipality Resources
        run: |
          chmod +x src/deploy/code_deploy_frontend.sh
          src/deploy/code_deploy_frontend mydeploy
```

