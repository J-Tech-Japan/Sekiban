/**
 * Interface for providing date/time values in Sekiban.
 * This allows for mocking and testing with fixed dates.
 */
export interface ISekibanDateProducer {
  /**
   * Get current local date and time
   */
  now(): Date
  
  /**
   * Get current UTC date and time
   */
  utcNow(): Date
  
  /**
   * Get today's date at midnight (00:00:00.000) in local time
   */
  today(): Date
}
