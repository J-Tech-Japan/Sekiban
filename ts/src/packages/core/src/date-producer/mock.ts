import type { ISekibanDateProducer } from './types'

/**
 * Create a mock date producer with fixed dates for testing
 */
export function createMockDateProducer(fixedDate: Date): ISekibanDateProducer {
  return {
    now: () => fixedDate,
    utcNow: () => fixedDate,
    today: () => new Date(fixedDate.getFullYear(), fixedDate.getMonth(), fixedDate.getDate())
  }
}

/**
 * Create a mock date producer that returns sequential dates
 */
export function createSequentialDateProducer(startDate: Date, intervalMs: number = 1000): ISekibanDateProducer {
  let counter = 0
  
  return {
    now: () => {
      const date = new Date(startDate.getTime() + (counter * intervalMs))
      counter++
      return date
    },
    utcNow: () => {
      const date = new Date(startDate.getTime() + (counter * intervalMs))
      counter++
      return date
    },
    today: () => {
      const baseDate = new Date(startDate.getTime() + (counter * intervalMs))
      return new Date(baseDate.getFullYear(), baseDate.getMonth(), baseDate.getDate())
    }
  }
}
