import { describe, it, expect } from 'vitest'
import { PartitionKeys } from './partition-keys'

describe('PartitionKeys', () => {
  describe('create', () => {
    it('should create partition keys with aggregate id only', () => {
      // Arrange
      const aggregateId = 'user-123'
      
      // Act
      const keys = PartitionKeys.create(aggregateId)
      
      // Assert
      expect(keys.aggregateId).toBe(aggregateId)
      expect(keys.partitionKey).toBe(aggregateId)
      expect(keys.group).toBeUndefined()
      expect(keys.rootPartitionKey).toBeUndefined()
    })
    
    it('should create partition keys with group', () => {
      // Arrange
      const aggregateId = 'user-123'
      const group = 'users'
      
      // Act
      const keys = PartitionKeys.create(aggregateId, group)
      
      // Assert
      expect(keys.aggregateId).toBe(aggregateId)
      expect(keys.group).toBe(group)
      expect(keys.partitionKey).toBe('users-user-123')
      expect(keys.rootPartitionKey).toBeUndefined()
    })
    
    it('should create partition keys with root partition', () => {
      // Arrange
      const aggregateId = 'user-123'
      const group = 'users'
      const rootPartitionKey = 'tenant-1'
      
      // Act
      const keys = PartitionKeys.create(aggregateId, group, rootPartitionKey)
      
      // Assert
      expect(keys.aggregateId).toBe(aggregateId)
      expect(keys.group).toBe(group)
      expect(keys.rootPartitionKey).toBe(rootPartitionKey)
      expect(keys.partitionKey).toBe('tenant-1-users-user-123')
    })
  })
  
  describe('generate', () => {
    it('should generate new partition keys with unique id', () => {
      const keys1 = PartitionKeys.generate()
      const keys2 = PartitionKeys.generate()
      
      expect(keys1.aggregateId).not.toBe(keys2.aggregateId)
      expect(keys1.aggregateId).toMatch(/^[0-9a-f-]+$/)
    })
    
    it('should generate with group', () => {
      const keys = PartitionKeys.generate('accounts')
      
      expect(keys.group).toBe('accounts')
      expect(keys.partitionKey).toContain('accounts-')
    })
    
    it('should generate with root partition', () => {
      const keys = PartitionKeys.generate('accounts', 'tenant-2')
      
      expect(keys.group).toBe('accounts')
      expect(keys.rootPartitionKey).toBe('tenant-2')
      expect(keys.partitionKey).toMatch(/^tenant-2-accounts-[0-9a-f-]+$/)
    })
  })
  
  describe('existing', () => {
    it('should create partition keys for existing aggregate', () => {
      const aggregateId = 'existing-123'
      const keys = PartitionKeys.existing(aggregateId)
      
      expect(keys.aggregateId).toBe(aggregateId)
      expect(keys.partitionKey).toBe(aggregateId)
    })
    
    it('should create with group for existing aggregate', () => {
      const keys = PartitionKeys.existing('existing-123', 'orders')
      
      expect(keys.aggregateId).toBe('existing-123')
      expect(keys.group).toBe('orders')
      expect(keys.partitionKey).toBe('orders-existing-123')
    })
  })
  
  describe('toString', () => {
    it('should return partition key as string', () => {
      const keys = PartitionKeys.create('test-123', 'items')
      
      expect(keys.toString()).toBe('items-test-123')
    })
  })
  
  describe('equals', () => {
    it('should return true for equal partition keys', () => {
      const keys1 = PartitionKeys.create('id-123', 'group1', 'root1')
      const keys2 = PartitionKeys.create('id-123', 'group1', 'root1')
      
      expect(keys1.equals(keys2)).toBe(true)
    })
    
    it('should return false for different aggregate ids', () => {
      const keys1 = PartitionKeys.create('id-123')
      const keys2 = PartitionKeys.create('id-456')
      
      expect(keys1.equals(keys2)).toBe(false)
    })
    
    it('should return false for different groups', () => {
      const keys1 = PartitionKeys.create('id-123', 'group1')
      const keys2 = PartitionKeys.create('id-123', 'group2')
      
      expect(keys1.equals(keys2)).toBe(false)
    })
    
    it('should return false for different root partitions', () => {
      const keys1 = PartitionKeys.create('id-123', 'group', 'root1')
      const keys2 = PartitionKeys.create('id-123', 'group', 'root2')
      
      expect(keys1.equals(keys2)).toBe(false)
    })
  })
})
