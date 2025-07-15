# Building the Dapr Sample

## Prerequisites

1. Node.js >= 18
2. pnpm >= 8
3. Docker (for PostgreSQL)

## Build Steps

### 1. Build Core Dependencies

First, build the Sekiban core packages:

```bash
# Build @sekiban/core
cd ../../src/packages/core
pnpm build

# Build @sekiban/postgres 
cd ../postgres
pnpm build

# Optional: Build other packages if needed
cd ../codegen
pnpm build
```

### 2. Return to Sample Directory

```bash
cd ../../../samples/dapr-sample
```

### 3. Install Dependencies

```bash
pnpm install
```

### 4. Build Sample Packages

Due to current TypeScript configuration issues, you can run the sample in development mode without building:

```bash
# Start PostgreSQL
docker-compose up -d postgres

# Run in development mode (without building)
cd packages/api
npx tsx src/server.ts
```

## Alternative: Run Without Type Checking

If you encounter type errors, you can bypass them:

```bash
# In packages/domain
npx tsc --noEmit false --skipLibCheck

# In packages/api  
npx tsc --noEmit false --skipLibCheck

# In packages/workflows
npx tsc --noEmit false --skipLibCheck
```

## Known Issues

1. **Type Definitions**: The `defineCommand` API is expecting additional properties that aren't documented
2. **Missing @sekiban/dapr**: The Dapr integration package doesn't exist yet
3. **Executor Implementation**: The executor setup needs proper implementation

## Development Mode (Recommended)

Instead of building, run directly with tsx:

```bash
# Terminal 1: PostgreSQL
docker-compose up -d postgres

# Terminal 2: API Server
cd packages/api
npx tsx watch src/server.ts
```

This will:
- Auto-reload on changes
- Skip type checking issues
- Allow you to test the API immediately

## Testing the API

```bash
# Health check
curl http://localhost:3000/health

# Create a task (Note: Command execution not yet implemented)
curl -X POST http://localhost:3000/api/tasks \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Test Task",
    "description": "Testing the API"
  }'
```