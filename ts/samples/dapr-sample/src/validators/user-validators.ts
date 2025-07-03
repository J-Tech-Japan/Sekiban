import { z } from 'zod';

const CreateUserRequestSchema = z.object({
  name: z.string()
    .min(1, 'Name is required')
    .max(100, 'Name must be less than 100 characters')
    .trim(),
  email: z.string()
    .email('Invalid email format')
    .max(255, 'Email must be less than 255 characters')
    .toLowerCase()
    .trim()
});

export type CreateUserRequest = z.infer<typeof CreateUserRequestSchema>;

export interface ValidationResult<T> {
  success: boolean;
  data?: T;
  errors?: string[];
}

export function validateCreateUserRequest(data: unknown): ValidationResult<CreateUserRequest> {
  try {
    const validated = CreateUserRequestSchema.parse(data);
    return {
      success: true,
      data: validated
    };
  } catch (error) {
    if (error instanceof z.ZodError) {
      return {
        success: false,
        errors: error.errors.map(e => e.message)
      };
    }
    return {
      success: false,
      errors: ['Invalid request data']
    };
  }
}