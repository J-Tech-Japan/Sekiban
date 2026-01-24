#!/bin/bash
# Initialize Orleans schema in RDS PostgreSQL
# Usage: ./init-orleans-schema.sh <env> [bastion-host]
#
# This script downloads Orleans PostgreSQL scripts and runs them against RDS.
# If a bastion host is provided, it tunnels through SSH.

set -e

ENV=${1:-dev}
BASTION_HOST=$2
REGION="ap-northeast-1"

echo "Initializing Orleans schema for environment: $ENV"

# Get RDS endpoint from CloudFormation outputs
STACK_NAME="SekibanDynamoDB-${ENV}"
RDS_ENDPOINT=$(aws cloudformation describe-stacks \
    --stack-name "$STACK_NAME" \
    --query "Stacks[0].Outputs[?OutputKey=='RdsEndpoint'].OutputValue" \
    --output text \
    --region "$REGION")

if [ -z "$RDS_ENDPOINT" ]; then
    echo "Error: Could not find RDS endpoint for stack $STACK_NAME"
    exit 1
fi

echo "RDS Endpoint: $RDS_ENDPOINT"

# Get RDS credentials from Secrets Manager
SECRET_NAME="sekiban-dynamodb-${ENV}-rds"
SECRET_JSON=$(aws secretsmanager get-secret-value \
    --secret-id "$SECRET_NAME" \
    --query 'SecretString' \
    --output text \
    --region "$REGION")

DB_HOST=$(echo "$SECRET_JSON" | jq -r '.host')
DB_PORT=$(echo "$SECRET_JSON" | jq -r '.port')
DB_NAME=$(echo "$SECRET_JSON" | jq -r '.dbname')
DB_USER=$(echo "$SECRET_JSON" | jq -r '.username')
DB_PASS=$(echo "$SECRET_JSON" | jq -r '.password')

# Create temp directory for SQL scripts
SCRIPT_DIR=$(mktemp -d)
trap "rm -rf $SCRIPT_DIR" EXIT

echo "Downloading Orleans PostgreSQL scripts..."

# Download Orleans SQL scripts from GitHub
ORLEANS_SQL_BASE="https://raw.githubusercontent.com/dotnet/orleans/main/src/AdoNet/Shared/PostgreSQL"
curl -sL "$ORLEANS_SQL_BASE/PostgreSQL-Main.sql" -o "$SCRIPT_DIR/1-Main.sql"
curl -sL "$ORLEANS_SQL_BASE/PostgreSQL-Persistence.sql" -o "$SCRIPT_DIR/2-Persistence.sql"
curl -sL "$ORLEANS_SQL_BASE/PostgreSQL-Reminders.sql" -o "$SCRIPT_DIR/3-Reminders.sql"

echo "Downloaded SQL scripts to $SCRIPT_DIR"

# Combine all scripts
cat "$SCRIPT_DIR/1-Main.sql" "$SCRIPT_DIR/2-Persistence.sql" "$SCRIPT_DIR/3-Reminders.sql" > "$SCRIPT_DIR/init-all.sql"

# Set PostgreSQL password environment variable
export PGPASSWORD="$DB_PASS"

if [ -n "$BASTION_HOST" ]; then
    echo "Using SSH tunnel through bastion host: $BASTION_HOST"

    # Set up SSH tunnel
    LOCAL_PORT=15432
    ssh -f -N -L "$LOCAL_PORT:$DB_HOST:$DB_PORT" "$BASTION_HOST"
    SSH_PID=$!
    trap "kill $SSH_PID 2>/dev/null; rm -rf $SCRIPT_DIR" EXIT

    sleep 2

    # Run SQL through tunnel
    echo "Running Orleans schema initialization..."
    psql -h localhost -p "$LOCAL_PORT" -U "$DB_USER" -d "$DB_NAME" -f "$SCRIPT_DIR/init-all.sql"
else
    echo "Connecting directly to RDS (requires network access)..."
    echo "If this fails, ensure you have VPN/Direct Connect or use a bastion host."

    # Try direct connection (works if running from within VPC or with proper network setup)
    psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -f "$SCRIPT_DIR/init-all.sql"
fi

echo ""
echo "Orleans schema initialization completed successfully!"
echo ""
echo "Tables created:"
echo "  - OrleansMembershipTable (clustering)"
echo "  - OrleansMembershipVersionTable (clustering)"
echo "  - OrleansQuery (stored procedures)"
echo "  - OrleansStorage (grain persistence)"
echo "  - OrleansRemindersTable (reminders)"
