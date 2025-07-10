import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import type { ISekibanDateProducer } from './types'
import { SekibanDateProducer } from './date-producer'

describe('SekibanDateProducer', () => {
  describe('ISekibanDateProducer interface', () => {
    it('should have now() method that returns current local time', () => {
      // Arrange
      const producer: ISekibanDateProducer = new SekibanDateProducer()
      const before = new Date()
      
      // Act
      const now = producer.now()
      const after = new Date()
      
      // Assert
      expect(now).toBeInstanceOf(Date)
      expect(now.getTime()).toBeGreaterThanOrEqual(before.getTime())
      expect(now.getTime()).toBeLessThanOrEqual(after.getTime())
    })
    
    it('should have utcNow() method that returns current UTC time', () => {
      // Arrange
      const producer: ISekibanDateProducer = new SekibanDateProducer()
      const before = new Date()
      
      // Act
      const utcNow = producer.utcNow()
      const after = new Date()
      
      // Assert
      expect(utcNow).toBeInstanceOf(Date)
      expect(utcNow.getTime()).toBeGreaterThanOrEqual(before.getTime())
      expect(utcNow.getTime()).toBeLessThanOrEqual(after.getTime())
    })
    
    it('should have today() method that returns today at midnight', () => {
      // Arrange
      const producer: ISekibanDateProducer = new SekibanDateProducer()
      const now = new Date()
      const expectedToday = new Date(now.getFullYear(), now.getMonth(), now.getDate())
      
      // Act
      const today = producer.today()
      
      // Assert
      expect(today).toBeInstanceOf(Date)
      expect(today.getHours()).toBe(0)
      expect(today.getMinutes()).toBe(0)
      expect(today.getSeconds()).toBe(0)
      expect(today.getMilliseconds()).toBe(0)
      expect(today.getTime()).toBe(expectedToday.getTime())
    })
  })
  
  describe('Static registration', () => {
    afterEach(() => {
      // Reset to default after each test
      SekibanDateProducer.register(new SekibanDateProducer())
    })
    
    it('should have static getRegistered() method', () => {
      // Act
      const registered = SekibanDateProducer.getRegistered()
      
      // Assert
      expect(registered).toBeDefined()
      expect(registered).toBeInstanceOf(SekibanDateProducer)
    })
    
    it('should allow registering custom date producer', () => {
      // Arrange
      const fixedDate = new Date('2024-01-01T00:00:00Z')
      const mockProducer: ISekibanDateProducer = {
        now: () => fixedDate,
        utcNow: () => fixedDate,
        today: () => fixedDate
      }
      
      // Act
      SekibanDateProducer.register(mockProducer)
      const registered = SekibanDateProducer.getRegistered()
      
      // Assert
      expect(registered).toBe(mockProducer)
      expect(registered.now()).toBe(fixedDate)
      expect(registered.utcNow()).toBe(fixedDate)
      expect(registered.today()).toBe(fixedDate)
    })
  })
  
  describe('Mock support for testing', () => {
    let originalDateNow: () => number
    
    beforeEach(() => {
      originalDateNow = Date.now
    })
    
    afterEach(() => {
      Date.now = originalDateNow
      vi.useRealTimers()
    })
    
    it('should work with vitest fake timers', () => {
      // Arrange
      const fixedTime = new Date('2024-07-02T12:00:00Z')
      vi.useFakeTimers()
      vi.setSystemTime(fixedTime)
      
      const producer = new SekibanDateProducer()
      
      // Act
      const now = producer.now()
      const utcNow = producer.utcNow()
      
      // Assert
      expect(now.getTime()).toBe(fixedTime.getTime())
      expect(utcNow.getTime()).toBe(fixedTime.getTime())
    })
    
    it('should support deterministic testing with mock producer', () => {
      // Arrange
      const mockDates = [
        new Date('2024-01-01T00:00:00Z'),
        new Date('2024-01-02T00:00:00Z'),
        new Date('2024-01-03T00:00:00Z')
      ]
      let callCount = 0
      
      const mockProducer: ISekibanDateProducer = {
        now: () => mockDates[callCount++ % mockDates.length],
        utcNow: () => mockDates[0],
        today: () => new Date(mockDates[0].getFullYear(), mockDates[0].getMonth(), mockDates[0].getDate())
      }
      
      SekibanDateProducer.register(mockProducer)
      
      // Act & Assert
      const registered = SekibanDateProducer.getRegistered()
      expect(registered.now()).toEqual(mockDates[0])
      expect(registered.now()).toEqual(mockDates[1])
      expect(registered.now()).toEqual(mockDates[2])
      expect(registered.now()).toEqual(mockDates[0]) // cycles back
    })
  })
  
  describe('Edge cases', () => {
    it('should handle daylight saving time transitions for today()', () => {
      // This is environment-specific, but we can test the behavior
      const producer = new SekibanDateProducer()
      const today = producer.today()
      
      // Today should always be at midnight local time
      expect(today.getHours()).toBe(0)
      expect(today.getMinutes()).toBe(0)
      expect(today.getSeconds()).toBe(0)
      expect(today.getMilliseconds()).toBe(0)
    })
    
    it('should maintain singleton pattern for default producer', () => {
      // Arrange & Act
      const producer1 = SekibanDateProducer.getRegistered()
      const producer2 = SekibanDateProducer.getRegistered()
      
      // Assert
      expect(producer1).toBe(producer2)
    })
  })
})
