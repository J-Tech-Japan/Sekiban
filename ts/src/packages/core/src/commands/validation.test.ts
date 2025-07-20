import { describe, it, expect } from 'vitest'
import {
  validateCommand,
  required,
  minLength,
  maxLength,
  email,
  range,
  pattern,
  custom,
  CommandValidator,
  createCommandValidator
} from './validation'
import { ICommand } from './command'
import { Result } from 'neverthrow'
import { ValidationError } from '../result/errors'

// Test commands
class CreateUserCommand implements ICommand {
  constructor(
    public readonly name: string,
    public readonly email: string,
    public readonly age: number,
    public readonly password: string,
    public readonly phone?: string
  ) {}
}

class UpdateProfileCommand implements ICommand {
  constructor(
    public readonly bio?: string,
    public readonly website?: string,
    public readonly tags?: string[]
  ) {}
}

describe('Command Validation', () => {
  describe('Validation decorators', () => {
    it('should validate required fields', () => {
      // Arrange
      const validator = createCommandValidator<CreateUserCommand>({
        name: [required('Name is required')],
        email: [required('Email is required')]
      })
      
      // Act
      const valid = validator.validate(
        new CreateUserCommand('John', 'john@test.com', 25, 'pass123')
      )
      const invalid = validator.validate(
        new CreateUserCommand('', '', 25, 'pass123')
      )
      
      // Assert
      expect(valid.isOk()).toBe(true)
      expect(invalid.isErr()).toBe(true)
      if (invalid.isErr()) {
        expect(invalid.error).toHaveLength(2)
        expect(invalid.error[0]!.message).toBe('Name is required')
        expect(invalid.error[1]!.message).toBe('Email is required')
      }
    })
    
    it('should validate string length', () => {
      // Arrange
      const validator = createCommandValidator<CreateUserCommand>({
        name: [
          required('Name is required'),
          minLength(2, 'Name must be at least 2 characters'),
          maxLength(50, 'Name must not exceed 50 characters')
        ],
        password: [
          minLength(8, 'Password must be at least 8 characters')
        ]
      })
      
      // Act
      const tooShort = validator.validate(
        new CreateUserCommand('J', 'john@test.com', 25, 'pass')
      )
      const tooLong = validator.validate(
        new CreateUserCommand('A'.repeat(51), 'john@test.com', 25, 'password123')
      )
      const valid = validator.validate(
        new CreateUserCommand('John Doe', 'john@test.com', 25, 'password123')
      )
      
      // Assert
      expect(tooShort.isErr()).toBe(true)
      if (tooShort.isErr()) {
        expect(tooShort.error.some(e => e.message.includes('at least 2'))).toBe(true)
        expect(tooShort.error.some(e => e.message.includes('at least 8'))).toBe(true)
      }
      
      expect(tooLong.isErr()).toBe(true)
      if (tooLong.isErr()) {
        expect(tooLong.error.some(e => e.message.includes('exceed 50'))).toBe(true)
      }
      
      expect(valid.isOk()).toBe(true)
    })
    
    it('should validate email format', () => {
      // Arrange
      const validator = createCommandValidator<CreateUserCommand>({
        email: [
          required('Email is required'),
          email('Invalid email format')
        ]
      })
      
      // Act
      const validEmails = [
        'user@example.com',
        'user.name@example.co.uk',
        'user+tag@example-domain.com'
      ]
      
      const invalidEmails = [
        'invalid',
        '@example.com',
        'user@',
        'user@.com',
        'user..name@example.com'
      ]
      
      // Assert
      validEmails.forEach(validEmail => {
        const result = validator.validate(
          new CreateUserCommand('Name', validEmail, 25, 'pass')
        )
        expect(result.isOk()).toBe(true)
      })
      
      invalidEmails.forEach(invalidEmail => {
        const result = validator.validate(
          new CreateUserCommand('Name', invalidEmail, 25, 'pass')
        )
        expect(result.isErr()).toBe(true, `Expected ${invalidEmail} to be invalid`)
        if (result.isErr()) {
          expect(result.error[0]!.message).toBe('Invalid email format')
        }
      })
    })
    
    it('should validate numeric ranges', () => {
      // Arrange
      const validator = createCommandValidator<CreateUserCommand>({
        age: [
          range(18, 120, 'Age must be between 18 and 120')
        ]
      })
      
      // Act
      const tooYoung = validator.validate(
        new CreateUserCommand('John', 'john@test.com', 17, 'pass')
      )
      const tooOld = validator.validate(
        new CreateUserCommand('John', 'john@test.com', 121, 'pass')
      )
      const valid = validator.validate(
        new CreateUserCommand('John', 'john@test.com', 25, 'pass')
      )
      
      // Assert
      expect(tooYoung.isErr()).toBe(true)
      expect(tooOld.isErr()).toBe(true)
      expect(valid.isOk()).toBe(true)
    })
    
    it('should validate with regex pattern', () => {
      // Arrange
      const validator = createCommandValidator<CreateUserCommand>({
        phone: [
          pattern(/^\+?[1-9]\d{1,14}$/, 'Invalid phone number format')
        ]
      })
      
      // Act
      const valid1 = validator.validate(
        new CreateUserCommand('John', 'john@test.com', 25, 'pass', '+1234567890')
      )
      const valid2 = validator.validate(
        new CreateUserCommand('John', 'john@test.com', 25, 'pass', '1234567890')
      )
      const invalid = validator.validate(
        new CreateUserCommand('John', 'john@test.com', 25, 'pass', '123-456-7890')
      )
      const missing = validator.validate(
        new CreateUserCommand('John', 'john@test.com', 25, 'pass')
      )
      
      // Assert
      expect(valid1.isOk()).toBe(true)
      expect(valid2.isOk()).toBe(true)
      expect(invalid.isErr()).toBe(true)
      expect(missing.isOk()).toBe(true) // Optional field
    })
    
    it('should validate with custom validator', () => {
      // Arrange
      const validator = createCommandValidator<CreateUserCommand>({
        password: [
          custom(
            (value: string) => {
              const hasUpperCase = /[A-Z]/.test(value)
              const hasLowerCase = /[a-z]/.test(value)
              const hasNumber = /[0-9]/.test(value)
              return hasUpperCase && hasLowerCase && hasNumber
            },
            'Password must contain uppercase, lowercase, and number'
          )
        ]
      })
      
      // Act
      const weak = validator.validate(
        new CreateUserCommand('John', 'john@test.com', 25, 'password')
      )
      const strong = validator.validate(
        new CreateUserCommand('John', 'john@test.com', 25, 'Password123')
      )
      
      // Assert
      expect(weak.isErr()).toBe(true)
      expect(strong.isOk()).toBe(true)
    })
  })
  
  describe('Nested validation', () => {
    it('should validate arrays', () => {
      // Arrange
      const validator = createCommandValidator<UpdateProfileCommand>({
        tags: [
          custom(
            (tags?: string[]) => !tags || tags.length <= 5,
            'Maximum 5 tags allowed'
          ),
          custom(
            (tags?: string[]) => !tags || tags.every(tag => tag.length >= 2),
            'Each tag must be at least 2 characters'
          )
        ]
      })
      
      // Act
      const tooMany = validator.validate(
        new UpdateProfileCommand(undefined, undefined, ['a', 'b', 'c', 'd', 'e', 'f'])
      )
      const tooShort = validator.validate(
        new UpdateProfileCommand(undefined, undefined, ['ok', 'a'])
      )
      const valid = validator.validate(
        new UpdateProfileCommand(undefined, undefined, ['typescript', 'testing'])
      )
      
      // Assert
      expect(tooMany.isErr()).toBe(true)
      expect(tooShort.isErr()).toBe(true)
      expect(valid.isOk()).toBe(true)
    })
  })
  
  describe('validateCommand helper', () => {
    it('should validate entire command object', () => {
      // Arrange
      const rules = {
        name: [required('Name required'), minLength(2, 'Too short')],
        email: [required('Email required'), email('Invalid email')],
        age: [range(0, 150, 'Invalid age')]
      }
      
      // Act
      const result = validateCommand(
        new CreateUserCommand('John', 'john@example.com', 30, 'pass'),
        rules
      )
      
      // Assert
      expect(result.isOk()).toBe(true)
    })
    
    it('should collect all validation errors', () => {
      // Arrange
      const rules = {
        name: [required('Name required')],
        email: [email('Invalid email')],
        age: [range(18, 100, 'Must be adult')]
      }
      
      // Act
      const result = validateCommand(
        new CreateUserCommand('', 'invalid', 10, 'pass'),
        rules
      )
      
      // Assert
      expect(result.isErr()).toBe(true)
      if (result.isErr()) {
        expect(result.error).toHaveLength(3)
      }
    })
  })
})