import { describe, it, expect } from 'vitest';
import { z } from 'zod';
import { defineEvent } from '../event-schema';

describe('defineEvent', () => {
  // Test 1: Basic event definition creation
  it('creates event definition with type property', () => {
    // Arrange
    const definition = {
      type: 'UserCreated' as const,
      schema: z.object({ userId: z.string() })
    };

    // Act
    const result = defineEvent(definition);

    // Assert
    expect(result.type).toBe('UserCreated');
  });

  // Test 2: Schema validation for correct data
  it('schema validates correct data', () => {
    // Arrange
    const UserCreated = defineEvent({
      type: 'UserCreated',
      schema: z.object({ 
        userId: z.string(),
        name: z.string().min(1)
      })
    });
    const validData = { userId: '123', name: 'John Doe' };

    // Act
    const result = UserCreated.schema.safeParse(validData);

    // Assert
    expect(result.success).toBe(true);
  });

  // Test 3: Schema validation for invalid data
  it('schema rejects invalid data', () => {
    // Arrange
    const UserCreated = defineEvent({
      type: 'UserCreated',
      schema: z.object({ 
        userId: z.string(),
        name: z.string().min(1)
      })
    });
    const invalidData = { userId: 123, name: '' }; // userId should be string, name too short

    // Act
    const result = UserCreated.schema.safeParse(invalidData);

    // Assert
    expect(result.success).toBe(false);
  });

  // Test 4: Create function adds type discriminator
  it('create function adds type discriminator', () => {
    // Arrange
    const UserCreated = defineEvent({
      type: 'UserCreated',
      schema: z.object({ userId: z.string() })
    });
    const data = { userId: '123' };

    // Act
    const event = UserCreated.create(data);

    // Assert
    expect(event.type).toBe('UserCreated');
  });

  // Test 5: Create function includes all data fields
  it('create function includes all data fields', () => {
    // Arrange
    const UserCreated = defineEvent({
      type: 'UserCreated',
      schema: z.object({ 
        userId: z.string(),
        name: z.string(),
        email: z.string().email()
      })
    });
    const data = { 
      userId: '123',
      name: 'John Doe',
      email: 'john@example.com'
    };

    // Act
    const event = UserCreated.create(data);

    // Assert
    expect(event.userId).toBe('123');
    expect(event.name).toBe('John Doe');
    expect(event.email).toBe('john@example.com');
  });

  // Test 6: Parse function validates and adds type
  it('parse function validates and adds type', () => {
    // Arrange
    const UserCreated = defineEvent({
      type: 'UserCreated',
      schema: z.object({ userId: z.string() })
    });
    const rawData = { userId: '123' };

    // Act
    const event = UserCreated.parse(rawData);

    // Assert
    expect(event.type).toBe('UserCreated');
    expect(event.userId).toBe('123');
  });

  // Test 7: Parse function throws on invalid data
  it('parse function throws on invalid data', () => {
    // Arrange
    const UserCreated = defineEvent({
      type: 'UserCreated',
      schema: z.object({ userId: z.string() })
    });
    const invalidData = { userId: 123 }; // Should be string

    // Act & Assert
    expect(() => UserCreated.parse(invalidData)).toThrow();
  });

  // Test 8: SafeParse returns success for valid data
  it('safeParse returns success for valid data', () => {
    // Arrange
    const UserCreated = defineEvent({
      type: 'UserCreated',
      schema: z.object({ userId: z.string() })
    });
    const validData = { userId: '123' };

    // Act
    const result = UserCreated.safeParse(validData);

    // Assert
    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data.type).toBe('UserCreated');
      expect(result.data.userId).toBe('123');
    }
  });

  // Test 9: SafeParse returns error for invalid data
  it('safeParse returns error for invalid data', () => {
    // Arrange
    const UserCreated = defineEvent({
      type: 'UserCreated',
      schema: z.object({ userId: z.string() })
    });
    const invalidData = { userId: 123 };

    // Act
    const result = UserCreated.safeParse(invalidData);

    // Assert
    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error).toBeDefined();
    }
  });

  // Test 10: Complex schema with nested objects
  it('handles complex schema with nested objects', () => {
    // Arrange
    const OrderPlaced = defineEvent({
      type: 'OrderPlaced',
      schema: z.object({
        orderId: z.string().uuid(),
        customer: z.object({
          id: z.string(),
          name: z.string(),
          email: z.string().email()
        }),
        items: z.array(z.object({
          productId: z.string(),
          quantity: z.number().positive(),
          price: z.number().positive()
        })),
        placedAt: z.string().datetime()
      })
    });

    const data = {
      orderId: '550e8400-e29b-41d4-a716-446655440000',
      customer: {
        id: 'cust123',
        name: 'John Doe',
        email: 'john@example.com'
      },
      items: [
        { productId: 'prod1', quantity: 2, price: 29.99 },
        { productId: 'prod2', quantity: 1, price: 49.99 }
      ],
      placedAt: '2024-01-15T10:30:00Z'
    };

    // Act
    const event = OrderPlaced.create(data);

    // Assert
    expect(event.type).toBe('OrderPlaced');
    expect(event.customer.name).toBe('John Doe');
    expect(event.items).toHaveLength(2);
  });

  // Test 11: Type inference works correctly
  it('provides correct TypeScript types through inference', () => {
    // Arrange
    const UserCreated = defineEvent({
      type: 'UserCreated',
      schema: z.object({ 
        userId: z.string(),
        name: z.string(),
        createdAt: z.string().datetime()
      })
    });

    // Act
    const event = UserCreated.create({
      userId: '123',
      name: 'John',
      createdAt: new Date().toISOString()
    });

    // Assert - TypeScript compile-time check
    // This test verifies that TypeScript correctly infers types
    const userId: string = event.userId;
    const name: string = event.name;
    const type: 'UserCreated' = event.type;
    
    expect(userId).toBe('123');
    expect(name).toBe('John');
    expect(type).toBe('UserCreated');
  });

  // Test 12: Optional fields work correctly
  it('handles optional fields correctly', () => {
    // Arrange
    const UserUpdated = defineEvent({
      type: 'UserUpdated',
      schema: z.object({
        userId: z.string(),
        name: z.string().optional(),
        email: z.string().email().optional()
      })
    });

    // Act
    const eventWithAllFields = UserUpdated.create({
      userId: '123',
      name: 'John',
      email: 'john@example.com'
    });

    const eventWithRequiredOnly = UserUpdated.create({
      userId: '123'
    });

    // Assert
    expect(eventWithAllFields.name).toBe('John');
    expect(eventWithRequiredOnly.name).toBeUndefined();
    expect(eventWithRequiredOnly.email).toBeUndefined();
  });
});