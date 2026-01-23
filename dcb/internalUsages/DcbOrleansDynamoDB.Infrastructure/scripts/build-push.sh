#!/bin/bash
# ============================================================================
# Build and Push Container Images to ECR
# ============================================================================
# Usage: ./build-push.sh [dev|prod] [api|web|all] [tag]
# ============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INFRA_DIR="$(dirname "$SCRIPT_DIR")"
# Navigate to the root of the dcb folder (where Dockerfile should be or ApiService is)
DCB_ROOT="$(dirname "$(dirname "$INFRA_DIR")")"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

ENV="${1:-dev}"
SERVICE="${2:-all}"
TAG="${3:-latest}"

echo "Environment: ${ENV}"
echo "Service: ${SERVICE}"
echo "Tag: ${TAG}"
echo ""

# Get configuration
REGION=$(jq -r '.region' "$INFRA_DIR/config/${ENV}.json")
ACCOUNT=$(aws sts get-caller-identity --query 'Account' --output text)
ECR_URI="${ACCOUNT}.dkr.ecr.${REGION}.amazonaws.com"

echo -e "${YELLOW}Logging in to ECR...${NC}"
aws ecr get-login-password --region "$REGION" | docker login --username AWS --password-stdin "$ECR_URI"

build_and_push() {
    local service_name=$1
    local dockerfile=$2
    local repo_name=$3

    echo -e "${YELLOW}Building ${service_name} container image...${NC}"
    docker buildx build \
        --platform linux/arm64 \
        --tag "${ECR_URI}/${repo_name}:${TAG}" \
        --tag "${ECR_URI}/${repo_name}:$(git rev-parse --short HEAD)" \
        --push \
        -f "$dockerfile" \
        "$DCB_ROOT"

    echo -e "${GREEN}Image pushed: ${ECR_URI}/${repo_name}:${TAG}${NC}"
}

if [ "$SERVICE" = "api" ] || [ "$SERVICE" = "all" ]; then
    build_and_push "API" \
        "$DCB_ROOT/internalUsages/DcbOrleansDynamoDB.WithoutResult.ApiService/Dockerfile" \
        "sekiban-dynamodb-api-${ENV}"
fi

if [ "$SERVICE" = "web" ] || [ "$SERVICE" = "all" ]; then
    build_and_push "Web" \
        "$DCB_ROOT/internalUsages/DcbOrleans.Web/Dockerfile" \
        "sekiban-dynamodb-web-${ENV}"
fi

echo -e "${GREEN}Done!${NC}"
