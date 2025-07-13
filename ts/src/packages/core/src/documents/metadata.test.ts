import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { Metadata, MetadataBuilder } from './metadata.js'

describe('Metadata', () => {
  describe('MetadataBuilder', () => {
    it('should create metadata with current timestamp by default', () => {
      // Arrange
      const beforeTime = new Date()
      
      // Act
      const metadata = new MetadataBuilder().build()
      
      // Assert
      const afterTime = new Date()
      expect(metadata.timestamp).toBeInstanceOf(Date)
      expect(metadata.timestamp.getTime()).toBeGreaterThanOrEqual(beforeTime.getTime())
      expect(metadata.timestamp.getTime()).toBeLessThanOrEqual(afterTime.getTime())
    })
    
    it('should build metadata with userId', () => {
      // Arrange & Act
      const metadata = new MetadataBuilder()
        .withUserId('user-123')
        .build()
      
      // Assert
      expect(metadata.userId).toBe('user-123')
    })
    
    it('should build metadata with correlationId', () => {
      // Arrange & Act
      const metadata = new MetadataBuilder()
        .withCorrelationId('correlation-456')
        .build()
      
      // Assert
      expect(metadata.correlationId).toBe('correlation-456')
    })
    
    it('should build metadata with causationId', () => {
      // Arrange & Act
      const metadata = new MetadataBuilder()
        .withCausationId('causation-789')
        .build()
      
      // Assert
      expect(metadata.causationId).toBe('causation-789')
    })
    
    it('should build metadata with custom timestamp', () => {
      // Arrange
      const customTime = new Date('2024-01-01T00:00:00Z')
      
      // Act
      const metadata = new MetadataBuilder()
        .withTimestamp(customTime)
        .build()
      
      // Assert
      expect(metadata.timestamp).toEqual(customTime)
    })
    
    it('should build metadata with single custom field', () => {
      // Arrange & Act
      const metadata = new MetadataBuilder()
        .withCustom('key1', 'value1')
        .build()
      
      // Assert
      expect(metadata.custom).toEqual({ key1: 'value1' })
    })
    
    it('should build metadata with multiple custom fields', () => {
      // Arrange & Act
      const metadata = new MetadataBuilder()
        .withCustom('key1', 'value1')
        .withCustom('key2', 42)
        .withCustom('key3', true)
        .build()
      
      // Assert
      expect(metadata.custom).toEqual({
        key1: 'value1',
        key2: 42,
        key3: true
      })
    })
    
    it('should build metadata with custom data object', () => {
      // Arrange
      const customData = {
        tenant: 'tenant-1',
        environment: 'production',
        version: '1.2.3'
      }
      
      // Act
      const metadata = new MetadataBuilder()
        .withCustomData(customData)
        .build()
      
      // Assert
      expect(metadata.custom).toEqual(customData)
    })
    
    it('should support method chaining', () => {
      // Arrange
      const timestamp = new Date()
      
      // Act
      const metadata = new MetadataBuilder()
        .withUserId('user-123')
        .withCorrelationId('corr-456')
        .withCausationId('cause-789')
        .withTimestamp(timestamp)
        .withCustom('key', 'value')
        .build()
      
      // Assert
      expect(metadata.userId).toBe('user-123')
      expect(metadata.correlationId).toBe('corr-456')
      expect(metadata.causationId).toBe('cause-789')
      expect(metadata.timestamp).toBe(timestamp)
      expect(metadata.custom).toEqual({ key: 'value' })
    })
    
    it('should create independent copies when building', () => {
      // Arrange
      const builder = new MetadataBuilder()
        .withUserId('user-123')
        .withCustom('key', 'value')
      
      // Act
      const metadata1 = builder.build()
      const metadata2 = builder.withUserId('user-456').build()
      
      // Assert
      expect(metadata1.userId).toBe('user-123')
      expect(metadata2.userId).toBe('user-456')
      expect(metadata1.custom).toEqual({ key: 'value' })
      expect(metadata2.custom).toEqual({ key: 'value' })
    })
  })
  
  describe('Metadata utility functions', () => {
    describe('create', () => {
      it('should create empty metadata with current timestamp', () => {
        // Arrange
        const beforeTime = new Date()
        
        // Act
        const metadata = Metadata.create()
        
        // Assert
        const afterTime = new Date()
        expect(metadata.timestamp).toBeInstanceOf(Date)
        expect(metadata.timestamp.getTime()).toBeGreaterThanOrEqual(beforeTime.getTime())
        expect(metadata.timestamp.getTime()).toBeLessThanOrEqual(afterTime.getTime())
        expect(metadata.userId).toBeUndefined()
        expect(metadata.correlationId).toBeUndefined()
        expect(metadata.causationId).toBeUndefined()
        expect(metadata.custom).toBeUndefined()
      })
    })
    
    describe('withUser', () => {
      it('should create metadata with userId', () => {
        // Act
        const metadata = Metadata.withUser('user-999')
        
        // Assert
        expect(metadata.userId).toBe('user-999')
        expect(metadata.timestamp).toBeInstanceOf(Date)
      })
    })
    
    describe('correlated', () => {
      it('should create metadata with correlationId only', () => {
        // Act
        const metadata = Metadata.correlated('corr-123')
        
        // Assert
        expect(metadata.correlationId).toBe('corr-123')
        expect(metadata.causationId).toBeUndefined()
        expect(metadata.timestamp).toBeInstanceOf(Date)
      })
      
      it('should create metadata with both correlationId and causationId', () => {
        // Act
        const metadata = Metadata.correlated('corr-123', 'cause-456')
        
        // Assert
        expect(metadata.correlationId).toBe('corr-123')
        expect(metadata.causationId).toBe('cause-456')
      })
    })
    
    describe('merge', () => {
      it('should merge two metadata objects', () => {
        // Arrange
        const base: Metadata = {
          userId: 'user-123',
          correlationId: 'corr-123',
          timestamp: new Date('2024-01-01T00:00:00Z')
        }
        
        const override: Partial<Metadata> = {
          userId: 'user-456',
          causationId: 'cause-789'
        }
        
        // Act
        const merged = Metadata.merge(base, override)
        
        // Assert
        expect(merged.userId).toBe('user-456') // overridden
        expect(merged.correlationId).toBe('corr-123') // preserved
        expect(merged.causationId).toBe('cause-789') // added
        expect(merged.timestamp).toEqual(base.timestamp) // preserved
      })
      
      it('should override timestamp when provided', () => {
        // Arrange
        const base: Metadata = {
          timestamp: new Date('2024-01-01T00:00:00Z')
        }
        
        const override: Partial<Metadata> = {
          timestamp: new Date('2024-12-31T23:59:59Z')
        }
        
        // Act
        const merged = Metadata.merge(base, override)
        
        // Assert
        expect(merged.timestamp).toEqual(override.timestamp)
      })
      
      it('should merge custom metadata', () => {
        // Arrange
        const base: Metadata = {
          timestamp: new Date(),
          custom: {
            key1: 'value1',
            key2: 'value2'
          }
        }
        
        const override: Partial<Metadata> = {
          custom: {
            key2: 'overridden',
            key3: 'new'
          }
        }
        
        // Act
        const merged = Metadata.merge(base, override)
        
        // Assert
        expect(merged.custom).toEqual({
          key1: 'value1',
          key2: 'overridden',
          key3: 'new'
        })
      })
      
      it('should handle missing custom data in base', () => {
        // Arrange
        const base: Metadata = {
          timestamp: new Date()
        }
        
        const override: Partial<Metadata> = {
          custom: { key: 'value' }
        }
        
        // Act
        const merged = Metadata.merge(base, override)
        
        // Assert
        expect(merged.custom).toEqual({ key: 'value' })
      })
      
      it('should handle missing custom data in override', () => {
        // Arrange
        const base: Metadata = {
          timestamp: new Date(),
          custom: { key: 'value' }
        }
        
        const override: Partial<Metadata> = {
          userId: 'user-123'
        }
        
        // Act
        const merged = Metadata.merge(base, override)
        
        // Assert
        expect(merged.custom).toEqual({ key: 'value' })
      })
    })
    
    describe('builder', () => {
      it('should create empty builder when no base provided', () => {
        // Act
        const metadata = Metadata.builder().build()
        
        // Assert
        expect(metadata.timestamp).toBeInstanceOf(Date)
        expect(metadata.userId).toBeUndefined()
      })
      
      it('should create builder from existing metadata', () => {
        // Arrange
        const base: Metadata = {
          userId: 'user-123',
          correlationId: 'corr-456',
          causationId: 'cause-789',
          timestamp: new Date('2024-01-01T00:00:00Z'),
          custom: {
            key1: 'value1',
            key2: 42
          }
        }
        
        // Act
        const metadata = Metadata.builder(base).build()
        
        // Assert
        expect(metadata).toEqual(base)
      })
      
      it('should allow modification of copied metadata', () => {
        // Arrange
        const base: Metadata = {
          userId: 'user-123',
          timestamp: new Date('2024-01-01T00:00:00Z')
        }
        
        // Act
        const metadata = Metadata.builder(base)
          .withUserId('user-456')
          .withCorrelationId('new-corr')
          .build()
        
        // Assert
        expect(metadata.userId).toBe('user-456')
        expect(metadata.correlationId).toBe('new-corr')
        expect(metadata.timestamp).toEqual(base.timestamp)
      })
    })
  })
  
  describe('Integration scenarios', () => {
    it('should support event metadata flow', () => {
      // Simulate a command creating an event with metadata
      const commandMetadata = new MetadataBuilder()
        .withUserId('user-123')
        .withCorrelationId('request-456')
        .withCustom('source', 'web-api')
        .build()
      
      // Event inherits from command but adds causation
      const eventMetadata = Metadata.builder(commandMetadata)
        .withCausationId('command-789')
        .withTimestamp(new Date())
        .build()
      
      expect(eventMetadata.userId).toBe('user-123')
      expect(eventMetadata.correlationId).toBe('request-456')
      expect(eventMetadata.causationId).toBe('command-789')
      expect(eventMetadata.custom).toEqual({ source: 'web-api' })
    })
    
    it('should support multi-tenant metadata', () => {
      // Create metadata for tenant-specific operation
      const metadata = new MetadataBuilder()
        .withUserId('user@tenant1.com')
        .withCustom('tenantId', 'tenant-1')
        .withCustom('permissions', ['read', 'write'])
        .build()
      
      expect(metadata.custom?.tenantId).toBe('tenant-1')
      expect(metadata.custom?.permissions).toEqual(['read', 'write'])
    })
  })
})