import { v4 as uuidv4, v5 as uuidv5 } from 'uuid'
import * as crypto from 'crypto'

/**
 * Generate a new UUID v4
 */
export function generateUuid(): string {
  return uuidv4()
}

/**
 * Generate a new UUID v7 (time-ordered UUID)
 * Based on https://www.ietf.org/archive/id/draft-peabody-dispatch-new-uuid-format-04.html
 */
export function createVersion7(): string {
  // Get current timestamp in milliseconds
  const timestamp = Date.now()
  
  // Convert timestamp to 48-bit hex (12 hex chars)
  const timestampHex = timestamp.toString(16).padStart(12, '0')
  
  // Generate random bytes for the rest
  const randomBytes = crypto.randomBytes(10)
  
  // Set version (0111 = 7) in bits 48-51
  randomBytes[0] = (randomBytes[0]! & 0x0f) | 0x70
  
  // Set variant (10) in bits 64-65
  randomBytes[2] = (randomBytes[2]! & 0x3f) | 0x80
  
  // Format as UUID
  const uuid = [
    timestampHex.substring(0, 8),                    // time_low
    timestampHex.substring(8, 12),                   // time_mid
    randomBytes.subarray(0, 2).toString('hex'),     // time_hi_and_version
    randomBytes.subarray(2, 4).toString('hex'),     // clock_seq_hi_and_reserved + clock_seq_low
    randomBytes.subarray(4, 10).toString('hex')     // node
  ].join('-')
  
  return uuid
}

/**
 * Validate if a string is a valid UUID
 */
export function isValidUuid(value: string): boolean {
  if (typeof value !== 'string') {
    return false
  }
  
  // Updated regex to support all UUID versions (1-8)
  const uuidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
  return uuidRegex.test(value)
}

/**
 * Create a deterministic UUID based on namespace and value
 * Uses UUID v5 (SHA-1 based) for proper deterministic generation
 */
export function createNamespacedUuid(namespace: string, value: string): string {
  // Use a fixed namespace UUID for the application
  // This could be customized per use case
  const APP_NAMESPACE = '6ba7b810-9dad-11d1-80b4-00c04fd430c8' // UUID v1 namespace
  
  // Create a combined namespace from the provided namespace string
  const namespaceId = uuidv5(namespace, APP_NAMESPACE)
  
  // Generate the final UUID using the namespace ID and value
  return uuidv5(value, namespaceId)
}

/**
 * Creates a deterministic UUID from a namespace and name (deprecated - use createNamespacedUuid)
 */
export function createDeterministicUuid(namespace: string, name: string): string {
  return createNamespacedUuid(namespace, name)
}