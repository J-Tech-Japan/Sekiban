import { z } from 'zod'
import type { ValidationError, ValidationResult, Validator, ZodSchema } from './types.js'

export type { ValidationError, ValidationResult, Validator } from './types.js'

/**
 * Create a validator from a Zod schema
 */
export function createValidator<T>(schema: z.ZodType<T, any, any>): Validator<T> {
  return {
    validate(data: unknown): ValidationResult<T> {
      const result = schema.safeParse(data)
      
      if (result.success) {
        return { success: true, data: result.data }
      }
      
      const errors: ValidationError[] = result.error.errors.map(err => ({
        path: err.path,
        message: err.message
      }))
      
      return { success: false, errors }
    }
  }
}

/**
 * Check if a validation result is successful
 */
export function isValid<T>(result: ValidationResult<T>): result is { success: true; data: T } {
  return result.success
}

/**
 * Get errors from a validation result
 */
export function getErrors<T>(result: ValidationResult<T>): ValidationError[] {
  return result.success ? [] : result.errors
}

/**
 * Validate data and throw if invalid
 */
export function validateOrThrow<T>(validator: Validator<T>, data: unknown): T {
  const result = validator.validate(data)
  
  if (!result.success) {
    throw new Error('Validation failed')
  }
  
  return result.data
}