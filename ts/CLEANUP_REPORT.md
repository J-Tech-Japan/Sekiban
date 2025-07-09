# Sekiban TypeScript Cleanup Report

## Files to Clean Up

### 1. Log Files (12 files, ~174KB)
```bash
# In samples/dapr-sample/
dapr-api.log (15KB)
dapr-api-new.log (27KB)
dapr-api-final.log (16KB)
dapr.log (131B)
dapr-demo.log (21KB)

# In samples/dapr-sample/packages/api/
direct-dev.log (8.5KB)
dev.log (8.3KB)
dapr.log (38KB)
server.log (2.1KB)
```

### 2. Misplaced Test Files in samples/dapr-sample/
```bash
check-postgres-events.js
init-postgres.js
test-api.js
test-api.sh
test-command.js
test-dapr-api.js
test-data.json
test-direct-postgres.js
test-setup.ts
test-insert.js (in api package)
test-create-task.sh (in api package)
test-health.sh (in api package)
```

### 3. Package Lock Files (redundant with pnpm)
- Look for any `package-lock.json` files outside node_modules

### 4. Build Artifacts
- `/dist/` directory at root level (if exists)

### 5. Backup Files
- Any `*.bak` files

## Recommended Actions

### Immediate Cleanup
Run the cleanup script:
```bash
cd /Users/tomohisa/dev/GitHub/Sekiban-ts/ts/samples/dapr-sample
./cleanup-files.sh
```

### Organize Test Files
Create proper test structure:
```bash
# Move test files to a test directory
cd /Users/tomohisa/dev/GitHub/Sekiban-ts/ts/samples/dapr-sample
mkdir -p test
mv check-postgres-events.js test/
mv init-postgres.js test/
mv test-*.js test/
mv test-*.sh test/
mv test-*.ts test/
mv test-data.json test/

# Move API test files
mkdir -p packages/api/test
mv packages/api/test-*.js packages/api/test/
mv packages/api/test-*.sh packages/api/test/
```

### Update .gitignore
The .gitignore has been updated with:
- TypeScript build outputs (dist/, *.tsbuildinfo)
- Backup files (*.bak)
- Package lock files (package-lock.json, yarn.lock)
- Environment files (.env, .env.*)
- Build/cache directories (.turbo, .cache, etc.)
- Test coverage (coverage/, .nyc_output/)

## Test File Organization

The colocated test pattern (*.test.ts next to source files) in src/packages/*/src/ appears intentional and follows a common pattern. These should be kept as-is.

## Summary

Total files to clean: ~25 files
Total size to free: ~200KB+ (not including dist/ if present)

After cleanup, the repository will be cleaner and follow TypeScript monorepo best practices.