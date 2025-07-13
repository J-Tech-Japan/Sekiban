import { Result, ok, err, ResultAsync } from 'neverthrow'

/**
 * Convert a Promise to a Result
 */
export async function toResult<T>(promise: Promise<T>): Promise<Result<T, Error>> {
  try {
    const value = await promise
    return ok(value)
  } catch (error) {
    if (error instanceof Error) {
      return err(error)
    }
    // Handle non-Error thrown values
    return err(new Error(String(error)))
  }
}

/**
 * Wrap a throwing function to return a Result
 */
export function fromThrowable<TArgs extends any[], TReturn>(
  fn: (...args: TArgs) => TReturn
): (...args: TArgs) => TReturn extends Promise<infer U> ? Promise<Result<U, Error>> : Result<TReturn, Error> {
  return ((...args: TArgs) => {
    try {
      const result = fn(...args)
      
      // Handle async functions
      if (result instanceof Promise) {
        return toResult(result) as any
      }
      
      // Handle sync functions
      return ok(result) as any
    } catch (error) {
      if (error instanceof Error) {
        return err(error) as any
      }
      return err(new Error(String(error))) as any
    }
  }) as any
}

/**
 * Transform error type in a Result
 */
export function mapError<T, E1, E2>(
  result: Result<T, E1>,
  fn: (error: E1) => E2
): Result<T, E2> {
  return result.mapErr(fn)
}

/**
 * Create a type guard for a specific error type
 */
export function isSpecificError<T extends Error>(
  ErrorClass: new (...args: any[]) => T
): (error: unknown) => error is T {
  return (error: unknown): error is T => {
    return error instanceof ErrorClass
  }
}

/**
 * Collect all errors from an array of Results
 */
export function collectErrors<T, E>(results: Result<T, E>[]): E[] {
  return results
    .filter(result => result.isErr())
    .map(result => result._unsafeUnwrapErr())
}

/**
 * Get the first error from an array of Results
 */
export function firstError<T, E>(results: Result<T, E>[]): E | null {
  const firstErrorResult = results.find(result => result.isErr())
  return firstErrorResult?.isErr() ? firstErrorResult._unsafeUnwrapErr() : null
}

/**
 * Chain multiple errors together using the cause property
 */
export function chainErrors(...errors: Error[]): Error | undefined {
  if (errors.length === 0) return undefined
  if (errors.length === 1) return errors[0]
  
  // Create a chain by setting cause property
  for (let i = 0; i < errors.length - 1; i++) {
    Object.defineProperty(errors[i], 'cause', {
      value: errors[i + 1],
      writable: true,
      configurable: true,
      enumerable: false
    })
  }
  
  return errors[0]
}