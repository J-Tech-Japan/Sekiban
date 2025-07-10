# Sekiban TypeScript Framework - Release File Structure

## Overview

This document outlines the complete file structure for the Sekiban TypeScript framework, including all necessary files for development, testing, and release.

## Complete Directory Structure

```
Sekiban-ts/
├── ts/                                     # TypeScript project root
│   ├── src/                               # Source code
│   │   ├── packages/                      # NPM packages (monorepo)
│   │   │   ├── core/                     # @sekiban/core
│   │   │   │   ├── aggregates/
│   │   │   │   │   ├── types.ts
│   │   │   │   │   ├── projector.ts
│   │   │   │   │   ├── projector.test.ts
│   │   │   │   │   └── index.ts
│   │   │   │   ├── commands/
│   │   │   │   │   ├── types.ts
│   │   │   │   │   ├── handler.ts
│   │   │   │   │   ├── executor.ts
│   │   │   │   │   ├── handler.test.ts
│   │   │   │   │   └── index.ts
│   │   │   │   ├── events/
│   │   │   │   │   ├── types.ts
│   │   │   │   │   ├── store.ts
│   │   │   │   │   ├── stream.ts
│   │   │   │   │   ├── store.test.ts
│   │   │   │   │   └── index.ts
│   │   │   │   ├── queries/
│   │   │   │   │   ├── types.ts
│   │   │   │   │   ├── handler.ts
│   │   │   │   │   ├── projections.ts
│   │   │   │   │   └── index.ts
│   │   │   │   ├── documents/
│   │   │   │   │   ├── partition-keys.ts
│   │   │   │   │   ├── sortable-unique-id.ts
│   │   │   │   │   ├── metadata.ts
│   │   │   │   │   ├── partition-keys.test.ts
│   │   │   │   │   ├── sortable-unique-id.test.ts
│   │   │   │   │   └── index.ts
│   │   │   │   ├── executors/
│   │   │   │   │   ├── types.ts
│   │   │   │   │   ├── in-memory.ts
│   │   │   │   │   ├── base.ts
│   │   │   │   │   ├── in-memory.test.ts
│   │   │   │   │   └── index.ts
│   │   │   │   ├── result/
│   │   │   │   │   ├── neverthrow.ts
│   │   │   │   │   ├── errors.ts
│   │   │   │   │   ├── errors.test.ts
│   │   │   │   │   └── index.ts
│   │   │   │   ├── serialization/
│   │   │   │   │   ├── json.ts
│   │   │   │   │   ├── event-format.ts
│   │   │   │   │   └── index.ts
│   │   │   │   ├── utils/
│   │   │   │   │   ├── uuid.ts
│   │   │   │   │   ├── date.ts
│   │   │   │   │   └── index.ts
│   │   │   │   ├── package.json
│   │   │   │   ├── tsconfig.json
│   │   │   │   ├── tsup.config.ts
│   │   │   │   ├── vitest.config.ts
│   │   │   │   ├── README.md
│   │   │   │   ├── CHANGELOG.md
│   │   │   │   └── index.ts
│   │   │   │
│   │   │   ├── dapr/                     # @sekiban/dapr
│   │   │   │   ├── actors/
│   │   │   │   │   ├── actor-proxy.ts
│   │   │   │   │   ├── aggregate-actor.ts
│   │   │   │   │   ├── types.ts
│   │   │   │   │   └── index.ts
│   │   │   │   ├── client/
│   │   │   │   │   ├── dapr-client.ts
│   │   │   │   │   ├── executor.ts
│   │   │   │   │   └── index.ts
│   │   │   │   ├── serialization/
│   │   │   │   │   ├── compression.ts
│   │   │   │   │   ├── interop.ts
│   │   │   │   │   └── index.ts
│   │   │   │   ├── package.json
│   │   │   │   ├── tsconfig.json
│   │   │   │   ├── tsup.config.ts
│   │   │   │   ├── vitest.config.ts
│   │   │   │   ├── README.md
│   │   │   │   ├── CHANGELOG.md
│   │   │   │   └── index.ts
│   │   │   │
│   │   │   ├── testing/                  # @sekiban/testing
│   │   │   │   ├── fixtures/
│   │   │   │   │   ├── events.ts
│   │   │   │   │   ├── aggregates.ts
│   │   │   │   │   ├── commands.ts
│   │   │   │   │   └── index.ts
│   │   │   │   ├── builders/
│   │   │   │   │   ├── event-builder.ts
│   │   │   │   │   ├── aggregate-builder.ts
│   │   │   │   │   └── index.ts
│   │   │   │   ├── assertions/
│   │   │   │   │   ├── result-assertions.ts
│   │   │   │   │   ├── event-assertions.ts
│   │   │   │   │   └── index.ts
│   │   │   │   ├── test-base.ts
│   │   │   │   ├── in-memory-test.ts
│   │   │   │   ├── package.json
│   │   │   │   ├── tsconfig.json
│   │   │   │   ├── tsup.config.ts
│   │   │   │   ├── README.md
│   │   │   │   ├── CHANGELOG.md
│   │   │   │   └── index.ts
│   │   │   │
│   │   │   ├── cosmos/                   # @sekiban/cosmos
│   │   │   │   ├── client/
│   │   │   │   │   ├── cosmos-client.ts
│   │   │   │   │   ├── config.ts
│   │   │   │   │   └── index.ts
│   │   │   │   ├── store/
│   │   │   │   │   ├── cosmos-event-store.ts
│   │   │   │   │   ├── query-builder.ts
│   │   │   │   │   └── index.ts
│   │   │   │   ├── package.json
│   │   │   │   ├── tsconfig.json
│   │   │   │   ├── tsup.config.ts
│   │   │   │   ├── vitest.config.ts
│   │   │   │   ├── README.md
│   │   │   │   ├── CHANGELOG.md
│   │   │   │   └── index.ts
│   │   │   │
│   │   │   └── postgres/                 # @sekiban/postgres
│   │   │       ├── client/
│   │   │       │   ├── pg-client.ts
│   │   │       │   ├── pool.ts
│   │   │       │   └── index.ts
│   │   │       ├── store/
│   │   │       │   ├── postgres-event-store.ts
│   │   │       │   ├── sql-builder.ts
│   │   │       │   └── index.ts
│   │   │       ├── migrations/
│   │   │       │   ├── 001_initial.sql
│   │   │       │   ├── migrate.ts
│   │   │       │   └── index.ts
│   │   │       ├── package.json
│   │   │       ├── tsconfig.json
│   │   │       ├── tsup.config.ts
│   │   │       ├── vitest.config.ts
│   │   │       ├── README.md
│   │   │       ├── CHANGELOG.md
│   │   │       └── index.ts
│   │   │
│   │   ├── examples/                     # Example projects
│   │   │   ├── basic-usage/
│   │   │   │   ├── src/
│   │   │   │   │   ├── domain/
│   │   │   │   │   │   └── user/
│   │   │   │   │   │       ├── commands/
│   │   │   │   │   │       ├── events/
│   │   │   │   │   │       ├── projector.ts
│   │   │   │   │   │       └── queries/
│   │   │   │   │   └── index.ts
│   │   │   │   ├── package.json
│   │   │   │   ├── tsconfig.json
│   │   │   │   └── README.md
│   │   │   │
│   │   │   ├── with-dapr/
│   │   │   │   ├── src/
│   │   │   │   ├── dapr/
│   │   │   │   │   └── config.yaml
│   │   │   │   ├── package.json
│   │   │   │   └── README.md
│   │   │   │
│   │   │   └── migration-from-csharp/
│   │   │       ├── src/
│   │   │       ├── docs/
│   │   │       ├── package.json
│   │   │       └── README.md
│   │   │
│   │   └── tests/                        # Integration & E2E tests
│   │       ├── integration/
│   │       │   ├── core/
│   │       │   ├── dapr/
│   │       │   └── interop/
│   │       ├── e2e/
│   │       │   ├── basic-flow.test.ts
│   │       │   ├── dapr-flow.test.ts
│   │       │   └── fixtures/
│   │       └── performance/
│   │           ├── benchmarks.ts
│   │           ├── load-tests.ts
│   │           └── results/
│   │
│   ├── scripts/                          # Build and release scripts
│   │   ├── build.ts                     # Build orchestration
│   │   ├── release.ts                   # Release automation
│   │   ├── version-bump.ts              # Version management
│   │   ├── publish.ts                   # NPM publishing
│   │   ├── generate-docs.ts             # Documentation generation
│   │   └── check-dependencies.ts        # Dependency validation
│   │
│   ├── docs/                            # Documentation
│   │   ├── getting-started.md
│   │   ├── api/                         # API documentation
│   │   │   ├── core.md
│   │   │   ├── dapr.md
│   │   │   ├── testing.md
│   │   │   ├── cosmos.md
│   │   │   └── postgres.md
│   │   ├── guides/                      # User guides
│   │   │   ├── migration-from-csharp.md
│   │   │   ├── testing-guide.md
│   │   │   └── deployment.md
│   │   └── architecture.md
│   │
│   ├── design/                          # Design documents
│   │   ├── devtestprocess.md
│   │   ├── folder.md
│   │   └── release-structure.md         # This file
│   │
│   ├── .github/                         # GitHub configuration
│   │   ├── workflows/
│   │   │   ├── ci.yml                   # Continuous Integration
│   │   │   ├── release.yml              # Release workflow
│   │   │   ├── publish.yml              # NPM publish workflow
│   │   │   └── docs.yml                 # Documentation deployment
│   │   ├── ISSUE_TEMPLATE/
│   │   │   ├── bug_report.md
│   │   │   └── feature_request.md
│   │   └── pull_request_template.md
│   │
│   ├── .changeset/                      # Changesets configuration
│   │   └── config.json
│   │
│   ├── package.json                     # Root package.json
│   ├── pnpm-workspace.yaml             # PNPM workspace config
│   ├── tsconfig.base.json              # Base TypeScript config
│   ├── tsconfig.json                   # Root TypeScript config
│   ├── vitest.config.ts                # Root test configuration
│   ├── .eslintrc.js                    # ESLint configuration
│   ├── .prettierrc                     # Prettier configuration
│   ├── .gitignore                      # Git ignore file
│   ├── .npmrc                          # NPM configuration
│   ├── LICENSE                         # License file
│   ├── README.md                       # Project README
│   └── CONTRIBUTING.md                 # Contribution guidelines
│
└── [Other directories...]
```

