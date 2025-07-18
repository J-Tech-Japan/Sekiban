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

2. Register Required Azure Resource Providers

Before deploying, you need to register the required Azure resource providers:

```bash
chmod +x ./register_providers.sh
./register_providers.sh
```

This script registers:
- `Microsoft.App` (for Container Apps)
- `Microsoft.ContainerService` (for Container Apps Environment)  
- `Microsoft.OperationalInsights` (for Log Analytics)
- `Microsoft.ServiceBus` (for Service Bus - required for Dapr pub/sub)

**Important for Dapr deployment:**
- This template uses **Cosmos DB** as the Dapr actor state store (supports transactions)
- **Service Bus** is used for Dapr pub/sub messaging
- **Azure Storage** is NOT used (replaced with Cosmos DB + Service Bus for better Dapr compatibility)

3. Create Setting File

create your deploy file as mydeploy.local.json
Use only lower case and '-' and number in your resource group name
```json
{
    "resourceGroupName": "your-resource-888",
    "location": "japaneast",
    "backendRelativePath": "../../DaprSekiban.ApiService",
    "frontendRelativePath": "../../DaprSekiban.Web",
    "eventRelayRelativePath": "../../DaprSekiban.EventRelay",
    "logincommand": "az login --tenant yourorg.onmicrosoft.com --use-device-code"
}
```

remember file name 'mydeploy' and use in following step

4. Create Resource Group

You might need to install jq library to your environment.

https://jqlang.org/download/


```bash
# create resource group
chmod +x ./create_resource_group.sh
./create_resource_group.sh mydeploy   
```

5. Purge Key Vault (if needed)

If you've previously deleted a Key Vault with the same name and are encountering issues recreating it, you may need to purge the soft-deleted Key Vault first:

```bash
chmod +x ./purge_keyvault.sh
./purge_keyvault.sh mydeploy
```

Note: This script should only be used when a Key Vault has been deleted but is still in a "soft-deleted" state, preventing you from creating a new Key Vault with the same name.

6. Create Log analytics workspace
```bash
chmod +x ./create_log-analytics.sh
./create_log-analytics.sh mydeploy
```

7. Create Container Registry
```bash
chmod +x ./create_container_registry.sh
./create_container_registry.sh mydeploy
```

8. Deploy Infrastructure
```bash
chmod +x ./runbicep.sh
./runbicep.sh mydeploy aca_infrastructure.bicep
```

This deploys:
- Key Vault
- Cosmos DB (for Sekiban Event Store and Dapr Actor State Store)
- Service Bus (for Dapr Pub/Sub messaging)
- Virtual Network
- Application Insights & Log Analytics
- Container Apps Environment (Managed Environment)
- Dapr Components (Cosmos DB State Store + Service Bus Pub/Sub) with scopes for both API and EventRelay

**Note:** This deployment may take 5-10 minutes as Container Apps Environment creation can take time.

9. Deploy backend image to ACR
```bash
chmod +x ./code_deploy_backend.sh
./code_deploy_backend.sh mydeploy   
```

**Note:** On first deployment, you may see an error "The containerapp 'backend-xxx' does not exist". This is expected as the Container App hasn't been created yet. The script will build and push the image to ACR successfully, but the automatic container update will fail. This is normal for initial deployment.

10. Deploy frontend image to ACR
```bash
chmod +x ./code_deploy_frontend.sh
./code_deploy_frontend.sh mydeploy   
```

**Note:** Similar to the backend, you may see an error "The containerapp 'frontend-xxx' does not exist" on first deployment. This is expected and normal. The image will be successfully pushed to ACR.

11. Deploy EventRelay image to ACR
```bash
chmod +x ./code_deploy_eventrelay.sh
./code_deploy_eventrelay.sh mydeploy   
```

**Note:** Similar to other apps, you may see an error "The containerapp 'eventrelay-xxx' does not exist" on first deployment. This is expected and normal. The image will be successfully pushed to ACR.

12. Deploy Container Apps

After infrastructure and images are ready, deploy the Container Apps:

```bash
./runbicep.sh mydeploy aca_apps.bicep
```

This creates:
- Backend Container App (internal access only, with Dapr enabled)
- EventRelay Container App (internal access only, with Dapr enabled)
- Frontend Container App (public access)

**Important Notes:**
- The backend is configured with internal access only for security
- The backend uses Dapr app ID: `daprsekiban-apiservice`
- The EventRelay is configured with internal access only and uses Dapr app ID: `daprsekiban-eventrelay`
- The EventRelay automatically scales based on Service Bus message volume (min: 1, max: 5 replicas)
- The frontend communicates with backend via internal URL
- Dapr components are scoped to both backend and EventRelay apps, allowing them to share state store and pub/sub

13. Give yourself access to KeyVault (optional)

```bash
chmod +x ./user_access_keyvault.sh
./user_access_keyvault.sh mydeploy   
```

14. Updating Deployed Applications

After initial deployment, you can update your applications easily:

```bash
# Update backend
./code_deploy_backend.sh mydeploy

# Update EventRelay
./code_deploy_eventrelay.sh mydeploy

# Update frontend  
./code_deploy_frontend.sh mydeploy
```

These scripts will:
- Build new Docker images
- Push to ACR
- Automatically update the running Container Apps

15. Setup Github Actions (Optional) - Create Azure Credentials

```bash
chmod +x ./generate_azure_credentials.sh
./generate_azure_credentials.sh mydeploy   
```

