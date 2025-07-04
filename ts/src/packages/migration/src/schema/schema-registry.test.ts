import { describe, it, expect, beforeEach } from 'vitest';
import { 
  SchemaRegistry, 
  EventSchema, 
  SchemaVersion,
  CompatibilityMode,
  SchemaValidationError
} from './schema-registry';
import { z } from 'zod';

describe('SchemaRegistry', () => {
  let registry: SchemaRegistry;

  beforeEach(() => {
    registry = new SchemaRegistry();
  });

  describe('Schema Registration', () => {
    it('should register a new event schema', () => {
      const schema: EventSchema = {
        eventType: 'UserCreated',
        version: 1,
        schema: z.object({
          name: z.string(),
          email: z.string().email(),
        }),
        description: 'User creation event',
      };

      registry.register(schema);

      const retrieved = registry.getSchema('UserCreated', 1);
      expect(retrieved).toEqual(schema);
    });

    it('should prevent registering duplicate schema versions', () => {
      const schema: EventSchema = {
        eventType: 'UserCreated',
        version: 1,
        schema: z.object({
          name: z.string(),
        }),
      };

      registry.register(schema);
      
      expect(() => registry.register(schema)).toThrow(
        'Schema for UserCreated v1 already exists'
      );
    });

    it('should allow registering different versions of same event type', () => {
      const v1: EventSchema = {
        eventType: 'UserCreated',
        version: 1,
        schema: z.object({
          fullName: z.string(),
          email: z.string().email(),
        }),
      };

      const v2: EventSchema = {
        eventType: 'UserCreated',
        version: 2,
        schema: z.object({
          firstName: z.string(),
          lastName: z.string(),
          email: z.string().email(),
        }),
      };

      registry.register(v1);
      registry.register(v2);

      expect(registry.getSchema('UserCreated', 1)).toEqual(v1);
      expect(registry.getSchema('UserCreated', 2)).toEqual(v2);
    });
  });

  describe('Schema Validation', () => {
    beforeEach(() => {
      const schema: EventSchema = {
        eventType: 'UserCreated',
        version: 1,
        schema: z.object({
          name: z.string(),
          email: z.string().email(),
          age: z.number().min(0).optional(),
        }),
      };
      registry.register(schema);
    });

    it('should validate valid event payload', () => {
      const payload = {
        name: 'John Doe',
        email: 'john@example.com',
      };

      const result = registry.validatePayload('UserCreated', 1, payload);
      expect(result.isValid).toBe(true);
      expect(result.data).toEqual(payload);
    });

    it('should reject invalid event payload', () => {
      const payload = {
        name: 'John Doe',
        email: 'invalid-email',
      };

      const result = registry.validatePayload('UserCreated', 1, payload);
      expect(result.isValid).toBe(false);
      expect(result.errors).toBeDefined();
    });

    it('should handle optional fields correctly', () => {
      const payload = {
        name: 'John Doe',
        email: 'john@example.com',
        age: 25,
      };

      const result = registry.validatePayload('UserCreated', 1, payload);
      expect(result.isValid).toBe(true);
      expect(result.data).toEqual(payload);
    });
  });

  describe('Schema Compatibility', () => {
    it('should check backward compatibility - adding optional field is OK', () => {
      const v1: EventSchema = {
        eventType: 'UserCreated',
        version: 1,
        schema: z.object({
          name: z.string(),
          email: z.string(),
        }),
      };

      const v2: EventSchema = {
        eventType: 'UserCreated',
        version: 2,
        schema: z.object({
          name: z.string(),
          email: z.string(),
          age: z.number().optional(), // New optional field
        }),
      };

      registry.register(v1);

      const result = registry.checkCompatibility(v2, CompatibilityMode.BACKWARD);
      expect(result.isCompatible).toBe(true);
    });

    it('should check backward compatibility - removing field breaks it', () => {
      const v1: EventSchema = {
        eventType: 'UserCreated',
        version: 1,
        schema: z.object({
          name: z.string(),
          email: z.string(),
        }),
      };

      const v2: EventSchema = {
        eventType: 'UserCreated',
        version: 2,
        schema: z.object({
          name: z.string(), // email field removed
        }),
      };

      registry.register(v1);

      const result = registry.checkCompatibility(v2, CompatibilityMode.BACKWARD);
      expect(result.isCompatible).toBe(false);
      expect(result.errors).toContain('Field "email" was removed');
    });

    it('should check forward compatibility - removing optional field is OK', () => {
      const v1: EventSchema = {
        eventType: 'UserCreated',
        version: 1,
        schema: z.object({
          name: z.string(),
          email: z.string(),
          age: z.number().optional(),
        }),
      };

      const v2: EventSchema = {
        eventType: 'UserCreated',
        version: 2,
        schema: z.object({
          name: z.string(),
          email: z.string(),
        }),
      };

      registry.register(v1);

      const result = registry.checkCompatibility(v2, CompatibilityMode.FORWARD);
      expect(result.isCompatible).toBe(true);
    });

    it('should check full compatibility - no breaking changes allowed', () => {
      const v1: EventSchema = {
        eventType: 'UserCreated',
        version: 1,
        schema: z.object({
          name: z.string(),
          email: z.string(),
        }),
      };

      const v2: EventSchema = {
        eventType: 'UserCreated',
        version: 2,
        schema: z.object({
          name: z.string(),
          email: z.string(),
          createdAt: z.date().optional(),
        }),
      };

      registry.register(v1);

      const result = registry.checkCompatibility(v2, CompatibilityMode.FULL);
      expect(result.isCompatible).toBe(true);
    });
  });

  describe('Schema Evolution', () => {
    it('should get latest version of event type', () => {
      registry.register({
        eventType: 'UserCreated',
        version: 1,
        schema: z.object({ name: z.string() }),
      });

      registry.register({
        eventType: 'UserCreated',
        version: 3,
        schema: z.object({ name: z.string() }),
      });

      registry.register({
        eventType: 'UserCreated',
        version: 2,
        schema: z.object({ name: z.string() }),
      });

      expect(registry.getLatestVersion('UserCreated')).toBe(3);
    });

    it('should list all versions of event type', () => {
      registry.register({
        eventType: 'UserCreated',
        version: 1,
        schema: z.object({ v: z.literal(1) }),
      });

      registry.register({
        eventType: 'UserCreated',
        version: 2,
        schema: z.object({ v: z.literal(2) }),
      });

      const versions = registry.getVersions('UserCreated');
      expect(versions).toEqual([1, 2]);
    });

    it('should generate migration guide between versions', () => {
      const v1: EventSchema = {
        eventType: 'UserCreated',
        version: 1,
        schema: z.object({
          fullName: z.string(),
          email: z.string(),
        }),
      };

      const v2: EventSchema = {
        eventType: 'UserCreated',
        version: 2,
        schema: z.object({
          firstName: z.string(),
          lastName: z.string(),
          email: z.string(),
        }),
      };

      registry.register(v1);
      registry.register(v2);

      const guide = registry.getMigrationGuide('UserCreated', 1, 2);
      expect(guide.removedFields).toContain('fullName');
      expect(guide.addedFields).toContain('firstName');
      expect(guide.addedFields).toContain('lastName');
      expect(guide.unchangedFields).toContain('email');
    });
  });

  describe('Schema Metadata', () => {
    it('should store and retrieve schema metadata', () => {
      const schema: EventSchema = {
        eventType: 'UserCreated',
        version: 1,
        schema: z.object({
          name: z.string(),
        }),
        description: 'Creates a new user in the system',
        deprecated: false,
        deprecationReason: undefined,
        createdAt: new Date('2024-01-01'),
        createdBy: 'john.doe',
      };

      registry.register(schema);
      
      const retrieved = registry.getSchema('UserCreated', 1);
      expect(retrieved?.description).toBe('Creates a new user in the system');
      expect(retrieved?.createdBy).toBe('john.doe');
    });

    it('should mark schemas as deprecated', () => {
      const schema: EventSchema = {
        eventType: 'UserCreatedV1',
        version: 1,
        schema: z.object({ name: z.string() }),
      };

      registry.register(schema);
      registry.deprecateSchema('UserCreatedV1', 1, 'Use UserCreatedV2 instead');

      const retrieved = registry.getSchema('UserCreatedV1', 1);
      expect(retrieved?.deprecated).toBe(true);
      expect(retrieved?.deprecationReason).toBe('Use UserCreatedV2 instead');
    });
  });

  describe('Schema Export/Import', () => {
    it('should export all schemas', () => {
      registry.register({
        eventType: 'UserCreated',
        version: 1,
        schema: z.object({ name: z.string() }),
      });

      registry.register({
        eventType: 'OrderPlaced',
        version: 1,
        schema: z.object({ orderId: z.string() }),
      });

      const exported = registry.exportSchemas();
      expect(exported).toHaveLength(2);
      expect(exported.map(s => s.eventType)).toContain('UserCreated');
      expect(exported.map(s => s.eventType)).toContain('OrderPlaced');
    });

    it('should import schemas', () => {
      const schemas: EventSchema[] = [
        {
          eventType: 'UserCreated',
          version: 1,
          schema: z.object({ name: z.string() }),
        },
        {
          eventType: 'OrderPlaced',
          version: 1,
          schema: z.object({ orderId: z.string() }),
        },
      ];

      registry.importSchemas(schemas);

      expect(registry.getSchema('UserCreated', 1)).toBeDefined();
      expect(registry.getSchema('OrderPlaced', 1)).toBeDefined();
    });
  });
});