## Release Configuration Files

### Root package.json

```json
{
  "name": "sekiban-ts",
  "private": true,
  "workspaces": [
    "src/packages/*",
    "src/examples/*"
  ],
  "scripts": {
    "build": "pnpm -r build",
    "test": "vitest",
    "test:unit": "vitest run --dir src/packages",
    "test:integration": "vitest run --dir src/tests/integration",
    "test:e2e": "vitest run --dir src/tests/e2e",
    "test:coverage": "vitest run --coverage",
    "lint": "eslint src/**/*.ts",
    "format": "prettier --write src/**/*.ts",
    "type-check": "tsc --noEmit",
    "clean": "pnpm -r clean && rimraf coverage",
    "changeset": "changeset",
    "version": "changeset version",
    "publish": "pnpm build && changeset publish",
    "release": "pnpm run scripts/release.ts",
    "docs:generate": "pnpm run scripts/generate-docs.ts",
    "prepare": "husky install"
  },
  "devDependencies": {
    "@changesets/cli": "^2.27.0",
    "@types/node": "^20.0.0",
    "@typescript-eslint/eslint-plugin": "^6.0.0",
    "@typescript-eslint/parser": "^6.0.0",
    "@vitest/coverage-v8": "^1.0.0",
    "@vitest/ui": "^1.0.0",
    "eslint": "^8.50.0",
    "husky": "^8.0.0",
    "lint-staged": "^15.0.0",
    "prettier": "^3.0.0",
    "rimraf": "^5.0.0",
    "tsup": "^8.0.0",
    "tsx": "^4.0.0",
    "typescript": "^5.3.0",
    "vitest": "^1.0.0"
  },
  "lint-staged": {
    "*.ts": [
      "eslint --fix",
      "prettier --write"
    ]
  }
}
```

