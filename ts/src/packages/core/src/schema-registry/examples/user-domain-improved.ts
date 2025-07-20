/**
 * User Domain Example using Improved Schema-based Command Design
 * 
 * This example demonstrates:
 * - Unified command definition with projector specification
 * - TypedPartitionKeys for type-safe partition key generation
 * - Command context with aggregate state access
 * - Both standard defineCommand and simplified command API
 * - Payload type constraints for state transitions
 */

import { z } from 'zod';
import { ok, err } from 'neverthrow';
import { defineEvent, defineCommand, defineProjector, command } from '../index';
import { TypedPartitionKeys } from '../../documents/index';
import { CommandValidationError } from '../../result/errors';
import type { ITypedAggregatePayload } from '../../aggregates/aggregate-projector';

// ============================================
// Events
// ============================================

export const UserCreated = defineEvent({
  type: 'UserCreated',
  schema: z.object({
    userId: z.string(),
    name: z.string(),
    email: z.string().email(),
    createdAt: z.string().datetime()
  })
});

export const UserUpdated = defineEvent({
  type: 'UserUpdated',
  schema: z.object({
    userId: z.string(),
    name: z.string().optional(),
    email: z.string().email().optional(),
    updatedAt: z.string().datetime()
  })
});

export const UserDeactivated = defineEvent({
  type: 'UserDeactivated',
  schema: z.object({
    userId: z.string(),
    reason: z.string(),
    deactivatedAt: z.string().datetime()
  })
});

export const UserActivated = defineEvent({
  type: 'UserActivated',
  schema: z.object({
    userId: z.string(),
    reason: z.string(),
    activatedAt: z.string().datetime()
  })
});

export const UserDeleted = defineEvent({
  type: 'UserDeleted',
  schema: z.object({
    userId: z.string(),
    deletedAt: z.string().datetime()
  })
});

// ============================================
// Aggregate Payloads (State Machine)
// ============================================

export interface ActiveUser extends ITypedAggregatePayload {
  aggregateType: 'ActiveUser';
  userId: string;
  name: string;
  email: string;
  createdAt: string;
  lastUpdatedAt?: string;
}

export interface InactiveUser extends ITypedAggregatePayload {
  aggregateType: 'InactiveUser';
  userId: string;
  name: string;
  email: string;
  deactivatedAt: string;
  deactivationReason: string;
}

export interface DeletedUser extends ITypedAggregatePayload {
  aggregateType: 'DeletedUser';
  userId: string;
  deletedAt: string;
}

export type UserPayloadUnion = ActiveUser | InactiveUser | DeletedUser;

// ============================================
// Projector
// ============================================

export const UserProjector = defineProjector<UserPayloadUnion>({
  aggregateType: 'User',
  initialState: () => ({ aggregateType: 'Empty' as const }),
  projections: {
    UserCreated: (state, event) => ({
      aggregateType: 'ActiveUser' as const,
      userId: event.userId,
      name: event.name,
      email: event.email,
      createdAt: event.createdAt
    }),
    
    UserUpdated: (state, event) => {
      if (state.aggregateType !== 'ActiveUser') return state;
      return {
        ...state,
        name: event.name ?? state.name,
        email: event.email ?? state.email,
        lastUpdatedAt: event.updatedAt
      };
    },
    
    UserDeactivated: (state, event) => {
      if (state.aggregateType !== 'ActiveUser') return state;
      return {
        aggregateType: 'InactiveUser' as const,
        userId: state.userId,
        name: state.name,
        email: state.email,
        deactivatedAt: event.deactivatedAt,
        deactivationReason: event.reason
      };
    },
    
    UserActivated: (state, event) => {
      if (state.aggregateType !== 'InactiveUser') return state;
      return {
        aggregateType: 'ActiveUser' as const,
        userId: state.userId,
        name: state.name,
        email: state.email,
        createdAt: event.activatedAt,
        lastUpdatedAt: event.activatedAt
      };
    },
    
    UserDeleted: (state, event) => ({
      aggregateType: 'DeletedUser' as const,
      userId: event.userId,
      deletedAt: event.deletedAt
    })
  }
});

// ============================================
// Commands using standard defineCommand
// ============================================

export const CreateUser = defineCommand({
  type: 'CreateUser',
  schema: z.object({
    name: z.string().min(1, 'Name is required'),
    email: z.string().email('Invalid email format'),
    tenantId: z.string().optional()
  }),
  projector: UserProjector,
  handlers: {
    specifyPartitionKeys: (data) => data.tenantId 
      ? TypedPartitionKeys.Generate(UserProjector, data.tenantId)
      : TypedPartitionKeys.Generate(UserProjector),
    
    validate: (data) => {
      // Business validation beyond schema
      if (data.email.endsWith('@test.com')) {
        return err(new CommandValidationError('CreateUser', 
          ['Test domain emails are not allowed']
        ));
      }
      
      // Check for corporate domains
      const corporateDomains = ['@company.com', '@corp.com'];
      const isCorporate = corporateDomains.some(domain => 
        data.email.endsWith(domain)
      );
      
      if (isCorporate && !data.tenantId) {
        return err(new CommandValidationError('CreateUser', 
          ['Corporate emails require a tenant ID']
        ));
      }
      
      return ok(undefined);
    },
    
    handle: (data, context) => {
      const aggregateId = context.getPartitionKeys().aggregateId;
      
      const event = UserCreated.create({
        userId: aggregateId,
        name: data.name,
        email: data.email,
        createdAt: new Date().toISOString()
      });
      
      return ok([event]);
    }
  }
});

