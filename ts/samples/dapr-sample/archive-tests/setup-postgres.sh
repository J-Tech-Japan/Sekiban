#!/bin/bash

echo "Setting up PostgreSQL for Sekiban..."
echo "===================================="

# Check if PostgreSQL is running
if ! pg_isready -h localhost -p 5432 > /dev/null 2>&1; then
    echo "PostgreSQL is not running. Please start PostgreSQL first."
    echo "On macOS with Homebrew: brew services start postgresql"
    exit 1
fi

echo "PostgreSQL is running."

# Create database and user
echo "Creating database and user..."

# Note: This assumes you can connect as the postgres superuser
# You may need to adjust based on your PostgreSQL setup
psql -h localhost -p 5432 -U postgres <<EOF
-- Create user if not exists
DO \$\$
BEGIN
   IF NOT EXISTS (SELECT FROM pg_catalog.pg_user WHERE usename = 'sekiban') THEN
      CREATE USER sekiban WITH PASSWORD 'sekiban_password';
   END IF;
END
\$\$;

-- Create database if not exists
SELECT 'CREATE DATABASE sekiban_events OWNER sekiban'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'sekiban_events')\gexec

-- Grant all privileges
GRANT ALL PRIVILEGES ON DATABASE sekiban_events TO sekiban;
EOF

echo ""
echo "Setup complete!"
echo "Database: sekiban_events"
echo "User: sekiban"
echo "Password: sekiban_password"