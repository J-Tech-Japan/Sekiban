/**
 * Gets the current UTC timestamp
 */
export function getCurrentUtcTimestamp(): Date {
  return new Date();
}

/**
 * Formats a date to ISO 8601 string
 */
export function toIsoString(date: Date): string {
  return date.toISOString();
}

/**
 * Parses an ISO 8601 string to Date
 */
export function fromIsoString(isoString: string): Date {
  return new Date(isoString);
}

/**
 * Adds seconds to a date
 */
export function addSeconds(date: Date, seconds: number): Date {
  const result = new Date(date);
  result.setSeconds(result.getSeconds() + seconds);
  return result;
}

/**
 * Adds milliseconds to a date
 */
export function addMilliseconds(date: Date, milliseconds: number): Date {
  return new Date(date.getTime() + milliseconds);
}

/**
 * Compares two dates
 */
export function compareDates(date1: Date, date2: Date): number {
  return date1.getTime() - date2.getTime();
}

/**
 * Checks if a date is before another date
 */
export function isBefore(date1: Date, date2: Date): boolean {
  return compareDates(date1, date2) < 0;
}

/**
 * Checks if a date is after another date
 */
export function isAfter(date1: Date, date2: Date): boolean {
  return compareDates(date1, date2) > 0;
}

/**
 * Gets Unix timestamp in seconds
 */
export function getUnixTimestamp(date: Date = new Date()): number {
  return Math.floor(date.getTime() / 1000);
}

/**
 * Creates a Date from Unix timestamp
 */
export function fromUnixTimestamp(timestamp: number): Date {
  return new Date(timestamp * 1000);
}