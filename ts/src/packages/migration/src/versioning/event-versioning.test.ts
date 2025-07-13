import { describe, it, expect, beforeEach } from 'vitest';
import { z } from 'zod';
import { SchemaRegistry, CompatibilityMode } from '../schema/schema-registry';
import { UpcasterRegistry } from '../upcaster';
import { 
  EventVersioningSystem,
  VersionedEvent,
  EventVersioningConfig,
  VersioningStrategy
} from './event-versioning';

// Test event schemas
const UserCreatedV1Schema = z.object({
  fullName: z.string(),
  email: z.string().email(),
});

const UserCreatedV2Schema = z.object({
  firstName: z.string(),
  lastName: z.string(),
  email: z.string().email(),
});

const UserCreatedV3Schema = z.object({
  firstName: z.string(),
  lastName: z.string(),
  email: z.string().email(),
  createdAt: z.date(),
  tags: z.array(z.string()).optional(),
});

describe('EventVersioningSystem', () => {
  let schemaRegistry: SchemaRegistry;
  let upcasterRegistry: UpcasterRegistry;
  let versioningSystem: EventVersioningSystem;

  beforeEach(() => {
    schemaRegistry = new SchemaRegistry();
    upcasterRegistry = new UpcasterRegistry();
    
    // Register schemas
    schemaRegistry.register({
      eventType: 'UserCreated',
      version: 1,
      schema: UserCreatedV1Schema,
      description: 'Initial user creation event',
    });

    schemaRegistry.register({
      eventType: 'UserCreated',
      version: 2,
      schema: UserCreatedV2Schema,
      description: 'Split fullName into firstName and lastName',
    });

    schemaRegistry.register({
      eventType: 'UserCreated',
      version: 3,
      schema: UserCreatedV3Schema,
      description: 'Added createdAt and tags fields',
    });

    // Register upcasters
    upcasterRegistry.register({
      eventType: 'UserCreated',
      fromVersion: 1,
      toVersion: 2,
      upcast: (payload: any) => {
        const [firstName, lastName] = payload.fullName.split(' ');
        const { fullName, ...rest } = payload;
        return {
          firstName: firstName || '',
          lastName: lastName || '',
          ...rest,
        };
      },
    });

    upcasterRegistry.register({
      eventType: 'UserCreated',
      fromVersion: 2,
      toVersion: 3,
      upcast: (payload: any) => ({
        ...payload,
        createdAt: new Date('2024-01-01T00:00:00Z'),
        tags: [],
      }),
    });

    // Create versioning system
    const config: EventVersioningConfig = {
      defaultCompatibilityMode: CompatibilityMode.BACKWARD,
      validateOnWrite: true,
      validateOnRead: true,
      strategy: VersioningStrategy.UPCAST_TO_LATEST,
    };

    versioningSystem = new EventVersioningSystem(
      schemaRegistry,
      upcasterRegistry,
      config
    );
  });

  describe('Event Processing', () => {
    it('should process and validate a current version event', async () => {
      const event: VersionedEvent = {
        eventType: 'UserCreated',
        version: 3,
        payload: {
          firstName: 'John',
          lastName: 'Doe',
          email: 'john@example.com',
          createdAt: new Date(),
          tags: ['vip'],
        },
      };

      const result = await versioningSystem.processEvent(event);
      
      expect(result.isOk()).toBe(true);
      if (result.isOk()) {
        expect(result.value.version).toBe(3);
        expect(result.value.payload.firstName).toBe('John');
      }
    });

    it('should upcast old version to latest', async () => {
      const event: VersionedEvent = {
        eventType: 'UserCreated',
        version: 1,
        payload: {
          fullName: 'Jane Smith',
          email: 'jane@example.com',
        },
      };

      const result = await versioningSystem.processEvent(event);
      
      expect(result.isOk()).toBe(true);
      if (result.isOk()) {
        expect(result.value.version).toBe(3);
        expect(result.value.payload.firstName).toBe('Jane');
        expect(result.value.payload.lastName).toBe('Smith');
        expect(result.value.payload.createdAt).toBeInstanceOf(Date);
        expect(result.value.payload.tags).toEqual([]);
      }
    });

    it('should reject invalid payload', async () => {
      const event: VersionedEvent = {
        eventType: 'UserCreated',
        version: 3,
        payload: {
          firstName: 'John',
          // Missing required fields
        },
      };

      const result = await versioningSystem.processEvent(event);
      
      expect(result.isErr()).toBe(true);
      if (result.isErr()) {
        expect(result.error.message).toContain('validation failed');
      }
    });
  });

  describe('Compatibility Checking', () => {
    it('should check backward compatibility before registration', async () => {
      const newSchema = {
        eventType: 'UserCreated',
        version: 4,
        schema: z.object({
          firstName: z.string(),
          lastName: z.string(),
          email: z.string().email(),
          createdAt: z.date(),
          tags: z.array(z.string()).optional(),
          preferences: z.object({
            newsletter: z.boolean(),
          }).optional(),
        }),
      };

      const result = await versioningSystem.registerSchema(newSchema);
      
      expect(result.isOk()).toBe(true);
    });

    it('should reject breaking changes in backward compatibility mode', async () => {
      const breakingSchema = {
        eventType: 'UserCreated',
        version: 4,
        schema: z.object({
          firstName: z.string(),
          lastName: z.string(),
          // email field removed - breaking change!
          createdAt: z.date(),
        }),
      };

      const result = await versioningSystem.registerSchema(breakingSchema);
      
      expect(result.isErr()).toBe(true);
      if (result.isErr()) {
        expect(result.error.message).toContain('compatibility');
      }
    });
  });

  describe('Versioning Strategies', () => {
    it('should keep original version with KEEP_ORIGINAL strategy', async () => {
      const config: EventVersioningConfig = {
        defaultCompatibilityMode: CompatibilityMode.BACKWARD,
        validateOnWrite: true,
        validateOnRead: true,
        strategy: VersioningStrategy.KEEP_ORIGINAL,
      };

      const system = new EventVersioningSystem(
        schemaRegistry,
        upcasterRegistry,
        config
      );

      const event: VersionedEvent = {
        eventType: 'UserCreated',
        version: 1,
        payload: {
          fullName: 'Test User',
          email: 'test@example.com',
        },
      };

      const result = await system.processEvent(event);
      
      expect(result.isOk()).toBe(true);
      if (result.isOk()) {
        expect(result.value.version).toBe(1);
        expect(result.value.payload.fullName).toBe('Test User');
      }
    });

    it('should upcast to specific version with UPCAST_TO_VERSION strategy', async () => {
      const config: EventVersioningConfig = {
        defaultCompatibilityMode: CompatibilityMode.BACKWARD,
        validateOnWrite: true,
        validateOnRead: true,
        strategy: VersioningStrategy.UPCAST_TO_VERSION,
        targetVersion: 2,
      };

      const system = new EventVersioningSystem(
        schemaRegistry,
        upcasterRegistry,
        config
      );

      const event: VersionedEvent = {
        eventType: 'UserCreated',
        version: 1,
        payload: {
          fullName: 'Target Test',
          email: 'target@example.com',
        },
      };

      const result = await system.processEvent(event);
      
      expect(result.isOk()).toBe(true);
      if (result.isOk()) {
        expect(result.value.version).toBe(2);
        expect(result.value.payload.firstName).toBe('Target');
        expect(result.value.payload.lastName).toBe('Test');
        // Should not have v3 fields
        expect(result.value.payload.createdAt).toBeUndefined();
      }
    });
  });

  describe('Batch Processing', () => {
    it('should process multiple events efficiently', async () => {
      const events: VersionedEvent[] = [
        {
          eventType: 'UserCreated',
          version: 1,
          payload: { fullName: 'User One', email: 'one@example.com' },
        },
        {
          eventType: 'UserCreated',
          version: 2,
          payload: { firstName: 'User', lastName: 'Two', email: 'two@example.com' },
        },
        {
          eventType: 'UserCreated',
          version: 3,
          payload: {
            firstName: 'User',
            lastName: 'Three',
            email: 'three@example.com',
            createdAt: new Date(),
            tags: ['batch'],
          },
        },
      ];

      const results = await versioningSystem.processEvents(events);
      
      expect(results).toHaveLength(3);
      expect(results.every(r => r.isOk())).toBe(true);
      
      // All should be upcasted to v3
      const versions = results
        .filter(r => r.isOk())
        .map(r => r._unsafeUnwrap().version);
      expect(versions).toEqual([3, 3, 3]);
    });
  });

  describe('Schema Evolution Helpers', () => {
    it('should generate migration code for schema changes', () => {
      const migration = versioningSystem.generateMigrationCode(
        'UserCreated',
        1,
        2
      );

      expect(migration).toContain('fromVersion: 1');
      expect(migration).toContain('toVersion: 2');
      expect(migration).toContain('upcast:');
    });

    it('should validate migration path exists', () => {
      const result = versioningSystem.canMigrate('UserCreated', 1, 3);
      expect(result).toBe(true);

      const noPath = versioningSystem.canMigrate('UserCreated', 3, 1);
      expect(noPath).toBe(false);
    });

    it('should get schema changelog', () => {
      const changelog = versioningSystem.getSchemaChangelog('UserCreated');
      
      expect(changelog).toHaveLength(3);
      expect(changelog[0].version).toBe(1);
      expect(changelog[0].description).toContain('Initial');
      expect(changelog[1].version).toBe(2);
      expect(changelog[2].version).toBe(3);
    });
  });

  describe('Error Handling', () => {
    it('should handle unknown event types gracefully', async () => {
      const event: VersionedEvent = {
        eventType: 'UnknownEvent',
        version: 1,
        payload: { data: 'test' },
      };

      const result = await versioningSystem.processEvent(event);
      
      expect(result.isErr()).toBe(true);
      if (result.isErr()) {
        expect(result.error.message).toContain('Schema not found');
      }
    });

    it('should handle missing upcaster gracefully', async () => {
      // Register a v4 schema without upcaster from v3
      schemaRegistry.register({
        eventType: 'UserCreated',
        version: 4,
        schema: z.object({
          firstName: z.string(),
          lastName: z.string(),
          email: z.string().email(),
          createdAt: z.date(),
          tags: z.array(z.string()).optional(),
          newField: z.string(),
        }),
      });

      const event: VersionedEvent = {
        eventType: 'UserCreated',
        version: 3,
        payload: {
          firstName: 'Test',
          lastName: 'User',
          email: 'test@example.com',
          createdAt: new Date(),
          tags: [],
        },
      };

      const result = await versioningSystem.processEvent(event);
      
      // Should still work - returns v3 since no upcaster to v4
      expect(result.isOk()).toBe(true);
      if (result.isOk()) {
        expect(result.value.version).toBe(3);
      }
    });
  });
});