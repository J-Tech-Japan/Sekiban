import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import {
  formatISOString,
  parseISOString,
  isValidDate,
  addDays,
  addHours,
  addMinutes,
  differenceInDays,
  differenceInHours,
  differenceInMinutes,
  startOfDay,
  endOfDay,
  isToday,
  isSameDay
} from './date'

describe('Date utilities', () => {
  let mockDate: Date
  
  beforeEach(() => {
    // Mock current date to 2024-07-02T12:00:00.000Z
    mockDate = new Date('2024-07-02T12:00:00.000Z')
    vi.useFakeTimers()
    vi.setSystemTime(mockDate)
  })
  
  afterEach(() => {
    vi.useRealTimers()
  })
  
  describe('formatISOString', () => {
    it('should format date to ISO string', () => {
      // Arrange
      const date = new Date('2024-01-15T10:30:45.123Z')
      
      // Act
      const result = formatISOString(date)
      
      // Assert
      expect(result).toBe('2024-01-15T10:30:45.123Z')
    })
    
    it('should handle timezone correctly', () => {
      // Arrange
      const date = new Date('2024-12-25T23:59:59.999Z')
      
      // Act
      const result = formatISOString(date)
      
      // Assert
      expect(result).toBe('2024-12-25T23:59:59.999Z')
    })
    
    it('should format current date', () => {
      // Act
      const result = formatISOString(new Date())
      
      // Assert
      expect(result).toBe('2024-07-02T12:00:00.000Z')
    })
  })
  
  describe('parseISOString', () => {
    it('should parse valid ISO string', () => {
      // Arrange
      const isoString = '2024-01-15T10:30:45.123Z'
      
      // Act
      const result = parseISOString(isoString)
      
      // Assert
      expect(result.isOk()).toBe(true)
      const date = result._unsafeUnwrap()
      expect(date.getTime()).toBe(new Date(isoString).getTime())
    })
    
    it('should parse ISO string without milliseconds', () => {
      // Arrange
      const isoString = '2024-01-15T10:30:45Z'
      
      // Act
      const result = parseISOString(isoString)
      
      // Assert
      expect(result.isOk()).toBe(true)
      const date = result._unsafeUnwrap()
      expect(formatISOString(date)).toBe('2024-01-15T10:30:45.000Z')
    })
    
    it('should fail for invalid ISO string', () => {
      // Arrange
      const invalidStrings = [
        'not-a-date',
        '2024-13-01T10:30:45Z', // invalid month
        '2024-01-32T10:30:45Z', // invalid day
        '2024-01-01T25:30:45Z', // invalid hour
        '2024-01-01T10:60:45Z', // invalid minute
        '2024-01-01T10:30:60Z', // invalid second
        '',
        null as any,
        undefined as any
      ]
      
      // Act & Assert
      invalidStrings.forEach(invalidString => {
        const result = parseISOString(invalidString)
        expect(result.isErr()).toBe(true)
      })
    })
  })
  
  describe('isValidDate', () => {
    it('should validate correct dates', () => {
      // Arrange
      const validDates = [
        new Date(),
        new Date('2024-01-01'),
        new Date(2024, 0, 1),
        new Date(Date.now())
      ]
      
      // Act & Assert
      validDates.forEach(date => {
        expect(isValidDate(date)).toBe(true)
      })
    })
    
    it('should reject invalid dates', () => {
      // Arrange
      const invalidDates = [
        new Date('invalid'),
        new Date(NaN),
        null as any,
        undefined as any,
        'string' as any,
        123 as any
      ]
      
      // Act & Assert
      invalidDates.forEach(date => {
        expect(isValidDate(date)).toBe(false)
      })
    })
  })
  
  describe('addDays', () => {
    it('should add positive days', () => {
      // Arrange
      const date = new Date('2024-01-15T10:30:00Z')
      
      // Act
      const result = addDays(date, 5)
      
      // Assert
      expect(formatISOString(result)).toBe('2024-01-20T10:30:00.000Z')
    })
    
    it('should subtract days with negative value', () => {
      // Arrange
      const date = new Date('2024-01-15T10:30:00Z')
      
      // Act
      const result = addDays(date, -5)
      
      // Assert
      expect(formatISOString(result)).toBe('2024-01-10T10:30:00.000Z')
    })
    
    it('should handle month boundaries', () => {
      // Arrange
      const date = new Date('2024-01-30T10:30:00Z')
      
      // Act
      const result = addDays(date, 5)
      
      // Assert
      expect(formatISOString(result)).toBe('2024-02-04T10:30:00.000Z')
    })
    
    it('should handle leap year', () => {
      // Arrange
      const date = new Date('2024-02-28T10:30:00Z')
      
      // Act
      const result = addDays(date, 1)
      
      // Assert
      expect(formatISOString(result)).toBe('2024-02-29T10:30:00.000Z')
    })
  })
  
  describe('addHours', () => {
    it('should add hours correctly', () => {
      // Arrange
      const date = new Date('2024-01-15T10:30:00Z')
      
      // Act
      const result = addHours(date, 5)
      
      // Assert
      expect(formatISOString(result)).toBe('2024-01-15T15:30:00.000Z')
    })
    
    it('should handle day boundary', () => {
      // Arrange
      const date = new Date('2024-01-15T22:30:00Z')
      
      // Act
      const result = addHours(date, 5)
      
      // Assert
      expect(formatISOString(result)).toBe('2024-01-16T03:30:00.000Z')
    })
  })
  
  describe('addMinutes', () => {
    it('should add minutes correctly', () => {
      // Arrange
      const date = new Date('2024-01-15T10:30:00Z')
      
      // Act
      const result = addMinutes(date, 45)
      
      // Assert
      expect(formatISOString(result)).toBe('2024-01-15T11:15:00.000Z')
    })
    
    it('should handle hour boundary', () => {
      // Arrange
      const date = new Date('2024-01-15T10:45:00Z')
      
      // Act
      const result = addMinutes(date, 30)
      
      // Assert
      expect(formatISOString(result)).toBe('2024-01-15T11:15:00.000Z')
    })
  })
  
  describe('differenceInDays', () => {
    it('should calculate positive difference', () => {
      // Arrange
      const startDate = new Date('2024-01-15T10:30:00Z')
      const endDate = new Date('2024-01-20T10:30:00Z')
      
      // Act
      const result = differenceInDays(endDate, startDate)
      
      // Assert
      expect(result).toBe(5)
    })
    
    it('should calculate negative difference', () => {
      // Arrange
      const startDate = new Date('2024-01-20T10:30:00Z')
      const endDate = new Date('2024-01-15T10:30:00Z')
      
      // Act
      const result = differenceInDays(endDate, startDate)
      
      // Assert
      expect(result).toBe(-5)
    })
    
    it('should handle partial days', () => {
      // Arrange
      const startDate = new Date('2024-01-15T10:30:00Z')
      const endDate = new Date('2024-01-16T08:30:00Z')
      
      // Act
      const result = differenceInDays(endDate, startDate)
      
      // Assert
      expect(result).toBe(0) // Less than 24 hours
    })
  })
  
  describe('startOfDay', () => {
    it('should return start of day', () => {
      // Arrange
      const date = new Date('2024-01-15T14:30:45.123Z')
      
      // Act
      const result = startOfDay(date)
      
      // Assert
      expect(formatISOString(result)).toBe('2024-01-15T00:00:00.000Z')
    })
  })
  
  describe('endOfDay', () => {
    it('should return end of day', () => {
      // Arrange
      const date = new Date('2024-01-15T14:30:45.123Z')
      
      // Act
      const result = endOfDay(date)
      
      // Assert
      expect(formatISOString(result)).toBe('2024-01-15T23:59:59.999Z')
    })
  })
  
  describe('isToday', () => {
    it('should return true for today', () => {
      // Arrange
      const today = new Date('2024-07-02T08:00:00Z')
      
      // Act & Assert
      expect(isToday(today)).toBe(true)
    })
    
    it('should return false for yesterday', () => {
      // Arrange
      const yesterday = new Date('2024-07-01T20:00:00Z')
      
      // Act & Assert
      expect(isToday(yesterday)).toBe(false)
    })
    
    it('should return false for tomorrow', () => {
      // Arrange
      const tomorrow = new Date('2024-07-03T02:00:00Z')
      
      // Act & Assert
      expect(isToday(tomorrow)).toBe(false)
    })
  })
  
  describe('isSameDay', () => {
    it('should return true for same day different times', () => {
      // Arrange
      const date1 = new Date('2024-01-15T08:30:00Z')
      const date2 = new Date('2024-01-15T20:45:00Z')
      
      // Act & Assert
      expect(isSameDay(date1, date2)).toBe(true)
    })
    
    it('should return false for different days', () => {
      // Arrange
      const date1 = new Date('2024-01-15T23:59:59Z')
      const date2 = new Date('2024-01-16T00:00:01Z')
      
      // Act & Assert
      expect(isSameDay(date1, date2)).toBe(false)
    })
  })
  
  describe('event sourcing scenarios', () => {
    it('should handle event timestamp ordering', () => {
      // Arrange
      const baseTime = new Date('2024-01-15T10:00:00Z')
      const events = [
        addMinutes(baseTime, 0),
        addMinutes(baseTime, 5),
        addMinutes(baseTime, 10),
        addMinutes(baseTime, 15)
      ]
      
      // Act
      const sortedEvents = [...events].sort((a, b) => a.getTime() - b.getTime())
      
      // Assert
      expect(sortedEvents).toEqual(events)
      expect(differenceInMinutes(events[3], events[0])).toBe(15)
    })
    
    it('should handle snapshot expiration', () => {
      // Arrange
      const snapshotTime = new Date('2024-01-15T10:00:00Z')
      const currentTime = new Date('2024-01-20T10:00:00Z')
      const maxAgeInDays = 7
      
      // Act
      const ageInDays = differenceInDays(currentTime, snapshotTime)
      const isExpired = ageInDays >= maxAgeInDays
      
      // Assert
      expect(ageInDays).toBe(5)
      expect(isExpired).toBe(false)
    })
  })
})
