import { describe, it, expect } from 'vitest'
import { 
  createValidator,
  isValid,
  getErrors,
  validateOrThrow,
  type ValidationResult,
  type Validator
} from './validation.js'
import { z } from 'zod'

describe('Validation utilities', () => {
  describe('createValidator', () => {
    it('should create a validator from Zod schema', () => {
      // Arrange
      const schema = z.object({
        name: z.string(),
        age: z.number().positive()
      })
      
      // Act
      const validator = createValidator(schema)
      
      // Assert
      expect(validator).toBeDefined()
      expect(typeof validator.validate).toBe('function')
    })
  })
  
  describe('validator.validate', () => {
    it('should return success result for valid data', () => {
      // Arrange
      const schema = z.object({
        name: z.string(),
        age: z.number().positive()
      })
      const validator = createValidator(schema)
      const validData = { name: 'John', age: 30 }
      
      // Act
      const result = validator.validate(validData)
      
      // Assert
      expect(result.success).toBe(true)
      expect(result.data).toEqual(validData)
      expect(result.errors).toBeUndefined()
    })
    
    it('should return error result for invalid data', () => {
      // Arrange
      const schema = z.object({
        name: z.string(),
        age: z.number().positive()
      })
      const validator = createValidator(schema)
      const invalidData = { name: 'John', age: -5 }
      
      // Act
      const result = validator.validate(invalidData)
      
      // Assert
      expect(result.success).toBe(false)
      expect(result.data).toBeUndefined()
      expect(result.errors).toBeDefined()
      expect(result.errors).toHaveLength(1)
      expect(result.errors![0].path).toEqual(['age'])
      expect(result.errors![0].message).toContain('greater than 0')
    })
    
    it('should handle multiple validation errors', () => {
      // Arrange
      const schema = z.object({
        name: z.string().min(3),
        age: z.number().positive(),
        email: z.string().email()
      })
      const validator = createValidator(schema)
      const invalidData = { name: 'Jo', age: -5, email: 'invalid' }
      
      // Act
      const result = validator.validate(invalidData)
      
      // Assert
      expect(result.success).toBe(false)
      expect(result.errors).toHaveLength(3)
      expect(result.errors!.map(e => e.path[0])).toContain('name')
      expect(result.errors!.map(e => e.path[0])).toContain('age')
      expect(result.errors!.map(e => e.path[0])).toContain('email')
    })
  })
  
  describe('isValid', () => {
    it('should return true for successful validation', () => {
      // Arrange
      const result: ValidationResult<any> = {
        success: true,
        data: { name: 'John' }
      }
      
      // Act & Assert
      expect(isValid(result)).toBe(true)
    })
    
    it('should return false for failed validation', () => {
      // Arrange
      const result: ValidationResult<any> = {
        success: false,
        errors: [{ path: ['field'], message: 'error' }]
      }
      
      // Act & Assert
      expect(isValid(result)).toBe(false)
    })
  })
  
  describe('getErrors', () => {
    it('should return empty array for successful validation', () => {
      // Arrange
      const result: ValidationResult<any> = {
        success: true,
        data: { name: 'John' }
      }
      
      // Act
      const errors = getErrors(result)
      
      // Assert
      expect(errors).toEqual([])
    })
    
    it('should return errors for failed validation', () => {
      // Arrange
      const expectedErrors = [
        { path: ['field1'], message: 'error1' },
        { path: ['field2'], message: 'error2' }
      ]
      const result: ValidationResult<any> = {
        success: false,
        errors: expectedErrors
      }
      
      // Act
      const errors = getErrors(result)
      
      // Assert
      expect(errors).toEqual(expectedErrors)
    })
  })
  
  describe('validateOrThrow', () => {
    it('should return data for valid input', () => {
      // Arrange
      const schema = z.object({
        name: z.string()
      })
      const validator = createValidator(schema)
      const validData = { name: 'John' }
      
      // Act
      const result = validateOrThrow(validator, validData)
      
      // Assert
      expect(result).toEqual(validData)
    })
    
    it('should throw ValidationError for invalid input', () => {
      // Arrange
      const schema = z.object({
        name: z.string().min(3)
      })
      const validator = createValidator(schema)
      const invalidData = { name: 'Jo' }
      
      // Act & Assert
      expect(() => validateOrThrow(validator, invalidData))
        .toThrow('Validation failed')
    })
  })
  
  describe('complex validation scenarios', () => {
    it('should validate nested objects', () => {
      // Arrange
      const addressSchema = z.object({
        street: z.string(),
        city: z.string(),
        zipCode: z.string().regex(/^\d{5}$/)
      })
      
      const userSchema = z.object({
        name: z.string(),
        address: addressSchema
      })
      
      const validator = createValidator(userSchema)
      const validData = {
        name: 'John',
        address: {
          street: '123 Main St',
          city: 'New York',
          zipCode: '12345'
        }
      }
      
      // Act
      const result = validator.validate(validData)
      
      // Assert
      expect(result.success).toBe(true)
      expect(result.data).toEqual(validData)
    })
    
    it('should validate arrays', () => {
      // Arrange
      const schema = z.object({
        tags: z.array(z.string().min(2))
      })
      const validator = createValidator(schema)
      
      // Act
      const validResult = validator.validate({ tags: ['tag1', 'tag2'] })
      const invalidResult = validator.validate({ tags: ['ok', 'x'] })
      
      // Assert
      expect(validResult.success).toBe(true)
      expect(invalidResult.success).toBe(false)
      expect(invalidResult.errors![0].path).toEqual(['tags', 1])
    })
    
    it('should validate discriminated unions', () => {
      // Arrange
      const schema = z.discriminatedUnion('type', [
        z.object({ type: z.literal('email'), email: z.string().email() }),
        z.object({ type: z.literal('phone'), phoneNumber: z.string() })
      ])
      const validator = createValidator(schema)
      
      // Act
      const emailResult = validator.validate({ type: 'email', email: 'test@example.com' })
      const phoneResult = validator.validate({ type: 'phone', phoneNumber: '+1234567890' })
      const invalidResult = validator.validate({ type: 'email', email: 'invalid' })
      
      // Assert
      expect(emailResult.success).toBe(true)
      expect(phoneResult.success).toBe(true)
      expect(invalidResult.success).toBe(false)
    })
    
    it('should handle optional fields', () => {
      // Arrange
      const schema = z.object({
        required: z.string(),
        optional: z.string().optional(),
        nullable: z.string().nullable(),
        withDefault: z.string().default('default')
      })
      const validator = createValidator(schema)
      
      // Act
      const result = validator.validate({ 
        required: 'value',
        nullable: null
      })
      
      // Assert
      expect(result.success).toBe(true)
      expect(result.data).toEqual({
        required: 'value',
        nullable: null,
        withDefault: 'default'
      })
    })
  })
  
  describe('domain-specific validations', () => {
    it('should validate aggregate IDs', () => {
      // Arrange
      const aggregateIdSchema = z.string().uuid()
      const validator = createValidator(aggregateIdSchema)
      
      // Act
      const validResult = validator.validate('123e4567-e89b-12d3-a456-426614174000')
      const invalidResult = validator.validate('not-a-uuid')
      
      // Assert
      expect(validResult.success).toBe(true)
      expect(invalidResult.success).toBe(false)
    })
    
    it('should validate event payload', () => {
      // Arrange
      const eventSchema = z.object({
        eventType: z.string().min(1),
        aggregateId: z.string().uuid(),
        payload: z.record(z.unknown()),
        metadata: z.object({
          userId: z.string(),
          timestamp: z.string().datetime()
        }).optional()
      })
      const validator = createValidator(eventSchema)
      
      // Act
      const result = validator.validate({
        eventType: 'UserCreated',
        aggregateId: '123e4567-e89b-12d3-a456-426614174000',
        payload: { name: 'John', email: 'john@example.com' }
      })
      
      // Assert
      expect(result.success).toBe(true)
    })
    
    it('should validate command with custom refinements', () => {
      // Arrange
      const commandSchema = z.object({
        name: z.string().min(1),
        startDate: z.string().datetime(),
        endDate: z.string().datetime()
      }).refine(
        data => new Date(data.endDate) > new Date(data.startDate),
        { message: 'End date must be after start date', path: ['endDate'] }
      )
      const validator = createValidator(commandSchema)
      
      // Act
      const validResult = validator.validate({
        name: 'Project',
        startDate: '2024-01-01T00:00:00Z',
        endDate: '2024-12-31T23:59:59Z'
      })
      
      const invalidResult = validator.validate({
        name: 'Project',
        startDate: '2024-12-31T00:00:00Z',
        endDate: '2024-01-01T00:00:00Z'
      })
      
      // Assert
      expect(validResult.success).toBe(true)
      expect(invalidResult.success).toBe(false)
      expect(invalidResult.errors![0].message).toContain('after start date')
    })
  })
})