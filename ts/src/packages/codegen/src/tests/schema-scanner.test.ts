import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { Project } from 'ts-morph';
import { SchemaScanner } from '../scanner.js';
import type { ScannedSchema } from '../types.js';

describe('SchemaScanner', () => {
  let project: Project;
  let scanner: SchemaScanner;

  beforeEach(() => {
    project = new Project({
      useInMemoryFileSystem: true,
      compilerOptions: {
        target: 99, // Latest
        module: 99, // ESNext
        declaration: true,
        esModuleInterop: true,
        allowSyntheticDefaultImports: true
      }
    });
    scanner = new SchemaScanner(project);
  });

  afterEach(() => {
    // Reset project by removing all source files
    project.getSourceFiles().forEach(file => file.delete());
  });

  // Test 1: Finds defineEvent calls
  it('finds defineEvent calls', () => {
    // Arrange
    const sourceCode = `
import { z } from 'zod';
import { defineEvent } from '@sekiban/core';

export const UserCreated = defineEvent({
  type: 'UserCreated',
  schema: z.object({
    userId: z.string(),
    name: z.string(),
    email: z.string().email()
  })
});
`;
    project.createSourceFile('user-events.ts', sourceCode);

    // Act
    const result = scanner.scanForSchemas();

    // Assert
    expect(result.events).toHaveLength(1);
    expect(result.events[0].name).toBe('UserCreated');
    expect(result.events[0].type).toBe('UserCreated');
    expect(result.events[0].filePath).toBe('/user-events.ts');
  });

  // Test 2: Extracts event type names from literal
  it('extracts event type names from literal', () => {
    // Arrange
    const sourceCode = `
import { defineEvent } from '@sekiban/core';
import { z } from 'zod';

export const OrderPlaced = defineEvent({
  type: 'OrderPlaced',
  schema: z.object({ orderId: z.string() })
});
`;
    project.createSourceFile('order-events.ts', sourceCode);

    // Act
    const result = scanner.scanForSchemas();

    // Assert
    expect(result.events).toHaveLength(1);
    expect(result.events[0].type).toBe('OrderPlaced');
    expect(result.events[0].name).toBe('OrderPlaced');
  });

  // Test 3: Finds defineCommand calls
  it('finds defineCommand calls', () => {
    // Arrange
    const sourceCode = `
import { z } from 'zod';
import { defineCommand } from '@sekiban/core';

export const CreateUser = defineCommand({
  type: 'CreateUser',
  schema: z.object({
    name: z.string(),
    email: z.string().email()
  }),
  handlers: {
    specifyPartitionKeys: () => PartitionKeys.generate('User'),
    validate: () => ok(undefined),
    handle: () => ok([])
  }
});
`;
    project.createSourceFile('user-commands.ts', sourceCode);

    // Act
    const result = scanner.scanForSchemas();

    // Assert
    expect(result.commands).toHaveLength(1);
    expect(result.commands[0].name).toBe('CreateUser');
    expect(result.commands[0].type).toBe('CreateUser');
    expect(result.commands[0].filePath).toBe('/user-commands.ts');
  });

  // Test 4: Extracts command type names
  it('extracts command type names', () => {
    // Arrange
    const sourceCode = `
import { defineCommand } from '@sekiban/core';
import { z } from 'zod';

export const UpdateUserProfile = defineCommand({
  type: 'UpdateUserProfile',
  schema: z.object({ userId: z.string(), name: z.string() }),
  handlers: { /* handlers */ }
});
`;
    project.createSourceFile('user-commands.ts', sourceCode);

    // Act
    const result = scanner.scanForSchemas();

    // Assert
    expect(result.commands).toHaveLength(1);
    expect(result.commands[0].type).toBe('UpdateUserProfile');
    expect(result.commands[0].name).toBe('UpdateUserProfile');
  });

  // Test 5: Finds defineProjector calls
  it('finds defineProjector calls', () => {
    // Arrange
    const sourceCode = `
import { defineProjector } from '@sekiban/core';
import { EmptyAggregatePayload } from '@sekiban/core';

export const userProjector = defineProjector({
  aggregateType: 'User',
  initialState: () => new EmptyAggregatePayload(),
  projections: {
    UserCreated: (state, event) => ({ ...state, ...event })
  }
});
`;
    project.createSourceFile('user-projector.ts', sourceCode);

    // Act
    const result = scanner.scanForSchemas();

    // Assert
    expect(result.projectors).toHaveLength(1);
    expect(result.projectors[0].name).toBe('userProjector');
    expect(result.projectors[0].aggregateType).toBe('User');
    expect(result.projectors[0].filePath).toBe('/user-projector.ts');
  });

  // Test 6: Extracts projector aggregate type
  it('extracts projector aggregate type', () => {
    // Arrange
    const sourceCode = `
import { defineProjector } from '@sekiban/core';

export const orderProjector = defineProjector({
  aggregateType: 'Order',
  initialState: () => ({ aggregateType: 'Empty' }),
  projections: {}
});
`;
    project.createSourceFile('order-projector.ts', sourceCode);

    // Act
    const result = scanner.scanForSchemas();

    // Assert
    expect(result.projectors).toHaveLength(1);
    expect(result.projectors[0].aggregateType).toBe('Order');
    expect(result.projectors[0].name).toBe('orderProjector');
  });

  // Test 7: Handles multiple files
  it('handles multiple files', () => {
    // Arrange
    const eventCode = `
import { defineEvent } from '@sekiban/core';
import { z } from 'zod';

export const UserCreated = defineEvent({
  type: 'UserCreated',
  schema: z.object({ userId: z.string() })
});

export const UserUpdated = defineEvent({
  type: 'UserUpdated',
  schema: z.object({ userId: z.string(), name: z.string() })
});
`;

    const commandCode = `
import { defineCommand } from '@sekiban/core';
import { z } from 'zod';

export const CreateUser = defineCommand({
  type: 'CreateUser',
  schema: z.object({ name: z.string() }),
  handlers: {}
});
`;

    project.createSourceFile('user-events.ts', eventCode);
    project.createSourceFile('user-commands.ts', commandCode);

    // Act
    const result = scanner.scanForSchemas();

    // Assert
    expect(result.events).toHaveLength(2);
    expect(result.commands).toHaveLength(1);
    expect(result.projectors).toHaveLength(0);
    
    expect(result.events.map(e => e.name)).toContain('UserCreated');
    expect(result.events.map(e => e.name)).toContain('UserUpdated');
    expect(result.commands[0].name).toBe('CreateUser');
  });

  // Test 8: Ignores test files
  it('ignores test files', () => {
    // Arrange
    const testCode = `
import { defineEvent } from '@sekiban/core';
import { z } from 'zod';

export const TestEvent = defineEvent({
  type: 'TestEvent',
  schema: z.object({ id: z.string() })
});
`;

    const normalCode = `
import { defineEvent } from '@sekiban/core';
import { z } from 'zod';

export const UserCreated = defineEvent({
  type: 'UserCreated',
  schema: z.object({ userId: z.string() })
});
`;

    project.createSourceFile('test-event.test.ts', testCode);
    project.createSourceFile('user-events.ts', normalCode);

    // Act
    const result = scanner.scanForSchemas();

    // Assert
    expect(result.events).toHaveLength(1);
    expect(result.events[0].name).toBe('UserCreated');
  });

  // Test 9: Handles nested directories
  it('handles nested directories', () => {
    // Arrange
    const userEventCode = `
import { defineEvent } from '@sekiban/core';
import { z } from 'zod';

export const UserCreated = defineEvent({
  type: 'UserCreated',
  schema: z.object({ userId: z.string() })
});
`;

    const orderEventCode = `
import { defineEvent } from '@sekiban/core';
import { z } from 'zod';

export const OrderPlaced = defineEvent({
  type: 'OrderPlaced',
  schema: z.object({ orderId: z.string() })
});
`;

    project.createSourceFile('domain/user/events/user-created.ts', userEventCode);
    project.createSourceFile('domain/order/events/order-placed.ts', orderEventCode);

    // Act
    const result = scanner.scanForSchemas();

    // Assert
    expect(result.events).toHaveLength(2);
    expect(result.events.map(e => e.name)).toContain('UserCreated');
    expect(result.events.map(e => e.name)).toContain('OrderPlaced');
  });

  // Test 10: Handles const assertions
  it('handles const assertions', () => {
    // Arrange
    const sourceCode = `
import { defineEvent } from '@sekiban/core';
import { z } from 'zod';

export const UserCreated = defineEvent({
  type: 'UserCreated' as const,
  schema: z.object({ userId: z.string() })
} as const);
`;
    project.createSourceFile('user-events.ts', sourceCode);

    // Act
    const result = scanner.scanForSchemas();


    // Assert
    expect(result.events).toHaveLength(1);
    expect(result.events[0].type).toBe('UserCreated');
  });

  // Test 11: Handles non-literal type values gracefully
  it('handles non-literal type values gracefully', () => {
    // Arrange
    const sourceCode = `
import { defineEvent } from '@sekiban/core';
import { z } from 'zod';

const eventType = 'DynamicEvent';

export const DynamicEvent = defineEvent({
  type: eventType,
  schema: z.object({ id: z.string() })
});
`;
    project.createSourceFile('dynamic-events.ts', sourceCode);

    // Act
    const result = scanner.scanForSchemas();

    // Assert
    expect(result.events).toHaveLength(1);
    expect(result.events[0].name).toBe('DynamicEvent');
    expect(result.events[0].type).toBe('DynamicEvent'); // Falls back to variable name
  });

  // Test 12: Extracts relative import paths
  it('extracts relative import paths', () => {
    // Arrange
    const sourceCode = `
import { defineEvent } from '@sekiban/core';
import { z } from 'zod';

export const UserCreated = defineEvent({
  type: 'UserCreated',
  schema: z.object({ userId: z.string() })
});
`;
    project.createSourceFile('src/domain/user/events/user-created.ts', sourceCode);

    // Act
    const result = scanner.scanForSchemas();


    // Assert
    expect(result.events).toHaveLength(1);
    expect(result.events[0].filePath).toBe('/src/domain/user/events/user-created.ts');
    expect(result.events[0].importPath).toBe('../domain/user/events/user-created.js');
  });

  // Test 13: Provides scan summary
  it('provides scan summary', () => {
    // Arrange
    const eventCode = `
import { defineEvent } from '@sekiban/core';
import { z } from 'zod';

export const Event1 = defineEvent({
  type: 'Event1',
  schema: z.object({ id: z.string() })
});

export const Event2 = defineEvent({
  type: 'Event2',
  schema: z.object({ id: z.string() })
});
`;

    const commandCode = `
import { defineCommand } from '@sekiban/core';
import { z } from 'zod';

export const Command1 = defineCommand({
  type: 'Command1',
  schema: z.object({ id: z.string() }),
  handlers: {}
});
`;

    const projectorCode = `
import { defineProjector } from '@sekiban/core';

export const projector1 = defineProjector({
  aggregateType: 'Aggregate1',
  initialState: () => ({}),
  projections: {}
});
`;

    project.createSourceFile('events.ts', eventCode);
    project.createSourceFile('commands.ts', commandCode);
    project.createSourceFile('projectors.ts', projectorCode);

    // Act
    const result = scanner.scanForSchemas();

    // Assert
    expect(result.summary.totalEvents).toBe(2);
    expect(result.summary.totalCommands).toBe(1);
    expect(result.summary.totalProjectors).toBe(1);
    expect(result.summary.totalFiles).toBe(3);
  });

  // Test 14: Handles malformed definitions gracefully
  it('handles malformed definitions gracefully', () => {
    // Arrange
    const malformedCode = `
import { defineEvent } from '@sekiban/core';

// Missing required properties
export const MalformedEvent = defineEvent({
  schema: z.object({ id: z.string() })
  // Missing 'type' property
});

// Valid event for comparison
export const ValidEvent = defineEvent({
  type: 'ValidEvent',
  schema: z.object({ id: z.string() })
});
`;
    project.createSourceFile('malformed.ts', malformedCode);

    // Act & Assert - Should not throw
    const result = scanner.scanForSchemas();
    
    // Should find the valid event, skip the malformed one
    expect(result.events).toHaveLength(1);
    expect(result.events[0].name).toBe('ValidEvent');
  });

  // Test 15: Scanner configuration options
  it('respects scanner configuration options', () => {
    // Arrange
    const config = {
      include: ['domain/**/*.ts'],
      exclude: ['**/*.test.ts', '**/*.spec.ts'],
      outputPath: 'src/generated'
    };
    
    scanner = new SchemaScanner(project, config);

    const domainCode = `
import { defineEvent } from '@sekiban/core';
import { z } from 'zod';

export const DomainEvent = defineEvent({
  type: 'DomainEvent',
  schema: z.object({ id: z.string() })
});
`;

    const testCode = `
import { defineEvent } from '@sekiban/core';
import { z } from 'zod';

export const TestEvent = defineEvent({
  type: 'TestEvent',
  schema: z.object({ id: z.string() })
});
`;

    project.createSourceFile('domain/events.ts', domainCode);
    project.createSourceFile('domain/events.test.ts', testCode);
    project.createSourceFile('other/events.ts', domainCode.replace('DomainEvent', 'OtherEvent'));

    // Act
    const result = scanner.scanForSchemas();

    // Assert
    expect(result.events).toHaveLength(1);
    expect(result.events[0].name).toBe('DomainEvent');
  });
});