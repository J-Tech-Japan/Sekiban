# Awilix DI Implementation for Sekiban Dapr Actors

## Overview

This document describes the Awilix dependency injection implementation for Sekiban TypeScript Dapr actors.

## What Was Implemented

### 1. **Wrapper Pattern for Actors**

The `AggregateActor` class now uses a wrapper pattern that separates:
- **Dapr requirements**: Constructor signature `(daprClient: DaprClient, id: ActorId)`
- **Business logic**: Delegated to `AggregateActorImpl` class

### 2. **Awilix Container Setup**

Created `/src/packages/dapr/src/container/dapr-container.ts`:
- Manages the Awilix container for Dapr actors
- Provides type-safe dependency resolution
- Uses `InjectionMode.PROXY` for automatic dependency resolution

### 3. **Dependency Registration**

The `AggregateActorFactory.configure()` method now:
- Initializes the Awilix container with all dependencies
- Registers: domainTypes, serviceProvider, actorProxyFactory, serializationService, eventStore

### 4. **Actor Implementation**

The `AggregateActor` constructor:
- Gets dependencies from Awilix using `getDaprCradle()`
- Creates `AggregateActorImpl` with injected dependencies
- All actor methods delegate to the implementation

## Files Modified/Created

1. **New Files**:
   - `/src/packages/dapr/src/container/dapr-container.ts` - Awilix container management
   - `/src/packages/dapr/src/container/index.ts` - Container exports
   - `/src/packages/dapr/src/actors/aggregate-actor-impl.ts` - Implementation class
   - `/src/packages/dapr/src/actors/serializable-types.ts` - Type definitions

2. **Modified Files**:
   - `/src/packages/dapr/src/actors/aggregate-actor.ts` - Uses Awilix DI
   - `/src/packages/dapr/src/actors/aggregate-actor-factory.ts` - Initializes container
   - `/src/packages/dapr/package.json` - Added awilix dependency
   - `/src/packages/dapr/tsup.config.ts` - Added awilix to externals, disabled minification

## How to Use

1. **Start the Dapr server**:
   ```bash
   cd packages/api
   ./run-dapr.sh
   ```

2. **Test the actor**:
   ```bash
   ./test-actor.sh
   ```

## Known Issues

1. **Timer Registration**: The actor timer registration is currently commented out due to a method resolution issue. This needs further investigation.

2. **Build Warnings**: The TypeScript build shows warnings about the "types" export condition placement. This doesn't affect functionality.

## Benefits of Awilix Implementation

1. **Clean Separation**: Business logic is separated from Dapr infrastructure
2. **Testability**: Implementation can be tested without Dapr
3. **Flexibility**: Easy to swap dependencies for testing or different environments
4. **Type Safety**: Full TypeScript support with proper types
5. **No Decorators**: Works without experimental decorators

## Next Steps

1. Fix the timer registration issue
2. Add proper error handling for missing dependencies
3. Consider adding scoped containers for multi-tenancy
4. Add comprehensive tests for the DI implementation