### .changeset/config.json

```json
{
  "$schema": "https://unpkg.com/@changesets/config@2.3.1/schema.json",
  "changelog": "@changesets/cli/changelog",
  "commit": false,
  "fixed": [],
  "linked": [],
  "access": "public",
  "baseBranch": "main",
  "updateInternalDependencies": "patch",
  "ignore": ["src/examples/*"]
}
```

### .github/workflows/release.yml

```yaml
name: Release

on:
  push:
    branches:
      - main

concurrency: ${{ github.workflow }}-${{ github.ref }}

jobs:
  release:
    name: Release
    runs-on: ubuntu-latest
    steps:
      - name: Checkout Repo
        uses: actions/checkout@v4

      - name: Setup pnpm
        uses: pnpm/action-setup@v2
        with:
          version: 8

      - name: Setup Node.js
        uses: actions/setup-node@v4
        with:
          node-version: 20
          cache: 'pnpm'

      - name: Install Dependencies
        run: pnpm install --frozen-lockfile

      - name: Create Release Pull Request or Publish to npm
        id: changesets
        uses: changesets/action@v1
        with:
          # This expects you to have a script called release which does a build
          publish: pnpm publish
          version: pnpm version
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
          NPM_TOKEN: ${{ secrets.NPM_TOKEN }}
```

