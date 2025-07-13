/**
 * Example showing how to use the schema-based type registry with SekibanDomainTypes
 */

import { z } from 'zod';
import {
  defineEvent,
  defineCommand,
  defineProjector,
  globalRegistry,
  createSchemaDomainTypes,
  createInMemorySekibanExecutor,
  PartitionKeys,
  ok,
  err,
  EmptyAggregatePayload
} from '@sekiban/core';

// Step 1: Define Events using Zod schemas
const UserCreated = defineEvent({
  type: 'UserCreated',
  schema: z.object({
    userId: z.string().uuid(),
    name: z.string().min(1),
    email: z.string().email(),
    createdAt: z.string().datetime()
  })
});

const UserUpdated = defineEvent({
  type: 'UserUpdated',
  schema: z.object({
    userId: z.string().uuid(),
    name: z.string().optional(),
    email: z.string().email().optional(),
    updatedAt: z.string().datetime()
  })
});

// Step 2: Define Aggregates
interface UserPayload {
  aggregateType: 'User';
  userId: string;
  name: string;
  email: string;
  createdAt: string;
  updatedAt?: string;
}

// Step 3: Define Commands
const CreateUser = defineCommand({
  type: 'CreateUser',
  schema: z.object({
    name: z.string().min(1),
    email: z.string().email()
  }),
  aggregateType: 'User', // Explicitly specify the aggregate type
  handlers: {
    specifyPartitionKeys: (data) => PartitionKeys.generate('User'),
    validate: (data) => {
      // Business validation
      if (data.email.endsWith('@test.com')) {
        return err({ type: 'ValidationError', message: 'Test emails not allowed' });
      }
      return ok(undefined);
    },
    handle: (data, aggregate) => {
      if (aggregate.payload.aggregateType !== 'Empty') {
        return err({ type: 'AggregateAlreadyExists', message: 'User already exists' });
      }
      
      return ok([
        UserCreated.create({
          userId: crypto.randomUUID(),
          name: data.name,
          email: data.email,
          createdAt: new Date().toISOString()
        })
      ]);
    }
  }
});

const UpdateUser = defineCommand({
  type: 'UpdateUser',
  schema: z.object({
    userId: z.string().uuid(),
    name: z.string().min(1).optional(),
    email: z.string().email().optional()
  }),
  aggregateType: 'User', // Explicitly specify the aggregate type
  handlers: {
    specifyPartitionKeys: (data) => PartitionKeys.existing('User', data.userId),
    validate: (data) => {
      if (!data.name && !data.email) {
        return err({ type: 'ValidationError', message: 'At least one field must be updated' });
      }
      return ok(undefined);
    },
    handle: (data, aggregate) => {
      if (aggregate.payload.aggregateType !== 'User') {
        return err({ type: 'AggregateNotFound', message: 'User not found' });
      }
      
      return ok([
        UserUpdated.create({
          userId: data.userId,
          name: data.name,
          email: data.email,
          updatedAt: new Date().toISOString()
        })
      ]);
    }
  }
});

// Step 4: Define Projector
const userProjector = defineProjector<UserPayload | EmptyAggregatePayload>({
  aggregateType: 'User',
  initialState: () => ({ aggregateType: 'Empty' as const }),
  projections: {
    UserCreated: (state, event: ReturnType<typeof UserCreated.create>) => ({
      aggregateType: 'User' as const,
      userId: event.userId,
      name: event.name,
      email: event.email,
      createdAt: event.createdAt
    }),
    UserUpdated: (state, event: ReturnType<typeof UserUpdated.create>) => {
      if (state.aggregateType !== 'User') return state;
      return {
        ...state,
        name: event.name || state.name,
        email: event.email || state.email,
        updatedAt: event.updatedAt
      };
    }
  }
});

// Step 5: Register all schemas with the global registry
globalRegistry.registerEvent(UserCreated);
globalRegistry.registerEvent(UserUpdated);
globalRegistry.registerCommand(CreateUser);
globalRegistry.registerCommand(UpdateUser);
globalRegistry.registerProjector(userProjector);

// Step 6: Create SekibanDomainTypes from the schema registry
const domainTypes = createSchemaDomainTypes(globalRegistry);

// Step 7: Create executor with SekibanDomainTypes
const executor = createInMemorySekibanExecutor(domainTypes, {
  enableSnapshots: true,
  snapshotFrequency: 10
});

// Step 8: Use the executor
async function example() {
  // Create a new user
  const createCommand = CreateUser.create({
    name: 'John Doe',
    email: 'john@example.com'
  });
  
  const createResult = await executor.executeCommand(createCommand);
  if (createResult.isErr()) {
    console.error('Failed to create user:', createResult.error);
    return;
  }
  
  console.log('User created successfully');
  
  // Update the user
  const updateCommand = UpdateUser.create({
    userId: createResult.value.aggregateId,
    name: 'John Smith'
  });
  
  const updateResult = await executor.executeCommand(updateCommand);
  if (updateResult.isErr()) {
    console.error('Failed to update user:', updateResult.error);
    return;
  }
  
  console.log('User updated successfully');
  
  // Load the aggregate
  const loadResult = await executor.loadAggregate<UserPayload>(
    PartitionKeys.existing('User', createResult.value.aggregateId)
  );
  
  if (loadResult.isErr() || !loadResult.value) {
    console.error('Failed to load user');
    return;
  }
  
  const user = loadResult.value;
  console.log('Loaded user:', user.payload);
}

// Run the example
example().catch(console.error);

// Export for use in other modules
export { domainTypes, executor };