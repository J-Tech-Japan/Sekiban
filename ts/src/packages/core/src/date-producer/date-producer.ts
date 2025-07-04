import type { ISekibanDateProducer } from './types'

/**
 * Default implementation of ISekibanDateProducer.
 * Provides current date/time values and supports registration of custom producers.
 */
export class SekibanDateProducer implements ISekibanDateProducer {
  private static registered: ISekibanDateProducer = new SekibanDateProducer()
  
  now(): Date {
    return new Date()
  }
  
  utcNow(): Date {
    return new Date()
  }
  
  today(): Date {
    const now = new Date()
    return new Date(now.getFullYear(), now.getMonth(), now.getDate())
  }
  
  /**
   * Get the currently registered date producer
   */
  static getRegistered(): ISekibanDateProducer {
    return this.registered
  }
  
  /**
   * Register a custom date producer (useful for testing)
   */
  static register(producer: ISekibanDateProducer): void {
    this.registered = producer
  }
}
