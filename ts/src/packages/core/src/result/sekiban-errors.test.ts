import { describe, it, expect } from 'vitest'
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

describe('Sekiban Errors', () => {
  describe('SekibanError', () => {
    it('should be abstract base class', () => {
      // Cannot instantiate abstract class directly
      // This test documents the expected behavior
      expect(SekibanError).toBeDefined()
    })
  })
  
  describe('AggregateNotFoundError', () => {
    it('should create error with aggregate details', () => {
      // Arrange & Act
      const error = new AggregateNotFoundError('user-123', 'User')
      
      // Assert
      expect(error.code).toBe('AGGREGATE_NOT_FOUND')
      expect(error.aggregateId).toBe('user-123')
      expect(error.aggregateType).toBe('User')
      expect(error.message).toBe('Aggregate User with id user-123 not found')
      expect(error.name).toBe('AggregateNotFoundError')
    })
    
    it('should be instanceof SekibanError', () => {
      const error = new AggregateNotFoundError('order-456', 'Order')
      expect(error).toBeInstanceOf(SekibanError)
      expect(error).toBeInstanceOf(Error)
    })
  })
  
  describe('CommandValidationError', () => {
    it('should create error with validation details', () => {
      // Arrange
      const validationErrors = ['Name is required', 'Email is invalid']
      
      // Act
      const error = new CommandValidationError('CreateUser', validationErrors)
      
      // Assert
      expect(error.code).toBe('COMMAND_VALIDATION_ERROR')
      expect(error.commandType).toBe('CreateUser')
      expect(error.validationErrors).toEqual(validationErrors)
      expect(error.message).toBe('Command validation failed for CreateUser: Name is required, Email is invalid')
    })
    
    it('should handle single validation error', () => {
      const error = new CommandValidationError('UpdateProfile', ['Invalid age'])
      expect(error.message).toBe('Command validation failed for UpdateProfile: Invalid age')
    })
    
    it('should handle empty validation errors', () => {
      const error = new CommandValidationError('DeleteUser', [])
      expect(error.message).toBe('Command validation failed for DeleteUser: ')
    })
  })
  
  describe('EventApplicationError', () => {
    it('should create error with event details', () => {
      // Arrange & Act
      const error = new EventApplicationError('UserCreated', 'User already exists')
      
      // Assert
      expect(error.code).toBe('EVENT_APPLICATION_ERROR')
      expect(error.eventType).toBe('UserCreated')
      expect(error.reason).toBe('User already exists')
      expect(error.message).toBe('Failed to apply event UserCreated: User already exists')
    })
  })
  
  describe('QueryExecutionError', () => {
    it('should create error with query details', () => {
      // Arrange & Act
      const error = new QueryExecutionError('GetUserById', 'Database connection failed')
      
      // Assert
      expect(error.code).toBe('QUERY_EXECUTION_ERROR')
      expect(error.queryType).toBe('GetUserById')
      expect(error.reason).toBe('Database connection failed')
      expect(error.message).toBe('Query execution failed for GetUserById: Database connection failed')
    })
  })
  
  describe('SerializationError', () => {
    it('should create error for serialization failure', () => {
      // Arrange & Act
      const error = new SerializationError('serialize', 'Circular reference detected')
      
      // Assert
      expect(error.code).toBe('SERIALIZATION_ERROR')
      expect(error.operation).toBe('serialize')
      expect(error.reason).toBe('Circular reference detected')
      expect(error.message).toBe('Serialization error during serialize: Circular reference detected')
    })
    
    it('should create error for deserialization failure', () => {
      const error = new SerializationError('deserialize', 'Invalid JSON format')
      expect(error.operation).toBe('deserialize')
      expect(error.message).toBe('Serialization error during deserialize: Invalid JSON format')
    })
  })
  
  describe('EventStoreError', () => {
    it('should create error with operation details', () => {
      // Arrange & Act
      const error = new EventStoreError('append', 'Event already exists')
      
      // Assert
      expect(error.code).toBe('EVENT_STORE_ERROR')
      expect(error.operation).toBe('append')
      expect(error.reason).toBe('Event already exists')
      expect(error.message).toBe('Event store error during append: Event already exists')
    })
    
    it('should handle different operations', () => {
      const readError = new EventStoreError('read', 'Connection timeout')
      expect(readError.operation).toBe('read')
      
      const writeError = new EventStoreError('write', 'Disk full')
      expect(writeError.operation).toBe('write')
    })
  })
  
  describe('ConcurrencyError', () => {
    it('should create error with version details', () => {
      // Arrange & Act
      const error = new ConcurrencyError(5, 3)
      
      // Assert
      expect(error.code).toBe('CONCURRENCY_ERROR')
      expect(error.expectedVersion).toBe(5)
      expect(error.actualVersion).toBe(3)
      expect(error.message).toBe('Concurrency conflict: expected version 5, but was 3')
    })
    
    it('should handle version 0', () => {
      const error = new ConcurrencyError(1, 0)
      expect(error.message).toBe('Concurrency conflict: expected version 1, but was 0')
    })
  })
  
  describe('UnsupportedOperationError', () => {
    it('should create error with operation name', () => {
      // Arrange & Act
      const error = new UnsupportedOperationError('bulk delete')
      
      // Assert
      expect(error.code).toBe('UNSUPPORTED_OPERATION')
      expect(error.operation).toBe('bulk delete')
      expect(error.message).toBe('Unsupported operation: bulk delete')
    })
  })
  
  describe('ValidationError', () => {
    it('should create simple validation error', () => {
      // Arrange & Act
      const error = new ValidationError('Email format is invalid')
      
      // Assert
      expect(error.code).toBe('VALIDATION_ERROR')
      expect(error.message).toBe('Email format is invalid')
      expect(error.name).toBe('ValidationError')
    })
  })
  
  describe('Error inheritance', () => {
    it('should maintain prototype chain', () => {
      const error = new CommandValidationError('TestCommand', ['error'])
      
      expect(error instanceof CommandValidationError).toBe(true)
      expect(error instanceof SekibanError).toBe(true)
      expect(error instanceof Error).toBe(true)
    })
    
    it('should be catchable as Error', () => {
      const throwError = () => {
        throw new EventStoreError('write', 'test error')
      }
      
      expect(throwError).toThrow(Error)
      expect(throwError).toThrow(SekibanError)
      expect(throwError).toThrow(EventStoreError)
    })
  })
  
  describe('Error serialization', () => {
    it('should serialize to JSON with all properties', () => {
      const error = new ConcurrencyError(10, 8)
      const json = JSON.stringify(error)
      const parsed = JSON.parse(json)
      
      expect(parsed.code).toBe('CONCURRENCY_ERROR')
      expect(parsed.expectedVersion).toBe(10)
      expect(parsed.actualVersion).toBe(8)
      expect(parsed.message).toBe('Concurrency conflict: expected version 10, but was 8')
      expect(parsed.name).toBe('ConcurrencyError')
    })
    
    it('should serialize complex errors', () => {
      const error = new CommandValidationError('ComplexCommand', [
        'Field A is required',
        'Field B must be positive',
        'Field C is too long'
      ])
      
      const json = JSON.stringify(error)
      const parsed = JSON.parse(json)
      
      expect(parsed.commandType).toBe('ComplexCommand')
      expect(parsed.validationErrors).toHaveLength(3)
      expect(parsed.validationErrors[1]).toBe('Field B must be positive')
    })
  })
})