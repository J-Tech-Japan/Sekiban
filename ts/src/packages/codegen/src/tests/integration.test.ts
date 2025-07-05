import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { Project } from 'ts-morph';
import { SchemaScanner } from '../scanner.js';
import { CodeGenerator } from '../generator.js';

describe('Integration: Scanner + Generator', () => {
  let project: Project;
  let scanner: SchemaScanner;
  let generator: CodeGenerator;

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
    
    scanner = new SchemaScanner(project, {
      include: ['**/*.ts'],
      exclude: ['**/*.test.ts']
    });
    
    generator = new CodeGenerator({
      outputFile: 'src/generated/domain-registry.ts'
    });
  });

  afterEach(() => {
    // Reset project by removing all source files
    project.getSourceFiles().forEach(file => file.delete());
  });

  // Test 1: End-to-end domain registry generation
  it('generates complete domain registry from domain files', () => {
    // Arrange - Create a complete domain structure
    const userEventsCode = `
import { z } from 'zod';
import { defineEvent } from '@sekiban/core';

export const UserCreated = defineEvent({
  type: 'UserCreated',
  schema: z.object({
    userId: z.string().uuid(),
    name: z.string().min(1),
    email: z.string().email()
  })
});

export const UserUpdated = defineEvent({
  type: 'UserUpdated',
  schema: z.object({
    userId: z.string().uuid(),
    name: z.string().min(1),
    email: z.string().email()
  })
});
`;

    const userCommandsCode = `
import { z } from 'zod';
import { defineCommand } from '@sekiban/core';

export const CreateUser = defineCommand({
  type: 'CreateUser',
  schema: z.object({
    name: z.string().min(1),
    email: z.string().email()
  }),
  handlers: {
    specifyPartitionKeys: () => PartitionKeys.generate('User'),
    validate: () => ok(undefined),
    handle: () => ok([])
  }
});

export const UpdateUser = defineCommand({
  type: 'UpdateUser',
  schema: z.object({
    userId: z.string().uuid(),
    name: z.string().min(1),
    email: z.string().email()
  }),
  handlers: {
    specifyPartitionKeys: (cmd) => PartitionKeys.existing('User', cmd.userId),
    validate: () => ok(undefined),
    handle: () => ok([])
  }
});
`;

    const userProjectorCode = `
import { defineProjector } from '@sekiban/core';
import { EmptyAggregatePayload } from '@sekiban/core';

export const userProjector = defineProjector({
  aggregateType: 'User',
  initialState: () => new EmptyAggregatePayload(),
  projections: {
    UserCreated: (state, event) => ({
      aggregateType: 'User',
      userId: event.userId,
      name: event.name,
      email: event.email,
      version: state.version + 1
    }),
    UserUpdated: (state, event) => ({
      ...state,
      name: event.name,
      email: event.email,
      version: state.version + 1
    })
  }
});
`;

    const orderEventsCode = `
import { z } from 'zod';
import { defineEvent } from '@sekiban/core';

export const OrderPlaced = defineEvent({
  type: 'OrderPlaced',
  schema: z.object({
    orderId: z.string().uuid(),
    userId: z.string().uuid(),
    items: z.array(z.object({
      productId: z.string(),
      quantity: z.number().positive(),
      price: z.number().positive()
    })),
    total: z.number().positive()
  })
});
`;

    // Create domain files
    project.createSourceFile('src/domain/user/events/user-events.ts', userEventsCode);
    project.createSourceFile('src/domain/user/commands/user-commands.ts', userCommandsCode);
    project.createSourceFile('src/domain/user/projector.ts', userProjectorCode);
    project.createSourceFile('src/domain/order/events/order-events.ts', orderEventsCode);

    // Act
    const scannedSchema = scanner.scanForSchemas();
    const generatedCode = generator.generateCode(scannedSchema);

    // Assert - Check scan results
    expect(scannedSchema.events).toHaveLength(3);
    expect(scannedSchema.commands).toHaveLength(2);
    expect(scannedSchema.projectors).toHaveLength(1);

    // Assert - Check generated code structure
    expect(generatedCode.code).toContain("import { UserCreated } from '../domain/user/events/user-events.js';");
    expect(generatedCode.code).toContain("import { UserUpdated } from '../domain/user/events/user-events.js';");
    expect(generatedCode.code).toContain("import { CreateUser } from '../domain/user/commands/user-commands.js';");
    expect(generatedCode.code).toContain("import { UpdateUser } from '../domain/user/commands/user-commands.js';");
    expect(generatedCode.code).toContain("import { userProjector } from '../domain/user/projector.js';");
    expect(generatedCode.code).toContain("import { OrderPlaced } from '../domain/order/events/order-events.js';");

    // Assert - Check registries
    expect(generatedCode.code).toContain('export const events = {');
    expect(generatedCode.code).toContain('UserCreated,');
    expect(generatedCode.code).toContain('UserUpdated,');
    expect(generatedCode.code).toContain('OrderPlaced,');

    expect(generatedCode.code).toContain('export const commands = {');
    expect(generatedCode.code).toContain('CreateUser,');
    expect(generatedCode.code).toContain('UpdateUser,');

    expect(generatedCode.code).toContain('export const projectors = {');
    expect(generatedCode.code).toContain('userProjector,');

    // Assert - Check union types (order may vary)
    expect(generatedCode.code).toMatch(/export type DomainEvent = .*ReturnType<typeof \w+\.create>.*ReturnType<typeof \w+\.create>.*ReturnType<typeof \w+\.create>;/);
    expect(generatedCode.code).toContain('ReturnType<typeof UserCreated.create>');
    expect(generatedCode.code).toContain('ReturnType<typeof UserUpdated.create>');
    expect(generatedCode.code).toContain('ReturnType<typeof OrderPlaced.create>');
    expect(generatedCode.code).toMatch(/export type DomainCommand = .*ReturnType<typeof \w+\.create>.*ReturnType<typeof \w+\.create>;/);
    expect(generatedCode.code).toContain('ReturnType<typeof CreateUser.create>');
    expect(generatedCode.code).toContain('ReturnType<typeof UpdateUser.create>');

    // Assert - Check main registry
    expect(generatedCode.code).toContain('export const domainRegistry = {');
    expect(generatedCode.code).toContain('events,');
    expect(generatedCode.code).toContain('commands,');
    expect(generatedCode.code).toContain('projectors,');
  });

  // Test 2: Handles partial domain implementations
  it('handles domain with only events', () => {
    // Arrange
    const eventsOnlyCode = `
import { z } from 'zod';
import { defineEvent } from '@sekiban/core';

export const UserRegistered = defineEvent({
  type: 'UserRegistered',
  schema: z.object({
    userId: z.string(),
    email: z.string().email()
  })
});
`;

    project.createSourceFile('src/events.ts', eventsOnlyCode);

    // Act
    const scannedSchema = scanner.scanForSchemas();
    const generatedCode = generator.generateCode(scannedSchema);

    // Assert
    expect(scannedSchema.events).toHaveLength(1);
    expect(scannedSchema.commands).toHaveLength(0);
    expect(scannedSchema.projectors).toHaveLength(0);

    expect(generatedCode.code).toContain('export const events = {');
    expect(generatedCode.code).toContain('UserRegistered,');
    expect(generatedCode.code).toContain('export const commands = {} as const;');
    expect(generatedCode.code).toContain('export const projectors = {} as const;');
    expect(generatedCode.code).toContain('export type DomainEvent = ReturnType<typeof UserRegistered.create>;');
    expect(generatedCode.code).toContain('export type DomainCommand = never;');
  });

  // Test 3: Handles empty domain
  it('handles empty domain gracefully', () => {
    // Arrange - Create a file with no domain definitions
    const emptyCode = `
import { z } from 'zod';

// Some other code that doesn't define events/commands/projectors
export const someUtility = () => {};
`;

    project.createSourceFile('src/utils.ts', emptyCode);

    // Act
    const scannedSchema = scanner.scanForSchemas();
    const generatedCode = generator.generateCode(scannedSchema);

    // Assert
    expect(scannedSchema.events).toHaveLength(0);
    expect(scannedSchema.commands).toHaveLength(0);
    expect(scannedSchema.projectors).toHaveLength(0);

    expect(generatedCode.code).toContain('export const events = {} as const;');
    expect(generatedCode.code).toContain('export const commands = {} as const;');
    expect(generatedCode.code).toContain('export const projectors = {} as const;');
    expect(generatedCode.code).toContain('export type DomainEvent = never;');
    expect(generatedCode.code).toContain('export type DomainCommand = never;');
  });

  // Test 4: Respects scanner configuration
  it('respects scanner configuration for file filtering', () => {
    // Arrange
    const domainCode = `
import { z } from 'zod';
import { defineEvent } from '@sekiban/core';

export const DomainEvent = defineEvent({
  type: 'DomainEvent',
  schema: z.object({ id: z.string() })
});
`;

    const testCode = `
import { z } from 'zod';
import { defineEvent } from '@sekiban/core';

export const TestEvent = defineEvent({
  type: 'TestEvent',
  schema: z.object({ id: z.string() })
});
`;

    project.createSourceFile('src/domain.ts', domainCode);
    project.createSourceFile('src/domain.test.ts', testCode);

    // Act
    const scannedSchema = scanner.scanForSchemas();
    const generatedCode = generator.generateCode(scannedSchema);

    // Assert - Should only include domain.ts, not domain.test.ts
    expect(scannedSchema.events).toHaveLength(1);
    expect(scannedSchema.events[0].name).toBe('DomainEvent');
    expect(generatedCode.code).toContain('DomainEvent,');
    expect(generatedCode.code).not.toContain('TestEvent');
  });

  // Test 5: Generates valid TypeScript
  it('generates syntactically valid TypeScript', () => {
    // Arrange
    const sampleCode = `
import { z } from 'zod';
import { defineEvent, defineCommand } from '@sekiban/core';

export const SampleEvent = defineEvent({
  type: 'SampleEvent',
  schema: z.object({ id: z.string() })
});

export const SampleCommand = defineCommand({
  type: 'SampleCommand',
  schema: z.object({ name: z.string() }),
  handlers: {}
});
`;

    project.createSourceFile('src/sample.ts', sampleCode);

    // Act
    const scannedSchema = scanner.scanForSchemas();
    const generatedCode = generator.generateCode(scannedSchema);

    // Assert - Create a new project and validate the generated code
    const validationProject = new Project({
      useInMemoryFileSystem: true,
      compilerOptions: {
        target: 99,
        module: 99,
        strict: true,
        noEmit: true
      }
    });

    // This should not throw if the TypeScript is valid
    expect(() => {
      validationProject.createSourceFile('generated.ts', generatedCode.code);
    }).not.toThrow();

    // Check for basic TypeScript syntax
    expect(generatedCode.code).not.toContain('export const events = {,}'); // No trailing commas in empty objects
    expect(generatedCode.code).not.toContain('undefined,'); // No undefined values
    expect(generatedCode.code).toMatch(/export const \w+ = \{[\s\S]*\} as const;/); // Proper const assertions
  });

  // Test 6: Performance with many domain objects
  it('handles large domain efficiently', () => {
    // Arrange - Create many domain objects
    const eventCount = 50;
    const commandCount = 30;
    
    for (let i = 0; i < eventCount; i++) {
      const eventCode = `
import { z } from 'zod';
import { defineEvent } from '@sekiban/core';

export const Event${i} = defineEvent({
  type: 'Event${i}',
  schema: z.object({ id: z.string(), data: z.number() })
});
`;
      project.createSourceFile(`src/events/event-${i}.ts`, eventCode);
    }

    for (let i = 0; i < commandCount; i++) {
      const commandCode = `
import { z } from 'zod';
import { defineCommand } from '@sekiban/core';

export const Command${i} = defineCommand({
  type: 'Command${i}',
  schema: z.object({ name: z.string() }),
  handlers: {}
});
`;
      project.createSourceFile(`src/commands/command-${i}.ts`, commandCode);
    }

    // Act
    const startTime = Date.now();
    const scannedSchema = scanner.scanForSchemas();
    const generatedCode = generator.generateCode(scannedSchema);
    const endTime = Date.now();

    // Assert
    expect(scannedSchema.events).toHaveLength(eventCount);
    expect(scannedSchema.commands).toHaveLength(commandCount);
    expect(endTime - startTime).toBeLessThan(5000); // Should complete within 5 seconds

    // Verify the generated code includes all items
    for (let i = 0; i < eventCount; i++) {
      expect(generatedCode.code).toContain(`Event${i},`);
    }
    for (let i = 0; i < commandCount; i++) {
      expect(generatedCode.code).toContain(`Command${i},`);
    }
  });
});