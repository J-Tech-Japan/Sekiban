#!/bin/bash
# ============================================================================
# Deploy SekibanDcbOrleansAws Infrastructure
# ============================================================================
# Usage: ./deploy.sh [dev|prod]
#
# This script performs a complete deployment:
# 1. Creates ECR repositories (if not exist)
# 2. Builds and pushes container images
# 3. Deploys CDK infrastructure stack
# ============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INFRA_DIR="$(dirname "$SCRIPT_DIR")"
PROJECT_ROOT="$(dirname "$INFRA_DIR")"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m'

ENV="${1:-dev}"

echo -e "${BOLD}${CYAN}============================================${NC}"
echo -e "${BOLD}${CYAN}SekibanDcbOrleansAws Infrastructure Deployment${NC}"
echo -e "${BOLD}${CYAN}============================================${NC}"
echo "Environment: ${ENV}"
echo ""

# Check config file exists
CONFIG_FILE="$INFRA_DIR/config/${ENV}.json"
if [ ! -f "$CONFIG_FILE" ]; then
    echo -e "${RED}Error: Configuration file not found: ${CONFIG_FILE}${NC}"
    echo "Create it by copying from dev.sample.json"
    exit 1
fi

# Check AWS credentials
echo -e "${YELLOW}[1/6] Checking AWS credentials...${NC}"
if ! aws sts get-caller-identity > /dev/null 2>&1; then
    echo -e "${RED}Error: Not authenticated with AWS.${NC}"
    echo "Run 'aws configure' or 'aws sso login' first."
    exit 1
fi
CALLER_ID=$(aws sts get-caller-identity --query 'Arn' --output text)
ACCOUNT=$(aws sts get-caller-identity --query 'Account' --output text)
REGION=$(jq -r '.region' "$CONFIG_FILE")
echo -e "${GREEN}Authenticated as: ${CALLER_ID}${NC}"
echo "Account: ${ACCOUNT}, Region: ${REGION}"
echo ""

# Install dependencies
echo -e "${YELLOW}[2/6] Installing dependencies...${NC}"
cd "$INFRA_DIR"
npm ci
echo ""

# Bootstrap CDK if needed
echo -e "${YELLOW}[3/6] Checking CDK bootstrap...${NC}"
if ! aws cloudformation describe-stacks --stack-name CDKToolkit --region "$REGION" > /dev/null 2>&1; then
    echo -e "${YELLOW}Bootstrapping CDK...${NC}"
    npx cdk bootstrap "aws://${ACCOUNT}/${REGION}"
fi
echo -e "${GREEN}CDK bootstrap ready${NC}"
echo ""

# Create ECR repositories if they don't exist
echo -e "${YELLOW}[4/6] Creating ECR repositories...${NC}"
ECR_URI="${ACCOUNT}.dkr.ecr.${REGION}.amazonaws.com"
API_REPO="sekibandcborleansaws-api-${ENV}"
WEB_REPO="sekibandcborleansaws-web-${ENV}"

create_ecr_repo() {
    local repo_name=$1
    if aws ecr describe-repositories --repository-names "$repo_name" --region "$REGION" > /dev/null 2>&1; then
        echo "  Repository ${repo_name} already exists"
    else
        aws ecr create-repository --repository-name "$repo_name" --region "$REGION" > /dev/null
        echo -e "  ${GREEN}Created repository: ${repo_name}${NC}"
    fi
}

create_ecr_repo "$API_REPO"
create_ecr_repo "$WEB_REPO"
echo ""

# Build and push container images
echo -e "${YELLOW}[5/6] Building and pushing container images...${NC}"
echo "Logging in to ECR..."
aws ecr get-login-password --region "$REGION" | docker login --username AWS --password-stdin "$ECR_URI"

TAG="latest"
GIT_TAG=$(git rev-parse --short HEAD 2>/dev/null || echo "unknown")

build_and_push() {
    local service_name=$1
    local dockerfile=$2
    local repo_name=$3

    echo "Building ${service_name}..."
    docker buildx build \
        --platform linux/arm64 \
        --tag "${ECR_URI}/${repo_name}:${TAG}" \
        --tag "${ECR_URI}/${repo_name}:${GIT_TAG}" \
        --push \
        -f "$dockerfile" \
        "$PROJECT_ROOT"
    echo -e "${GREEN}  Pushed: ${ECR_URI}/${repo_name}:${TAG}${NC}"
}

# Note: You need to create Dockerfiles for your API and Web projects
build_and_push "API" \
    "$PROJECT_ROOT/SekibanDcbOrleansAws.ApiService/Dockerfile" \
    "$API_REPO"

build_and_push "Web" \
    "$PROJECT_ROOT/SekibanDcbOrleansAws.Web/Dockerfile" \
    "$WEB_REPO"
echo ""

# Deploy CDK stack
echo -e "${YELLOW}[6/6] Deploying CDK infrastructure...${NC}"
cd "$INFRA_DIR"
npx cdk deploy -c env="$ENV" --require-approval never

echo ""
echo -e "${BOLD}${GREEN}============================================${NC}"
echo -e "${BOLD}${GREEN}Deployment complete!${NC}"
echo -e "${BOLD}${GREEN}============================================${NC}"
echo ""

# Show outputs
echo -e "${YELLOW}Stack Outputs:${NC}"
aws cloudformation describe-stacks \
    --stack-name "SekibanDcbOrleansAws-${ENV}" \
    --query "Stacks[0].Outputs[*].{Key:OutputKey,Value:OutputValue}" \
    --output table \
    --region "$REGION" 2>/dev/null || echo "Stack outputs not available yet."

echo ""
echo -e "${GREEN}Access your application at the CloudFront URL shown above.${NC}"
echo ""
