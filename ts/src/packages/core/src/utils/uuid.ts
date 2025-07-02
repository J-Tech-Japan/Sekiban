import { v4 as uuidv4 } from 'uuid';

/**
 * Generates a new UUID v4
 */
export function generateUuid(): string {
  return uuidv4();
}

/**
 * Validates if a string is a valid UUID
 */
export function isValidUuid(value: string): boolean {
  const uuidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
  return uuidRegex.test(value);
}

/**
 * Creates a deterministic UUID from a namespace and name
 */
export function createDeterministicUuid(namespace: string, name: string): string {
  // Simple deterministic UUID generation using hash
  const hash = `${namespace}:${name}`;
  let result = '';
  
  for (let i = 0; i < hash.length && result.length < 32; i++) {
    result += hash.charCodeAt(i).toString(16).padStart(2, '0');
  }
  
  // Pad with zeros if needed
  result = result.padEnd(32, '0');
  
  // Format as UUID
  return [
    result.slice(0, 8),
    result.slice(8, 12),
    '4' + result.slice(13, 16), // Version 4
    ((parseInt(result.slice(16, 17), 16) & 0x3) | 0x8).toString(16) + result.slice(17, 20), // Variant
    result.slice(20, 32)
  ].join('-');
}