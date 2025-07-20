import { describe, it, expect } from 'vitest'
import { ok, err, Result } from 'neverthrow'
import {
  toResult,
  fromThrowable,
  mapError,
  isSpecificError,
  collectErrors,
  firstError,
  chainErrors
} from './error-utils'
import {
  ValidationError,
  EventStoreError,
  ConcurrencyError,
  AggregateNotFoundError,
  CommandValidationError
} from './errors'

describe('Error Utils', () => {
  describe('toResult', () => {
    it('should convert promise to Result', async () => {
      // Arrange
      const successPromise = Promise.resolve('success')
      const failurePromise = Promise.reject(new ValidationError('Invalid input'))
      
      // Act
      const successResult = await toResult(successPromise)
      const failureResult = await toResult(failurePromise)
      
      // Assert
      expect(successResult.isOk()).toBe(true)
      expect(successResult._unsafeUnwrap()).toBe('success')
      
      expect(failureResult.isErr()).toBe(true)
      expect(failureResult._unsafeUnwrapErr()).toBeInstanceOf(ValidationError)
    })
    
    it('should handle non-Error rejections', async () => {
      const stringReject = Promise.reject('string error')
      const result = await toResult(stringReject)
      
      expect(result.isErr()).toBe(true)
      const error = result._unsafeUnwrapErr()
      expect(error).toBeInstanceOf(Error)
      expect(error.message).toBe('string error')
    })
  })
  
  describe('fromThrowable', () => {
    it('should wrap throwing function', () => {
      // Arrange
      const throwingFn = (shouldThrow: boolean): string => {
        if (shouldThrow) throw new EventStoreError('write', 'Disk full')
        return 'success'
      }
      
      const safeFn = fromThrowable(throwingFn)
      
      // Act
      const successResult = safeFn(false)
      const failureResult = safeFn(true)
      
      // Assert
      expect(successResult.isOk()).toBe(true)
      expect(successResult._unsafeUnwrap()).toBe('success')
      
      expect(failureResult.isErr()).toBe(true)
      expect(failureResult._unsafeUnwrapErr()).toBeInstanceOf(EventStoreError)
    })
    
    it('should work with async functions', async () => {
      const asyncThrowingFn = async (shouldThrow: boolean): Promise<number> => {
        if (shouldThrow) throw new ConcurrencyError(2, 1)
        return 42
      }
      
      const safeAsyncFn = fromThrowable(asyncThrowingFn)
      
      const successResult = await safeAsyncFn(false)
      const failureResult = await safeAsyncFn(true)
      
      expect(successResult.isOk()).toBe(true)
      expect(successResult._unsafeUnwrap()).toBe(42)
      
      expect(failureResult.isErr()).toBe(true)
      expect(failureResult._unsafeUnwrapErr()).toBeInstanceOf(ConcurrencyError)
    })
  })
  
  describe('mapError', () => {
    it('should transform error type', () => {
      // Arrange
      const result: Result<string, ValidationError> = err(
        new ValidationError('Invalid email')
      )
      
      // Act
      const mapped = mapError(
        result, 
        (error) => new CommandValidationError('CreateUser', [error.message])
      )
      
      // Assert
      expect(mapped.isErr()).toBe(true)
      const mappedError = mapped._unsafeUnwrapErr()
      expect(mappedError).toBeInstanceOf(CommandValidationError)
      expect(mappedError.commandType).toBe('CreateUser')
      expect(mappedError.validationErrors).toEqual(['Invalid email'])
    })
    
    it('should not affect ok results', () => {
      const result: Result<string, ValidationError> = ok('success')
      
      const mapped = mapError(
        result,
        () => new CommandValidationError('Never', ['called'])
      )
      
      expect(mapped.isOk()).toBe(true)
      expect(mapped._unsafeUnwrap()).toBe('success')
    })
  })
  
  describe('isSpecificError', () => {
    it('should create type guard for specific error', () => {
      // Arrange
      const isAggregateNotFound = isSpecificError(AggregateNotFoundError)
      const isConcurrencyError = isSpecificError(ConcurrencyError)
      
      const aggregateError = new AggregateNotFoundError('123', 'User')
      const concurrencyError = new ConcurrencyError(2, 1)
      const genericError = new Error('generic')
      
      // Act & Assert
      expect(isAggregateNotFound(aggregateError)).toBe(true)
      expect(isAggregateNotFound(concurrencyError)).toBe(false)
      expect(isAggregateNotFound(genericError)).toBe(false)
      
      expect(isConcurrencyError(concurrencyError)).toBe(true)
      expect(isConcurrencyError(aggregateError)).toBe(false)
    })
    
    it('should work in Result error checking', () => {
      const result: Result<string, Error> = err(
        new EventStoreError('read', 'Connection lost')
      )
      
      const isEventStoreError = isSpecificError(EventStoreError)
      
      if (result.isErr() && isEventStoreError(result.error)) {
        expect(result.error.operation).toBe('read')
        expect(result.error.reason).toBe('Connection lost')
      } else {
        expect.fail('Should have matched EventStoreError')
      }
    })
  })
  
  describe('collectErrors', () => {
    it('should collect all errors from results', () => {
      // Arrange
      const results: Result<string, Error>[] = [
        ok('success1'),
        err(new ValidationError('Error 1')),
        ok('success2'),
        err(new ValidationError('Error 2')),
        err(new EventStoreError('write', 'Error 3'))
      ]
      
      // Act
      const errors = collectErrors(results)
      
      // Assert
      expect(errors).toHaveLength(3)
      expect(errors[0].message).toBe('Error 1')
      expect(errors[1].message).toBe('Error 2')
      expect(errors[2]).toBeInstanceOf(EventStoreError)
    })
    
    it('should return empty array when all results are ok', () => {
      const results: Result<number, Error>[] = [
        ok(1),
        ok(2),
        ok(3)
      ]
      
      const errors = collectErrors(results)
      expect(errors).toHaveLength(0)
    })
  })
  
  describe('firstError', () => {
    it('should return first error found', () => {
      // Arrange
      const results: Result<string, Error>[] = [
        ok('success1'),
        err(new ValidationError('First error')),
        err(new ValidationError('Second error')),
        ok('success2')
      ]
      
      // Act
      const error = firstError(results)
      
      // Assert
      expect(error).not.toBeNull()
      expect(error?.message).toBe('First error')
    })
    
    it('should return null when no errors', () => {
      const results: Result<number, Error>[] = [
        ok(1),
        ok(2),
        ok(3)
      ]
      
      const error = firstError(results)
      expect(error).toBeNull()
    })
  })
  
  describe('chainErrors', () => {
    it('should chain error causes', () => {
      // Arrange
      const rootCause = new ValidationError('Invalid format')
      const middleError = new EventStoreError('write', 'Failed to persist')
      const topError = new CommandValidationError('CreateUser', ['Command failed'])
      
      // Act
      const chained = chainErrors(topError, middleError, rootCause)
      
      // Assert
      expect(chained).toBe(topError)
      expect(chained.cause).toBe(middleError)
      expect((chained.cause as Error).cause).toBe(rootCause)
    })
    
    it('should handle single error', () => {
      const error = new ValidationError('Single error')
      const chained = chainErrors(error)
      
      expect(chained).toBe(error)
      expect(chained.cause).toBeUndefined()
    })
    
    it('should handle empty array', () => {
      const chained = chainErrors()
      expect(chained).toBeUndefined()
    })
  })
})