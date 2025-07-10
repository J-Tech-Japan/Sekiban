import { describe, it, expect } from 'vitest'
import {
  isSekibanError,
  isAggregateNotFoundError,
  isCommandValidationError,
  isEventApplicationError,
  isQueryExecutionError,
  isSerializationError,
  isEventStoreError,
  isConcurrencyError,
  isUnsupportedOperationError,
  isValidationError
} from './error-guards'
import {
  SekibanError,
  AggregateNotFoundError,
  CommandValidationError,
  EventApplicationError,
  QueryExecutionError,
  SerializationError,
  EventStoreError,
  ConcurrencyError,
  UnsupportedOperationError,
  ValidationError
} from './errors'

describe('Error Guards', () => {
  describe('isSekibanError', () => {
    it('should identify SekibanError and subclasses', () => {
      // Arrange
      const sekibanErrors = [
        new AggregateNotFoundError('123', 'User'),
        new CommandValidationError('Create', ['error']),
        new EventApplicationError('Event', 'reason'),
        new QueryExecutionError('Query', 'reason'),
        new SerializationError('serialize', 'reason'),
        new EventStoreError('write', 'reason'),
        new ConcurrencyError(2, 1),
        new UnsupportedOperationError('operation'),
        new ValidationError('message')
      ]
      
      const nonSekibanErrors = [
        new Error('regular error'),
        new TypeError('type error'),
        new RangeError('range error'),
        'string error',
        null,
        undefined,
        { message: 'fake error' }
      ]
      
      // Act & Assert
      sekibanErrors.forEach(error => {
        expect(isSekibanError(error)).toBe(true)
      })
      
      nonSekibanErrors.forEach(error => {
        expect(isSekibanError(error)).toBe(false)
      })
    })
  })
  
  describe('Specific error guards', () => {
    it('should identify AggregateNotFoundError', () => {
      const aggregateError = new AggregateNotFoundError('123', 'User')
      const otherError = new ValidationError('other')
      
      expect(isAggregateNotFoundError(aggregateError)).toBe(true)
      expect(isAggregateNotFoundError(otherError)).toBe(false)
      expect(isAggregateNotFoundError(new Error())).toBe(false)
    })
    
    it('should identify CommandValidationError', () => {
      const commandError = new CommandValidationError('CreateUser', ['Invalid'])
      const otherError = new ValidationError('other')
      
      expect(isCommandValidationError(commandError)).toBe(true)
      expect(isCommandValidationError(otherError)).toBe(false)
    })
    
    it('should identify EventApplicationError', () => {
      const eventError = new EventApplicationError('UserCreated', 'Failed')
      const otherError = new ValidationError('other')
      
      expect(isEventApplicationError(eventError)).toBe(true)
      expect(isEventApplicationError(otherError)).toBe(false)
    })
    
    it('should identify QueryExecutionError', () => {
      const queryError = new QueryExecutionError('GetUser', 'Failed')
      const otherError = new ValidationError('other')
      
      expect(isQueryExecutionError(queryError)).toBe(true)
      expect(isQueryExecutionError(otherError)).toBe(false)
    })
    
    it('should identify SerializationError', () => {
      const serializationError = new SerializationError('deserialize', 'Invalid JSON')
      const otherError = new ValidationError('other')
      
      expect(isSerializationError(serializationError)).toBe(true)
      expect(isSerializationError(otherError)).toBe(false)
    })
    
    it('should identify EventStoreError', () => {
      const storeError = new EventStoreError('append', 'Duplicate')
      const otherError = new ValidationError('other')
      
      expect(isEventStoreError(storeError)).toBe(true)
      expect(isEventStoreError(otherError)).toBe(false)
    })
    
    it('should identify ConcurrencyError', () => {
      const concurrencyError = new ConcurrencyError(5, 3)
      const otherError = new ValidationError('other')
      
      expect(isConcurrencyError(concurrencyError)).toBe(true)
      expect(isConcurrencyError(otherError)).toBe(false)
    })
    
    it('should identify UnsupportedOperationError', () => {
      const unsupportedError = new UnsupportedOperationError('bulk delete')
      const otherError = new ValidationError('other')
      
      expect(isUnsupportedOperationError(unsupportedError)).toBe(true)
      expect(isUnsupportedOperationError(otherError)).toBe(false)
    })
    
    it('should identify ValidationError', () => {
      const validationError = new ValidationError('Invalid email')
      const otherError = new EventStoreError('write', 'Failed')
      
      expect(isValidationError(validationError)).toBe(true)
      expect(isValidationError(otherError)).toBe(false)
    })
  })
  
  describe('Type narrowing in conditionals', () => {
    it('should narrow types correctly', () => {
      const error: unknown = new ConcurrencyError(10, 8)
      
      if (isConcurrencyError(error)) {
        // TypeScript should know error is ConcurrencyError here
        expect(error.expectedVersion).toBe(10)
        expect(error.actualVersion).toBe(8)
        expect(error.code).toBe('CONCURRENCY_ERROR')
      } else {
        expect.fail('Should have identified as ConcurrencyError')
      }
    })
    
    it('should work with try-catch blocks', () => {
      const throwError = () => {
        throw new EventStoreError('write', 'Disk full')
      }
      
      try {
        throwError()
      } catch (error) {
        if (isEventStoreError(error)) {
          expect(error.operation).toBe('write')
          expect(error.reason).toBe('Disk full')
        } else {
          expect.fail('Should have caught EventStoreError')
        }
      }
    })
  })
  
  describe('Guard composition', () => {
    it('should handle multiple checks', () => {
      const errors: unknown[] = [
        new ValidationError('Error 1'),
        new ConcurrencyError(2, 1),
        new Error('Regular error'),
        'String error',
        new EventStoreError('read', 'Failed')
      ]
      
      const sekibanErrors = errors.filter(isSekibanError)
      expect(sekibanErrors).toHaveLength(3)
      
      const validationErrors = errors.filter(isValidationError)
      expect(validationErrors).toHaveLength(1)
      
      const concurrencyErrors = errors.filter(isConcurrencyError)
      expect(concurrencyErrors).toHaveLength(1)
    })
  })
})