### .github/workflows/ci.yml

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  test:
    name: Test
    runs-on: ubuntu-latest
    strategy:
      matrix:
        node-version: [18, 20]
    steps:
      - uses: actions/checkout@v4
      
      - uses: pnpm/action-setup@v2
        with:
          version: 8
      
      - uses: actions/setup-node@v4
        with:
          node-version: ${{ matrix.node-version }}
          cache: 'pnpm'
      
      - run: pnpm install --frozen-lockfile
      
      - run: pnpm type-check
      
      - run: pnpm lint
      
      - run: pnpm test:coverage
      
      - name: Upload coverage reports
        uses: codecov/codecov-action@v3
        if: matrix.node-version == 20
        with:
          file: ./coverage/coverage-final.json
```

### Individual Package Configuration Example (@sekiban/core)

#### package.json

```json
{
  "name": "@sekiban/core",
  "version": "0.1.0",
  "description": "Core event sourcing functionality for Sekiban",
  "keywords": ["event-sourcing", "cqrs", "typescript", "sekiban"],
  "homepage": "https://github.com/J-Tech-Japan/Sekiban-ts#readme",
  "bugs": {
    "url": "https://github.com/J-Tech-Japan/Sekiban-ts/issues"
  },
  "repository": {
    "type": "git",
    "url": "git+https://github.com/J-Tech-Japan/Sekiban-ts.git",
    "directory": "ts/src/packages/core"
  },
  "license": "MIT",
  "author": "J-Tech Japan",
  "main": "dist/index.js",
  "module": "dist/index.mjs",
  "types": "dist/index.d.ts",
  "exports": {
    ".": {
      "types": "./dist/index.d.ts",
      "import": "./dist/index.mjs",
      "require": "./dist/index.js"
    },
    "./aggregates": {
      "types": "./dist/aggregates/index.d.ts",
      "import": "./dist/aggregates/index.mjs",
      "require": "./dist/aggregates/index.js"
    },
    "./commands": {
      "types": "./dist/commands/index.d.ts",
      "import": "./dist/commands/index.mjs",
      "require": "./dist/commands/index.js"
    },
    "./events": {
      "types": "./dist/events/index.d.ts",
      "import": "./dist/events/index.mjs",
      "require": "./dist/events/index.js"
    },
    "./queries": {
      "types": "./dist/queries/index.d.ts",
      "import": "./dist/queries/index.mjs",
      "require": "./dist/queries/index.js"
    },
    "./result": {
      "types": "./dist/result/index.d.ts",
      "import": "./dist/result/index.mjs",
      "require": "./dist/result/index.js"
    },
    "./documents": {
      "types": "./dist/documents/index.d.ts",
      "import": "./dist/documents/index.mjs",
      "require": "./dist/documents/index.js"
    },
    "./executors": {
      "types": "./dist/executors/index.d.ts",
      "import": "./dist/executors/index.mjs",
      "require": "./dist/executors/index.js"
    }
  },
  "scripts": {
    "build": "tsup",
    "dev": "tsup --watch",
    "test": "vitest",
    "test:run": "vitest run",
    "clean": "rimraf dist coverage",
    "prepublishOnly": "pnpm build"
  },
  "dependencies": {
    "neverthrow": "^6.1.0",
    "uuid": "^9.0.0",
    "zod": "^3.22.0"
  },
  "devDependencies": {
    "@types/uuid": "^9.0.0",
    "tsup": "^8.0.0",
    "vitest": "^1.0.0"
  },
  "files": [
    "dist",
    "src",
    "!src/**/*.test.ts"
  ],
  "sideEffects": false,
  "publishConfig": {
    "access": "public",
    "registry": "https://registry.npmjs.org/"
  },
  "engines": {
    "node": ">=18.0.0"
  }
}
```

#### tsup.config.ts

```typescript
import { defineConfig } from 'tsup'

