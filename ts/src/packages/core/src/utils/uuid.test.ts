import { describe, it, expect } from 'vitest'
import { generateUuid, isValidUuid, createNamespacedUuid } from './uuid'

describe('UUID utilities', () => {
  describe('generateUuid', () => {
    it('should generate valid UUID v4', () => {
      // Act
      const uuid = generateUuid()
      
      // Assert
      expect(typeof uuid).toBe('string')
      expect(uuid).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i)
    })
    
    it('should generate unique UUIDs', () => {
      // Act
      const uuid1 = generateUuid()
      const uuid2 = generateUuid()
      const uuid3 = generateUuid()
      
      // Assert
      expect(uuid1).not.toBe(uuid2)
      expect(uuid1).not.toBe(uuid3)
      expect(uuid2).not.toBe(uuid3)
    })
    
    it('should generate many unique UUIDs', () => {
      // Arrange
      const count = 1000
      const uuids = new Set<string>()
      
      // Act
      for (let i = 0; i < count; i++) {
        uuids.add(generateUuid())
      }
      
      // Assert
      expect(uuids.size).toBe(count)
    })
  })
  
  describe('isValidUuid', () => {
    it('should validate correct UUID v4', () => {
      // Arrange
      const validUuids = [
        '123e4567-e89b-12d3-a456-426614174000',
        'f47ac10b-58cc-4372-a567-0e02b2c3d479',
        '6ba7b810-9dad-11d1-80b4-00c04fd430c8',
        '6ba7b811-9dad-11d1-80b4-00c04fd430c8'
      ]
      
      // Act & Assert
      validUuids.forEach(uuid => {
        expect(isValidUuid(uuid)).toBe(true)
      })
    })
    
    it('should reject invalid UUID formats', () => {
      // Arrange
      const invalidUuids = [
        '',
        'not-a-uuid',
        '123e4567-e89b-12d3-a456',
        '123e4567-e89b-12d3-a456-426614174000-extra',
        '123e4567e89b12d3a456426614174000', // no dashes
        '123e4567-e89b-12d3-a456-42661417400g', // invalid character
        'zzzzzzzz-zzzz-zzzz-zzzz-zzzzzzzzzzzz' // invalid hex
      ]
      
      // Act & Assert
      invalidUuids.forEach(uuid => {
        expect(isValidUuid(uuid)).toBe(false)
      })
    })
    
    it('should handle edge cases', () => {
      // Act & Assert
      expect(isValidUuid(null as any)).toBe(false)
      expect(isValidUuid(undefined as any)).toBe(false)
      expect(isValidUuid(123 as any)).toBe(false)
      expect(isValidUuid({} as any)).toBe(false)
    })
  })
  
  describe('createNamespacedUuid', () => {
    it('should create namespaced UUID from string', () => {
      // Arrange
      const namespace = 'test-namespace'
      const value = 'test-value'
      
      // Act
      const uuid = createNamespacedUuid(namespace, value)
      
      // Assert
      expect(isValidUuid(uuid)).toBe(true)
    })
    
    it('should create deterministic UUIDs', () => {
      // Arrange
      const namespace = 'test-namespace'
      const value = 'test-value'
      
      // Act
      const uuid1 = createNamespacedUuid(namespace, value)
      const uuid2 = createNamespacedUuid(namespace, value)
      
      // Assert
      expect(uuid1).toBe(uuid2)
    })
    
    it('should create different UUIDs for different values', () => {
      // Arrange
      const namespace = 'test-namespace'
      const value1 = 'test-value-1'
      const value2 = 'test-value-2'
      
      // Act
      const uuid1 = createNamespacedUuid(namespace, value1)
      const uuid2 = createNamespacedUuid(namespace, value2)
      
      // Assert
      expect(uuid1).not.toBe(uuid2)
    })
    
    it('should create different UUIDs for different namespaces', () => {
      // Arrange
      const namespace1 = 'namespace-1'
      const namespace2 = 'namespace-2'
      const value = 'same-value'
      
      // Act
      const uuid1 = createNamespacedUuid(namespace1, value)
      const uuid2 = createNamespacedUuid(namespace2, value)
      
      // Assert
      expect(uuid1).not.toBe(uuid2)
    })
    
    it('should handle special characters and unicode', () => {
      // Arrange
      const namespace = 'special-chars'
      const values = [
        'hello@world.com',
        'user/123/profile',
        'データベース',
        '🦄 unicorn',
        'line1\nline2',
        'quote"test'
      ]
      
      // Act & Assert
      values.forEach(value => {
        const uuid = createNamespacedUuid(namespace, value)
        expect(isValidUuid(uuid)).toBe(true)
      })
    })
    
    it('should handle empty strings', () => {
      // Arrange
      const namespace = 'test'
      const value = ''
      
      // Act
      const uuid = createNamespacedUuid(namespace, value)
      
      // Assert
      expect(isValidUuid(uuid)).toBe(true)
    })
  })
  
  describe('integration with domain objects', () => {
    it('should work with aggregate IDs', () => {
      // Arrange
      const aggregateType = 'User'
      const userEmail = 'user@example.com'
      
      // Act
      const aggregateId = createNamespacedUuid(aggregateType, userEmail)
      
      // Assert
      expect(isValidUuid(aggregateId)).toBe(true)
      
      // Same email should produce same ID
      const sameId = createNamespacedUuid(aggregateType, userEmail)
      expect(aggregateId).toBe(sameId)
    })
    
    it('should work with event IDs', () => {
      // Arrange
      const eventType = 'UserCreated'
      const timestamp = '2024-01-01T00:00:00Z'
      const userId = 'user-123'
      const eventKey = `${eventType}-${timestamp}-${userId}`
      
      // Act
      const eventId = createNamespacedUuid('Events', eventKey)
      
      // Assert
      expect(isValidUuid(eventId)).toBe(true)
    })
    
    it('should generate unique but deterministic partition keys', () => {
      // Arrange
      const tenantId = 'tenant-1'
      const aggregateType = 'Order'
      const businessKey = 'ORD-2024-001'
      
      // Act
      const partitionKey1 = createNamespacedUuid(`${tenantId}-${aggregateType}`, businessKey)
      const partitionKey2 = createNamespacedUuid(`${tenantId}-${aggregateType}`, businessKey)
      const differentKey = createNamespacedUuid(`tenant-2-${aggregateType}`, businessKey)
      
      // Assert
      expect(partitionKey1).toBe(partitionKey2)
      expect(partitionKey1).not.toBe(differentKey)
      expect(isValidUuid(partitionKey1)).toBe(true)
      expect(isValidUuid(differentKey)).toBe(true)
    })
  })
})
