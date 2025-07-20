# Sekiban TypeScript

> ⚠️ **Alpha Version**: These packages are currently in alpha. APIs may change between releases.

Event Sourcing and CQRS framework for TypeScript with multiple storage providers and Dapr integration.

## Packages

- **[@sekiban/core](./src/packages/core)** - Core framework with event sourcing and CQRS patterns
- **[@sekiban/postgres](./src/packages/postgres)** - PostgreSQL storage provider
- **[@sekiban/cosmos](./src/packages/cosmos)** - Azure Cosmos DB storage provider
- **[@sekiban/dapr](./src/packages/dapr)** - Dapr actor integration with snapshot support

## Installation

```bash
# Install core package
npm install @sekiban/core@alpha

# Install with PostgreSQL support
npm install @sekiban/core@alpha @sekiban/postgres@alpha

# Install with Cosmos DB support
npm install @sekiban/core@alpha @sekiban/cosmos@alpha

# Install with Dapr support
npm install @sekiban/core@alpha @sekiban/dapr@alpha
```

## Development

This is a monorepo managed with pnpm workspaces.

### Prerequisites

- Node.js 18 or higher
- pnpm 8 or higher

### Setup

```bash
# Install dependencies
pnpm install

# Build all packages
pnpm build

# Run tests
pnpm test

# Type checking
pnpm typecheck
```

### Build Order

Packages must be built in the following order due to dependencies:
1. @sekiban/core
2. @sekiban/postgres, @sekiban/cosmos, @sekiban/dapr (can be built in parallel)

Use `pnpm build:packages` to build in the correct order.

## Release Process

We use changesets for version management and releases.

### Creating a Changeset

When you make changes that should be released:

```bash
# Create a changeset
pnpm changeset

# Select the packages that have changed
# Select the type of change (major, minor, patch)
# Write a summary of the changes
```

### Publishing Alpha Releases

Alpha releases are published automatically when changes are pushed to the main branch.

To manually publish an alpha release:

```bash
# Build and publish alpha versions
pnpm release:alpha
```

### Release Workflow

1. Make changes and create changesets
2. Push to main branch
3. GitHub Actions will automatically:
   - Build and test all packages
   - Publish alpha versions to npm
   - Create git tags

### NPM Configuration

Before publishing, ensure you have:
1. An npm account with access to the @sekiban scope
2. Set the NPM_TOKEN secret in GitHub repository settings

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests for your changes
5. Create a changeset
6. Submit a pull request

## License

MIT

## Repository

https://github.com/J-Tech-Japan/Sekiban