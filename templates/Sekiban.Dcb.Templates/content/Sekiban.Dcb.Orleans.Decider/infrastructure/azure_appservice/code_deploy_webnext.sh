#!/bin/bash

# Deploy WebNext (Next.js) to Azure App Service using standalone output
# parameter $1 is the name of the environment

set -e

# Handle both filename and path inputs
ARG_INPUT="$1"
if [[ "$ARG_INPUT" == *.local.json ]]; then
    BASENAME="${ARG_INPUT##*/}"
    ENVIRONMENT="${BASENAME%.local.json}"
    CONFIG_FILE="$BASENAME"
    CONFIG_PATH="$ARG_INPUT"
else
    ENVIRONMENT="$ARG_INPUT"
    CONFIG_FILE="${ENVIRONMENT}.local.json"
    CONFIG_PATH="$CONFIG_FILE"
fi

# Check if config file exists
if [ ! -f "$CONFIG_PATH" ]; then
    echo "Error: Configuration file $CONFIG_PATH not found"
    exit 1
fi

# Get resource group name from config file
RESOURCE_GROUP=$(jq -r '.resourceGroupName' "$CONFIG_PATH")
# Get WebNext relative path from config file (fallback to default if not specified)
WEBNEXT_PATH=$(jq -r '.webnextRelativePath // "../../SekibanDcbDecider.WebNext"' "$CONFIG_PATH")

echo "Resource Group: $RESOURCE_GROUP"
echo "WebNext path: $WEBNEXT_PATH"

# Verify the WebNext path exists
if [ ! -d "$WEBNEXT_PATH" ]; then
    echo "Error: WebNext directory not found at $WEBNEXT_PATH"
    exit 1
fi

# App Service naming
APP_SERVICE_NAME="webnext-${RESOURCE_GROUP}"
echo "App Service Name: $APP_SERVICE_NAME"

# Build the Next.js application
echo "Building Next.js application with standalone output..."
pushd "$WEBNEXT_PATH"

# Install dependencies
echo "Installing dependencies..."
npm install

# Build the application (standalone output)
echo "Building application..."
npm run build

# Prepare deployment directory using standalone output
echo "Preparing standalone deployment..."
DEPLOY_DIR="deploy"
rm -rf "$DEPLOY_DIR"
mkdir -p "$DEPLOY_DIR"

# Copy standalone build
cp -r .next/standalone/* "$DEPLOY_DIR/"

# Copy static files (required for standalone)
mkdir -p "$DEPLOY_DIR/.next/static"
cp -r .next/static/* "$DEPLOY_DIR/.next/static/"

# Copy public folder if exists
if [ -d "public" ]; then
    cp -r public "$DEPLOY_DIR/"
fi

# Create a startup script for standalone mode
cat > "$DEPLOY_DIR/startup.sh" << 'EOF'
#!/bin/bash
cd /home/site/wwwroot
node server.js
EOF
chmod +x "$DEPLOY_DIR/startup.sh"

# Create zip for deployment
echo "Creating deployment zip..."
cd "$DEPLOY_DIR"
zip -r ../deploy.zip .
cd ..

popd

# Disable Oryx build (standalone has all dependencies bundled)
echo "Disabling Oryx build..."
az webapp config appsettings set \
    --resource-group "$RESOURCE_GROUP" \
    --name "$APP_SERVICE_NAME" \
    --settings SCM_DO_BUILD_DURING_DEPLOYMENT=false ENABLE_ORYX_BUILD=false

# Update startup command for the App Service
echo "Configuring App Service startup command..."
az webapp config set \
    --resource-group "$RESOURCE_GROUP" \
    --name "$APP_SERVICE_NAME" \
    --startup-file "node server.js"

# Deploy to Azure App Service using zip deploy
echo "Deploying to Azure App Service..."
az webapp deploy \
    --resource-group "$RESOURCE_GROUP" \
    --name "$APP_SERVICE_NAME" \
    --src-path "$WEBNEXT_PATH/deploy.zip" \
    --type zip \
    --clean true

DEPLOY_RESULT=$?

# Cleanup
rm -rf "$WEBNEXT_PATH/deploy"
rm -f "$WEBNEXT_PATH/deploy.zip"

if [ $DEPLOY_RESULT -eq 0 ]; then
    echo ""
    echo "Deployment completed successfully!"
    echo "WebNext URL: https://${APP_SERVICE_NAME}.azurewebsites.net"
else
    echo "Error: Deployment failed"
    exit 1
fi
