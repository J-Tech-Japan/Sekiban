import { describe, it, expect, vi } from 'vitest'
import { SortableUniqueId } from './sortable-unique-id'

describe('SortableUniqueId', () => {
  describe('generate', () => {
    it('should create a unique id', () => {
      const id1 = SortableUniqueId.generate()
      const id2 = SortableUniqueId.generate()
      
      expect(id1.value).not.toBe(id2.value)
    })
    
    it('should create sortable ids in chronological order', async () => {
      const id1 = SortableUniqueId.generate()
      
      // Wait a bit to ensure timestamp difference
      await new Promise(resolve => setTimeout(resolve, 10))
      
      const id2 = SortableUniqueId.generate()
      
      expect(id1.value < id2.value).toBe(true)
    })
    
    it('should handle rapid generation with counter', () => {
      const ids = Array.from({ length: 100 }, () => SortableUniqueId.generate())
      const uniqueIds = new Set(ids.map(id => id.value))
      
      expect(uniqueIds.size).toBe(100)
    })
  })
  
  describe('toString', () => {
    it('should convert to string', () => {
      const id = SortableUniqueId.generate()
      
      expect(id.toString()).toBe(id.value)
      expect(typeof id.toString()).toBe('string')
    })
  })
  
  describe('fromString', () => {
    it('should create from valid string', () => {
      const original = SortableUniqueId.generate()
      const result = SortableUniqueId.fromString(original.value)
      
      expect(result.isOk()).toBe(true)
      expect(result._unsafeUnwrap().value).toBe(original.value)
    })
    
    it('should fail for empty string', () => {
      const result = SortableUniqueId.fromString('')
      
      expect(result.isErr()).toBe(true)
      expect(result._unsafeUnwrapErr().code).toBe('VALIDATION_ERROR')
    })
    
    it('should fail for invalid format', () => {
      const result = SortableUniqueId.fromString('invalid-id')
      
      expect(result.isErr()).toBe(true)
      expect(result._unsafeUnwrapErr().code).toBe('VALIDATION_ERROR')
    })
  })
  
  describe('compare', () => {
    it('should compare ids correctly', () => {
      const id1 = SortableUniqueId.generate()
      const id2 = SortableUniqueId.generate()
      
      expect(SortableUniqueId.compare(id1, id2)).toBeLessThan(0)
      expect(SortableUniqueId.compare(id2, id1)).toBeGreaterThan(0)
      expect(SortableUniqueId.compare(id1, id1)).toBe(0)
    })
  })
})