json will print on the screen, you will keep that json as AZURE_CREDENTIALS_MYDEPLOY in github secrets.

16. Setup Github Actions

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
      - "DaprSekiban.ApiService/**"
      - "DaprSekiban.Domain/**"
      - "DaprSekiban.ServiceDefaults/**"
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
          file: DaprSekiban.ApiService/Dockerfile

      - name: Update ACA image
        run: |
          az containerapp update \
            --name ${{ env.BACKEND_IMAGE_NAME }} \
            --resource-group ${{ env.RESOUCE_GROUP_NAME }} \
            --image ${{ secrets.ACR_LOGIN_SERVER }}/${{ env.BACKEND_IMAGE_NAME }}:${{ github.sha }}
```

deploy-eventrelay.yml
```yml
name: "Deploy EventRelay"

on:
  workflow_dispatch:
  push:
    branches:
      - main
    paths:
      - "DaprSekiban.EventRelay/**"
      - "DaprSekiban.Domain/**"
      - "DaprSekiban.ServiceDefaults/**"
env:
  DOTNET_VERSION: 9.0.x
  RESOUCE_GROUP_NAME: <resouce_group_name>
  EVENTRELAY_IMAGE_NAME: eventrelay-${{ env.RESOUCE_GROUP_NAME }}

jobs:
  deploy-eventrelay:
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

      - name: Build and push EventRelay image to registry
        uses: docker/build-push-action@v2
        with:
          push: true
          tags: ${{ secrets.ACR_LOGIN_SERVER }}/${{ env.EVENTRELAY_IMAGE_NAME }}:${{ github.sha }}
          file: DaprSekiban.EventRelay/Dockerfile

      - name: Update ACA image
        run: |
          az containerapp update \
            --name ${{ env.EVENTRELAY_IMAGE_NAME }} \
            --resource-group ${{ env.RESOUCE_GROUP_NAME }} \
            --image ${{ secrets.ACR_LOGIN_SERVER }}/${{ env.EVENTRELAY_IMAGE_NAME }}:${{ github.sha }}
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
      - "DaprSekiban.Web/**"
      - "DaprSekiban.Domain/**"
      - "DaprSekiban.ServiceDefaults/**"
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
          file: DaprSekiban.Web/Dockerfile

      - name: Update ACA image
        run: |
          az containerapp update \
            --name ${{ env.FRONTEND_IMAGE_NAME }} \
            --resource-group ${{ env.RESOUCE_GROUP_NAME }} \
            --image ${{ secrets.ACR_LOGIN_SERVER }}/${{ env.FRONTEND_IMAGE_NAME }}:${{ github.sha }}
```

## Troubleshooting

### Common Issues

1. **Dapr state store error: "the state store is not configured to use the actor runtime"**
   - This occurs when Dapr components are not properly loaded
   - Solution: Ensure the app IDs match the Dapr component scopes (`daprsekiban-apiservice` for backend, `daprsekiban-eventrelay` for EventRelay)
   - You may need to restart the container apps

2. **Frontend cannot connect to backend**
   - Check that the backend is using internal ingress (not external)
   - Verify the frontend environment variable points to the internal URL
   - The internal URL format should be: `https://backend-{resource-group}.internal.{environment-domain}.azurecontainerapps.io`

3. **Container Apps Environment takes long to create**
   - This is normal and can take 5-10 minutes
   - Be patient during the infrastructure deployment step

4. **"Container app does not exist" errors during first deployment**
   - This is expected behavior
   - The scripts build and push images first, then try to update non-existent apps
   - Simply continue with the deployment steps

5. **Key Vault already exists error**
   - Use the purge_keyvault.sh script to remove soft-deleted vaults
   - Key Vaults have a 90-day retention period by default

6. **EventRelay not processing messages**
   - Check that EventRelay is running with at least 1 replica
   - Verify Service Bus topic `events.all` exists
   - Check EventRelay logs for subscription errors
   - Ensure EventRelay Dapr app ID is included in pub/sub component scopes

### Viewing Logs

```bash
# View backend logs
az containerapp logs show --name backend-{resource-group} --resource-group {resource-group} --tail 50

# View backend Dapr sidecar logs
az containerapp logs show --name backend-{resource-group} --resource-group {resource-group} --container daprd --tail 50

# View EventRelay logs
az containerapp logs show --name eventrelay-{resource-group} --resource-group {resource-group} --tail 50

# View EventRelay Dapr sidecar logs
az containerapp logs show --name eventrelay-{resource-group} --resource-group {resource-group} --container daprd --tail 50

# View frontend logs  
az containerapp logs show --name frontend-{resource-group} --resource-group {resource-group} --tail 50
```

### Accessing Your Application

After successful deployment:
- Frontend URL: `https://frontend-{resource-group}.{region}.azurecontainerapps.io`
- Backend is internal only and not directly accessible from internet
- EventRelay is internal only and processes events automatically in the background
- Navigate to the frontend URL and click on "Weather" to test the application

### Architecture Overview

The deployed solution consists of:
1. **Frontend (Blazor)** - Public facing web application
2. **Backend API** - Internal Dapr-enabled API handling commands and queries
3. **EventRelay** - Internal Dapr-enabled service processing events and updating projections
4. **Shared Infrastructure**:
   - Single Container Apps Environment with Dapr enabled
   - Cosmos DB for event store and actor state
   - Service Bus for pub/sub messaging
   - All services share the same Dapr components with proper scoping