export const UpdateUser = defineCommand({
  type: 'UpdateUser',
  schema: z.object({
    userId: z.string(),
    name: z.string().min(1).optional(),
    email: z.string().email().optional()
  }),
  projector: UserProjector,
  requiredPayloadType: 'ActiveUser', // Only active users can be updated
  handlers: {
    specifyPartitionKeys: (data) => 
      TypedPartitionKeys.Existing(UserProjector, data.userId),
    
    validate: (data) => {
      if (!data.name && !data.email) {
        return err(new CommandValidationError('UpdateUser', 
          ['At least one field must be updated']
        ));
      }
      return ok(undefined);
    },
    
    handle: (data, context) => {
      // context.getAggregate() returns Aggregate<ActiveUser> due to requiredPayloadType
      const aggregate = context.getAggregate();
      if (aggregate.isErr()) {
        return err(aggregate.error);
      }
      
      const event = UserUpdated.create({
        userId: data.userId,
        name: data.name,
        email: data.email,
        updatedAt: new Date().toISOString()
      });
      
      return ok([event]);
    }
  }
});

export const DeactivateUser = defineCommand({
  type: 'DeactivateUser',
  schema: z.object({
    userId: z.string(),
    reason: z.string().min(10, 'Reason must be at least 10 characters')
  }),
  projector: UserProjector,
  requiredPayloadType: 'ActiveUser',
  handlers: {
    specifyPartitionKeys: (data) => 
      TypedPartitionKeys.Existing(UserProjector, data.userId),
    
    handle: (data, context) => {
      const event = UserDeactivated.create({
        userId: data.userId,
        reason: data.reason,
        deactivatedAt: new Date().toISOString()
      });
      
      return ok([event]);
    }
  }
});

// ============================================
// Commands using simplified API
// ============================================

export const CreateUserSimple = command.create('CreateUserSimple', {
  schema: z.object({
    name: z.string().min(1),
    email: z.string().email(),
    tenantId: z.string().optional()
  }),
  projector: UserProjector,
  partitionKeys: (data) => data.tenantId 
    ? TypedPartitionKeys.Generate(UserProjector, data.tenantId)
    : TypedPartitionKeys.Generate(UserProjector),
  handle: (data, { aggregateId, appendEvent }) => {
    appendEvent(UserCreated.create({
      userId: aggregateId,
      name: data.name,
      email: data.email,
      createdAt: new Date().toISOString()
    }));
  }
});

export const UpdateUserSimple = command.update('UpdateUserSimple', {
  schema: z.object({
    userId: z.string(),
    name: z.string().optional(),
    email: z.string().email().optional()
  }),
  projector: UserProjector,
  partitionKeys: (data) => TypedPartitionKeys.Existing(UserProjector, data.userId),
  handle: (data, { appendEvent }) => {
    appendEvent(UserUpdated.create({
      userId: data.userId,
      name: data.name,
      email: data.email,
      updatedAt: new Date().toISOString()
    }));
  }
});

export const ActivateUserSimple = command.transition('ActivateUserSimple', {
  schema: z.object({
    userId: z.string(),
    reason: z.string()
  }),
  projector: UserProjector,
  fromState: 'InactiveUser',
  partitionKeys: (data) => TypedPartitionKeys.Existing(UserProjector, data.userId),
  handle: (data, { aggregate, appendEvent }) => {
    // aggregate is typed as InactiveUser
    console.log(`Reactivating user ${aggregate?.name} after ${aggregate?.deactivationReason}`);
    
    appendEvent(UserActivated.create({
      userId: data.userId,
      reason: data.reason,
      activatedAt: new Date().toISOString()
    }));
  }
});

// ============================================
// Usage Example
// ============================================

export function demonstrateUsage() {
  // Creating commands with type safety
  const createCmd = CreateUser.create({
    name: 'John Doe',
    email: 'john@example.com',
    tenantId: 'tenant-123'
  });
  
  // The command instance implements ICommandWithHandler
  const projector = createCmd.getProjector(); // Returns UserProjector instance
  const partitionKeys = createCmd.specifyPartitionKeys(createCmd); // Type-safe partition keys
  
  // Using simplified API
  const createSimpleCmd = CreateUserSimple.create({
    name: 'Jane Doe',
    email: 'jane@example.com'
  });
  
  // State transitions are type-safe
  const activateCmd = ActivateUserSimple.create({
    userId: 'user-123',
    reason: 'User requested reactivation'
  });
  
  console.log('Commands created successfully with improved design!');
}