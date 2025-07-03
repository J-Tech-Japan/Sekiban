import { z } from 'zod'

export interface ValidationError {
  path: (string | number)[]
  message: string
}

export type ValidationResult<T> = 
  | { success: true; data: T; errors?: undefined }
  | { success: false; data?: undefined; errors: ValidationError[] }

export interface Validator<T> {
  validate(data: unknown): ValidationResult<T>
}

export type ZodSchema = z.ZodType<any, any, any>