export default defineConfig({
  entry: {
    index: 'index.ts',
    'aggregates/index': 'aggregates/index.ts',
    'commands/index': 'commands/index.ts',
    'events/index': 'events/index.ts',
    'queries/index': 'queries/index.ts',
    'result/index': 'result/index.ts',
    'documents/index': 'documents/index.ts',
    'executors/index': 'executors/index.ts',
  },
  format: ['cjs', 'esm'],
  dts: true,
  sourcemap: true,
  clean: true,
  treeshake: true,
  splitting: false,
  minify: false,
  target: 'es2022',
  outDir: 'dist',
  external: ['neverthrow', 'uuid', 'zod'],
})
```

## Release Process

### 1. Development Phase

- Develop features in feature branches
- Write tests following TDD principles
- Ensure all tests pass
- Update documentation

### 2. Pre-release Checklist

- [ ] All tests passing
- [ ] Type checking passes
- [ ] Linting passes
- [ ] Coverage meets thresholds (90%+)
- [ ] Documentation updated
- [ ] Changesets created for changes

### 3. Release Steps

1. **Create Changeset**
   ```bash
   pnpm changeset
   ```

2. **Version Packages**
   ```bash
   pnpm version
   ```

3. **Build All Packages**
   ```bash
   pnpm build
   ```

4. **Run Full Test Suite**
   ```bash
   pnpm test:coverage
   ```

5. **Publish to NPM**
   ```bash
   pnpm publish
   ```

### 4. Post-release

- Tag release in Git
- Update documentation site
- Announce release
- Monitor for issues

## Package Versioning Strategy

- **Major**: Breaking changes
- **Minor**: New features (backward compatible)
- **Patch**: Bug fixes

### Version Synchronization

- Core packages maintain independent versions
- Storage providers depend on specific core versions
- Examples always use latest versions

## NPM Package Names

- `@sekiban/core` - Core functionality
- `@sekiban/dapr` - Dapr integration
- `@sekiban/testing` - Testing utilities
- `@sekiban/cosmos` - Azure Cosmos DB
- `@sekiban/postgres` - PostgreSQL

## Release Artifacts

### NPM Packages

- Source maps included
- TypeScript declarations
- Both CommonJS and ESM builds
- Comprehensive documentation

### Documentation

- API documentation (generated)
- User guides
- Migration guides
- Example projects

### GitHub Releases

- Release notes
- Migration instructions
- Breaking changes highlighted
- Download links

## Quality Gates

### Automated Checks

1. **Unit Tests**: 90% coverage minimum
2. **Integration Tests**: All passing
3. **Type Checking**: No errors
4. **Linting**: No violations
5. **Build**: Successful for all packages

### Manual Checks

1. **Documentation Review**
2. **Breaking Change Assessment**
3. **Performance Regression Testing**
4. **Cross-platform Compatibility**

## Security Considerations

### Pre-release Security Checklist

- [ ] No hardcoded secrets
- [ ] Dependencies updated
- [ ] Security vulnerabilities scanned
- [ ] SAST/DAST passed

### NPM Security

- 2FA enabled for publishing
- Scoped packages under @sekiban
- Regular dependency audits

## Maintenance

### Regular Tasks

- Weekly dependency updates
- Monthly performance benchmarks
- Quarterly security audits
- Continuous documentation updates

### Support Policy

- Latest major version: Full support
- Previous major version: Security updates for 12 months
- Older versions: Community support only
