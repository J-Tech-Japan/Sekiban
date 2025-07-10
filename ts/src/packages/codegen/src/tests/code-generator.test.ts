import { describe, it, expect, beforeEach } from 'vitest';
import { CodeGenerator } from '../generator.js';
import type { ScannedSchema } from '../types.js';

describe('CodeGenerator', () => {
  let generator: CodeGenerator;
  
  beforeEach(() => {
    generator = new CodeGenerator({
      outputFile: 'src/generated/domain-registry.ts'
    });
  });

  // Test 1: Creates import statements
  it('creates import statements', () => {
    // Arrange
    const scannedSchema: ScannedSchema = {
      events: [
        {
          name: 'UserCreated',
          type: 'UserCreated',
          filePath: '/src/domain/user/events/user-created.ts',
          importPath: '../domain/user/events/user-created.js'
        }
      ],
      commands: [
        {
          name: 'CreateUser',
          type: 'CreateUser',
          filePath: '/src/domain/user/commands/create-user.ts',
          importPath: '../domain/user/commands/create-user.js'
        }
      ],
      projectors: [
        {
          name: 'userProjector',
          aggregateType: 'User',
          filePath: '/src/domain/user/projector.ts',
          importPath: '../domain/user/projector.js'
        }
      ],
      summary: {
        totalEvents: 1,
        totalCommands: 1,
        totalProjectors: 1,
        totalFiles: 3,
        scanDurationMs: 5
      }
    };

    // Act
    const result = generator.generateCode(scannedSchema);

    // Assert
    expect(result.code).toContain("import { UserCreated } from '../domain/user/events/user-created.js';");
    expect(result.code).toContain("import { CreateUser } from '../domain/user/commands/create-user.js';");
    expect(result.code).toContain("import { userProjector } from '../domain/user/projector.js';");
  });

  // Test 2: Creates event registry object
  it('creates event registry object', () => {
    // Arrange
    const scannedSchema: ScannedSchema = {
      events: [
        {
          name: 'UserCreated',
          type: 'UserCreated',
          filePath: '/src/domain/user/events/user-created.ts',
          importPath: '../domain/user/events/user-created.js'
        },
        {
          name: 'UserUpdated',
          type: 'UserUpdated',
          filePath: '/src/domain/user/events/user-updated.ts',
          importPath: '../domain/user/events/user-updated.js'
        }
      ],
      commands: [],
      projectors: [],
      summary: {
        totalEvents: 2,
        totalCommands: 0,
        totalProjectors: 0,
        totalFiles: 2,
        scanDurationMs: 3
      }
    };

    // Act
    const result = generator.generateCode(scannedSchema);

    // Assert
    expect(result.code).toContain('export const events = {');
    expect(result.code).toContain('UserCreated,');
    expect(result.code).toContain('UserUpdated,');
    expect(result.code).toContain('} as const;');
  });

  // Test 3: Creates command registry object
  it('creates command registry object', () => {
    // Arrange
    const scannedSchema: ScannedSchema = {
      events: [],
      commands: [
        {
          name: 'CreateUser',
          type: 'CreateUser',
          filePath: '/src/domain/user/commands/create-user.ts',
          importPath: '../domain/user/commands/create-user.js'
        },
        {
          name: 'UpdateUser',
          type: 'UpdateUser',
          filePath: '/src/domain/user/commands/update-user.ts',
          importPath: '../domain/user/commands/update-user.js'
        }
      ],
      projectors: [],
      summary: {
        totalEvents: 0,
        totalCommands: 2,
        totalProjectors: 0,
        totalFiles: 2,
        scanDurationMs: 3
      }
    };

    // Act
    const result = generator.generateCode(scannedSchema);

    // Assert
    expect(result.code).toContain('export const commands = {');
    expect(result.code).toContain('CreateUser,');
    expect(result.code).toContain('UpdateUser,');
    expect(result.code).toContain('} as const;');
  });

  // Test 4: Creates projector registry object
  it('creates projector registry object', () => {
    // Arrange
    const scannedSchema: ScannedSchema = {
      events: [],
      commands: [],
      projectors: [
        {
          name: 'userProjector',
          aggregateType: 'User',
          filePath: '/src/domain/user/projector.ts',
          importPath: '../domain/user/projector.js'
        },
        {
          name: 'orderProjector',
          aggregateType: 'Order',
          filePath: '/src/domain/order/projector.ts',
          importPath: '../domain/order/projector.js'
        }
      ],
      summary: {
        totalEvents: 0,
        totalCommands: 0,
        totalProjectors: 2,
        totalFiles: 2,
        scanDurationMs: 3
      }
    };

    // Act
    const result = generator.generateCode(scannedSchema);

    // Assert
    expect(result.code).toContain('export const projectors = {');
    expect(result.code).toContain('userProjector,');
    expect(result.code).toContain('orderProjector,');
    expect(result.code).toContain('} as const;');
  });

  // Test 5: Creates type exports
  it('creates type exports', () => {
    // Arrange
    const scannedSchema: ScannedSchema = {
      events: [
        {
          name: 'UserCreated',
          type: 'UserCreated',
          filePath: '/src/domain/user/events/user-created.ts',
          importPath: '../domain/user/events/user-created.js'
        }
      ],
      commands: [
        {
          name: 'CreateUser',
          type: 'CreateUser',
          filePath: '/src/domain/user/commands/create-user.ts',
          importPath: '../domain/user/commands/create-user.js'
        }
      ],
      projectors: [
        {
          name: 'userProjector',
          aggregateType: 'User',
          filePath: '/src/domain/user/projector.ts',
          importPath: '../domain/user/projector.js'
        }
      ],
      summary: {
        totalEvents: 1,
        totalCommands: 1,
        totalProjectors: 1,
        totalFiles: 3,
        scanDurationMs: 5
      }
    };

    // Act
    const result = generator.generateCode(scannedSchema);

    // Assert
    expect(result.code).toContain('export type EventTypes = typeof events;');
    expect(result.code).toContain('export type CommandTypes = typeof commands;');
    expect(result.code).toContain('export type ProjectorTypes = typeof projectors;');
  });

  // Test 6: Creates union types
  it('creates union types', () => {
    // Arrange
    const scannedSchema: ScannedSchema = {
      events: [
        {
          name: 'UserCreated',
          type: 'UserCreated',
          filePath: '/src/domain/user/events/user-created.ts',
          importPath: '../domain/user/events/user-created.js'
        },
        {
          name: 'UserUpdated',
          type: 'UserUpdated',
          filePath: '/src/domain/user/events/user-updated.ts',
          importPath: '../domain/user/events/user-updated.js'
        }
      ],
      commands: [
        {
          name: 'CreateUser',
          type: 'CreateUser',
          filePath: '/src/domain/user/commands/create-user.ts',
          importPath: '../domain/user/commands/create-user.js'
        }
      ],
      projectors: [],
      summary: {
        totalEvents: 2,
        totalCommands: 1,
        totalProjectors: 0,
        totalFiles: 3,
        scanDurationMs: 5
      }
    };

    // Act
    const result = generator.generateCode(scannedSchema);

    // Assert
    expect(result.code).toContain('export type DomainEvent = ReturnType<typeof UserCreated.create> | ReturnType<typeof UserUpdated.create>;');
    expect(result.code).toContain('export type DomainCommand = ReturnType<typeof CreateUser.create>;');
  });

  // Test 7: Handles empty registries
  it('handles empty registries', () => {
    // Arrange
    const scannedSchema: ScannedSchema = {
      events: [],
      commands: [],
      projectors: [],
      summary: {
        totalEvents: 0,
        totalCommands: 0,
        totalProjectors: 0,
        totalFiles: 0,
        scanDurationMs: 1
      }
    };

    // Act
    const result = generator.generateCode(scannedSchema);

    // Assert
    expect(result.code).toContain('export const events = {} as const;');
    expect(result.code).toContain('export const commands = {} as const;');
    expect(result.code).toContain('export const projectors = {} as const;');
    expect(result.code).toContain('export type DomainEvent = never;');
    expect(result.code).toContain('export type DomainCommand = never;');
  });

  // Test 8: Generates complete registry file
  it('generates complete registry file', () => {
    // Arrange
    const scannedSchema: ScannedSchema = {
      events: [
        {
          name: 'UserCreated',
          type: 'UserCreated',
          filePath: '/src/domain/user/events/user-created.ts',
          importPath: '../domain/user/events/user-created.js'
        }
      ],
      commands: [
        {
          name: 'CreateUser',
          type: 'CreateUser',
          filePath: '/src/domain/user/commands/create-user.ts',
          importPath: '../domain/user/commands/create-user.js'
        }
      ],
      projectors: [
        {
          name: 'userProjector',
          aggregateType: 'User',
          filePath: '/src/domain/user/projector.ts',
          importPath: '../domain/user/projector.js'
        }
      ],
      summary: {
        totalEvents: 1,
        totalCommands: 1,
        totalProjectors: 1,
        totalFiles: 3,
        scanDurationMs: 5
      }
    };

    // Act
    const result = generator.generateCode(scannedSchema);

    // Assert
    expect(result.code).toContain('// Generated domain registry');
    expect(result.code).toContain('// DO NOT EDIT - This file is auto-generated');
    expect(result.code).toContain('export const domainRegistry = {');
    expect(result.code).toContain('events,');
    expect(result.code).toContain('commands,');
    expect(result.code).toContain('projectors,');
    expect(result.code).toContain('} as const;');
  });

  // Test 9: Adds proper TypeScript declarations
  it('adds proper TypeScript declarations', () => {
    // Arrange
    const scannedSchema: ScannedSchema = {
      events: [
        {
          name: 'UserCreated',
          type: 'UserCreated',
          filePath: '/src/domain/user/events/user-created.ts',
          importPath: '../domain/user/events/user-created.js'
        }
      ],
      commands: [],
      projectors: [],
      summary: {
        totalEvents: 1,
        totalCommands: 0,
        totalProjectors: 0,
        totalFiles: 1,
        scanDurationMs: 2
      }
    };

    // Act
    const result = generator.generateCode(scannedSchema);

    // Assert
    expect(result.code).toContain('export type DomainRegistryType = typeof domainRegistry;');
    expect(result.code).toContain('export type EventTypeLookup = {');
    expect(result.code).toContain('[K in keyof EventTypes]: ReturnType<EventTypes[K]["create"]>');
    expect(result.code).toContain('};');
  });

  // Test 10: Includes generation metadata
  it('includes generation metadata', () => {
    // Arrange
    const scannedSchema: ScannedSchema = {
      events: [],
      commands: [],
      projectors: [],
      summary: {
        totalEvents: 0,
        totalCommands: 0,
        totalProjectors: 0,
        totalFiles: 0,
        scanDurationMs: 1
      }
    };

    // Act
    const result = generator.generateCode(scannedSchema);

    // Assert
    expect(result.code).toContain('Generated on:');
    expect(result.code).toContain('Total files scanned: 0');
    expect(result.code).toContain('Total events: 0');
    expect(result.code).toContain('Total commands: 0');
    expect(result.code).toContain('Total projectors: 0');
    expect(result.code).toContain('Scan duration:');
  });

  // Test 11: Handles configuration options
  it('handles configuration options', () => {
    // Arrange
    const customGenerator = new CodeGenerator({
      outputFile: 'custom/output.ts',
      includeComments: false,
      template: 'minimal'
    });
    
    const scannedSchema: ScannedSchema = {
      events: [
        {
          name: 'UserCreated',
          type: 'UserCreated',
          filePath: '/src/domain/user/events/user-created.ts',
          importPath: '../domain/user/events/user-created.js'
        }
      ],
      commands: [],
      projectors: [],
      summary: {
        totalEvents: 1,
        totalCommands: 0,
        totalProjectors: 0,
        totalFiles: 1,
        scanDurationMs: 2
      }
    };

    // Act
    const result = customGenerator.generateCode(scannedSchema);

    // Assert
    // With minimal template and no comments, should have less boilerplate
    expect(result.code).not.toContain('// Generated domain registry');
    expect(result.code).toContain('export const events = {');
    expect(result.code).toContain('UserCreated,');
  });

  // Test 12: Generates proper file structure
  it('generates proper file structure', () => {
    // Arrange
    const scannedSchema: ScannedSchema = {
      events: [
        {
          name: 'UserCreated',
          type: 'UserCreated',
          filePath: '/src/domain/user/events/user-created.ts',
          importPath: '../domain/user/events/user-created.js'
        }
      ],
      commands: [],
      projectors: [],
      summary: {
        totalEvents: 1,
        totalCommands: 0,
        totalProjectors: 0,
        totalFiles: 1,
        scanDurationMs: 2
      }
    };

    // Act
    const result = generator.generateCode(scannedSchema);

    // Assert - Check the overall structure
    const lines = result.code.split('\n').filter(line => line.trim());
    
    // Should start with header comments
    expect(lines[0]).toContain('Generated domain registry');
    
    // Should have imports section
    expect(result.code.indexOf('import')).toBeLessThan(result.code.indexOf('export const events'));
    
    // Should have registries before types
    expect(result.code.indexOf('export const events')).toBeLessThan(result.code.indexOf('export type EventTypes'));
    
    // Should end with main domain registry export
    expect(result.code.indexOf('export const domainRegistry')).toBeGreaterThan(result.code.indexOf('export type'));
  });
});