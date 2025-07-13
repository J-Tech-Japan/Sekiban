import { Result, ok, err } from 'neverthrow'
import { ValidationError } from '../result/errors.js'

/**
 * Validation rule function
 */
export type ValidationRule<T> = (value: T) => boolean | string

/**
 * Command validator
 */
export interface CommandValidator<TCommand> {
  validate(command: TCommand): Result<void, ValidationError[]>
}

/**
 * Validation rules for a command
 */
export type ValidationRules<TCommand> = {
  [K in keyof TCommand]?: ValidationRule<TCommand[K]>[]
}

/**
 * Create a command validator
 */
export function createCommandValidator<TCommand>(
  rules: ValidationRules<TCommand>
): CommandValidator<TCommand> {
  return {
    validate(command: TCommand): Result<void, ValidationError[]> {
      const errors: ValidationError[] = []
      
      for (const [field, fieldRules] of Object.entries(rules) as [keyof TCommand, ValidationRule<any>[]][]) {
        const value = command[field]
        
        for (const rule of fieldRules) {
          const result = rule(value)
          
          if (result !== true) {
            errors.push(new ValidationError(
              typeof result === 'string' ? result : `Validation failed for ${String(field)}`
            ))
          }
        }
      }
      
      return errors.length > 0 ? err(errors) : ok(undefined)
    }
  }
}

/**
 * Validate a command with rules
 */
export function validateCommand<TCommand>(
  command: TCommand,
  rules: ValidationRules<TCommand>
): Result<void, ValidationError[]> {
  const validator = createCommandValidator(rules)
  return validator.validate(command)
}

/**
 * Required field validator
 */
export function required(message: string): ValidationRule<any> {
  return (value: any) => {
    if (value === null || value === undefined || value === '') {
      return message
    }
    return true
  }
}

/**
 * Minimum length validator
 */
export function minLength(min: number, message: string): ValidationRule<string> {
  return (value: string) => {
    if (value && value.length < min) {
      return message
    }
    return true
  }
}

/**
 * Maximum length validator
 */
export function maxLength(max: number, message: string): ValidationRule<string> {
  return (value: string) => {
    if (value && value.length > max) {
      return message
    }
    return true
  }
}

/**
 * Email validator
 */
export function email(message: string): ValidationRule<string> {
  return (value: string) => {
    if (!value) return true
    
    // Basic structure check
    const parts = value.split('@')
    if (parts.length !== 2) return message
    
    const [local, domain] = parts
    
    // Check local part
    if (!local || local.length === 0) return message
    if (local.includes('..')) return message
    if (local.startsWith('.') || local.endsWith('.')) return message
    
    // Check domain part
    if (!domain || domain.length === 0) return message
    if (!domain.includes('.')) return message
    if (domain.startsWith('.') || domain.endsWith('.')) return message
    if (domain.includes('..')) return message
    
    // Basic character validation
    const validChars = /^[a-zA-Z0-9.!#$%&'*+\/=?^_`{|}~-]+@[a-zA-Z0-9.-]+$/
    if (!validChars.test(value)) return message
    
    return true
  }
}

/**
 * Range validator for numbers
 */
export function range(min: number, max: number, message: string): ValidationRule<number> {
  return (value: number) => {
    if (value < min || value > max) {
      return message
    }
    return true
  }
}

/**
 * Pattern validator
 */
export function pattern(regex: RegExp, message: string): ValidationRule<string> {
  return (value: string) => {
    if (value && !regex.test(value)) {
      return message
    }
    return true
  }
}

/**
 * Custom validator
 */
export function custom<T>(
  predicate: (value: T) => boolean,
  message: string
): ValidationRule<T> {
  return (value: T) => {
    if (!predicate(value)) {
      return message
    }
    return true
  }
}