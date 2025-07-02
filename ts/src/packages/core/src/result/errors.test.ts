import { describe, it, expect } from 'vitest'
import {
  DomainError,
  ValidationError,
  NotFoundError,
  ConflictError,
  BusinessRuleError,
  UnauthorizedError,
  createDomainError,
  isDomainError,
  isValidationError,
  isNotFoundError,
  isConflictError,
  isBusinessRuleError,
  isUnauthorizedError
} from './errors'

describe('Domain Errors', () => {
  describe('ValidationError', () => {
    it('should create validation error with field', () => {
      const error = new ValidationError('Email is invalid', 'email')
      
      expect(error.code).toBe('VALIDATION_ERROR')
      expect(error.field).toBe('email')
      expect(error.message).toBe('Email is invalid')
      expect(error.timestamp).toBeInstanceOf(Date)
    })
    
    it('should be identifiable by type guard', () => {
      const error = new ValidationError('Invalid input', 'input')
      
      expect(isValidationError(error)).toBe(true)
      expect(isDomainError(error)).toBe(true)
      expect(isNotFoundError(error)).toBe(false)
    })
  })
  
  describe('NotFoundError', () => {
    it('should create not found error', () => {
      const error = new NotFoundError('User', 'user-123')
      
      expect(error.code).toBe('NOT_FOUND')
      expect(error.resourceType).toBe('User')
      expect(error.resourceId).toBe('user-123')
      expect(error.message).toBe('User with id user-123 not found')
    })
    
    it('should be identifiable by type guard', () => {
      const error = new NotFoundError('Order', 'order-456')
      
      expect(isNotFoundError(error)).toBe(true)
      expect(isDomainError(error)).toBe(true)
      expect(isValidationError(error)).toBe(false)
    })
  })
  
  describe('ConflictError', () => {
    it('should create conflict error', () => {
      const error = new ConflictError('User already exists', 'DUPLICATE_USER')
      
      expect(error.code).toBe('CONFLICT')
      expect(error.conflictType).toBe('DUPLICATE_USER')
      expect(error.message).toBe('User already exists')
    })
    
    it('should be identifiable by type guard', () => {
      const error = new ConflictError('Conflict occurred', 'RESOURCE_LOCKED')
      
      expect(isConflictError(error)).toBe(true)
      expect(isDomainError(error)).toBe(true)
    })
  })
  
  describe('BusinessRuleError', () => {
    it('should create business rule error', () => {
      const error = new BusinessRuleError(
        'Insufficient balance for withdrawal',
        'INSUFFICIENT_BALANCE'
      )
      
      expect(error.code).toBe('BUSINESS_RULE_ERROR')
      expect(error.rule).toBe('INSUFFICIENT_BALANCE')
      expect(error.message).toBe('Insufficient balance for withdrawal')
    })
    
    it('should be identifiable by type guard', () => {
      const error = new BusinessRuleError('Rule violated', 'MAX_LIMIT_EXCEEDED')
      
      expect(isBusinessRuleError(error)).toBe(true)
      expect(isDomainError(error)).toBe(true)
    })
  })
  
  describe('UnauthorizedError', () => {
    it('should create unauthorized error', () => {
      const error = new UnauthorizedError('Admin access required', 'admin')
      
      expect(error.code).toBe('UNAUTHORIZED')
      expect(error.requiredRole).toBe('admin')
      expect(error.message).toBe('Admin access required')
    })
    
    it('should create unauthorized error without role', () => {
      const error = new UnauthorizedError('Login required')
      
      expect(error.code).toBe('UNAUTHORIZED')
      expect(error.requiredRole).toBeUndefined()
      expect(error.message).toBe('Login required')
    })
    
    it('should be identifiable by type guard', () => {
      const error = new UnauthorizedError('Access denied')
      
      expect(isUnauthorizedError(error)).toBe(true)
      expect(isDomainError(error)).toBe(true)
    })
  })
  
  describe('createDomainError', () => {
    it('should create validation error', () => {
      const error = createDomainError(
        'VALIDATION_ERROR',
        'Invalid email format',
        { field: 'email' }
      )
      
      expect(isValidationError(error)).toBe(true)
      expect((error as ValidationError).field).toBe('email')
    })
    
    it('should create not found error', () => {
      const error = createDomainError(
        'NOT_FOUND',
        'Resource not found',
        { resourceType: 'Product', resourceId: 'prod-123' }
      )
      
      expect(isNotFoundError(error)).toBe(true)
      expect((error as NotFoundError).resourceType).toBe('Product')
    })
    
    it('should create conflict error', () => {
      const error = createDomainError(
        'CONFLICT',
        'Username taken',
        { conflictType: 'DUPLICATE_USERNAME' }
      )
      
      expect(isConflictError(error)).toBe(true)
      expect((error as ConflictError).conflictType).toBe('DUPLICATE_USERNAME')
    })
    
    it('should create business rule error', () => {
      const error = createDomainError(
        'BUSINESS_RULE_ERROR',
        'Age restriction',
        { rule: 'MIN_AGE_REQUIRED' }
      )
      
      expect(isBusinessRuleError(error)).toBe(true)
      expect((error as BusinessRuleError).rule).toBe('MIN_AGE_REQUIRED')
    })
    
    it('should create unauthorized error', () => {
      const error = createDomainError(
        'UNAUTHORIZED',
        'Premium feature',
        { requiredRole: 'premium' }
      )
      
      expect(isUnauthorizedError(error)).toBe(true)
      expect((error as UnauthorizedError).requiredRole).toBe('premium')
    })
    
    it('should create generic domain error for unknown code', () => {
      const error = createDomainError(
        'UNKNOWN_ERROR' as any,
        'Something went wrong'
      )
      
      expect(isDomainError(error)).toBe(true)
      expect(error.code).toBe('UNKNOWN_ERROR')
    })
  })
  
  describe('Error serialization', () => {
    it('should serialize and deserialize correctly', () => {
      const error = new BusinessRuleError('Test error', 'TEST_RULE')
      const json = JSON.stringify(error)
      const parsed = JSON.parse(json)
      
      expect(parsed.code).toBe('BUSINESS_RULE_ERROR')
      expect(parsed.message).toBe('Test error')
      expect(parsed.rule).toBe('TEST_RULE')
      expect(parsed.timestamp).toBeDefined()
    })
  })
})
