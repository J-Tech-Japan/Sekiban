#!/bin/bash
# ============================================================================
# Deploy Sekiban DynamoDB Infrastructure
# ============================================================================
# Usage: ./deploy.sh [dev|prod]
# ============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INFRA_DIR="$(dirname "$SCRIPT_DIR")"
PROJECT_ROOT="$(dirname "$(dirname "$(dirname "$INFRA_DIR")")")"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m'

ENV="${1:-dev}"

echo -e "${BOLD}${CYAN}============================================${NC}"
echo -e "${BOLD}${CYAN}Sekiban DynamoDB Infrastructure Deployment${NC}"
echo -e "${BOLD}${CYAN}============================================${NC}"
echo "Environment: ${ENV}"
echo ""

# Check AWS credentials
echo -e "${YELLOW}Checking AWS credentials...${NC}"
if ! aws sts get-caller-identity > /dev/null 2>&1; then
    echo -e "${RED}Error: Not authenticated with AWS.${NC}"
    echo "Run 'aws configure' or 'aws sso login' first."
    exit 1
fi
CALLER_ID=$(aws sts get-caller-identity --query 'Arn' --output text)
echo -e "${GREEN}Authenticated as: ${CALLER_ID}${NC}"
echo ""

# Install dependencies
echo -e "${YELLOW}Installing dependencies...${NC}"
cd "$INFRA_DIR"
npm ci
echo ""

# Bootstrap CDK if needed
echo -e "${YELLOW}Checking CDK bootstrap...${NC}"
ACCOUNT=$(aws sts get-caller-identity --query 'Account' --output text)
REGION=$(jq -r '.region' "$INFRA_DIR/config/${ENV}.json")

if ! aws cloudformation describe-stacks --stack-name CDKToolkit --region "$REGION" > /dev/null 2>&1; then
    echo -e "${YELLOW}Bootstrapping CDK...${NC}"
    npx cdk bootstrap "aws://${ACCOUNT}/${REGION}"
fi
echo ""

# Synthesize
echo -e "${YELLOW}Synthesizing CloudFormation template...${NC}"
npx cdk synth -c env="$ENV"
echo ""

# Deploy
echo -e "${YELLOW}Deploying infrastructure...${NC}"
read -p "Continue with deployment? [Y/n] " -n 1 -r
echo ""
if [[ ! $REPLY =~ ^[Nn]$ ]]; then
    npx cdk deploy -c env="$ENV" --require-approval broadening
fi

echo ""
echo -e "${BOLD}${GREEN}Deployment complete!${NC}"
echo ""

# Show outputs
echo -e "${YELLOW}Stack Outputs:${NC}"
aws cloudformation describe-stacks \
    --stack-name "SekibanDynamoDB-${ENV}" \
    --query "Stacks[0].Outputs[*].{Key:OutputKey,Value:OutputValue}" \
    --output table \
    --region "$REGION" 2>/dev/null || echo "Stack outputs not available yet."

# Check if Orleans schema needs to be initialized
echo ""
echo -e "${YELLOW}Orleans Schema Setup:${NC}"
echo "If this is a first-time deployment, you need to initialize the Orleans schema in RDS."
echo ""
read -p "Initialize Orleans schema now? [y/N] " -n 1 -r
echo ""
if [[ $REPLY =~ ^[Yy]$ ]]; then
    echo -e "${YELLOW}Initializing Orleans schema...${NC}"
    "$SCRIPT_DIR/init-orleans-schema.sh" "$ENV"
fi

echo ""
echo -e "${BOLD}${GREEN}All done!${NC}"
echo ""
echo "Next steps:"
echo "  1. Build and push container images: ./scripts/build-push.sh $ENV all"
echo "  2. Force ECS service update: aws ecs update-service --cluster sekiban-dynamodb-$ENV --service <service-name> --force-new-deployment"
