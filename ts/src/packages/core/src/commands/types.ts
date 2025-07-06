/**
 * This file is kept minimal for basic types that might be used across the system.
 * For command definitions, use schema-registry/command-schema.ts
 */

import { Metadata } from '../documents/index.js';

/**
 * Command execution options
 */
export interface CommandExecutionOptions {
  /**
   * Whether to skip validation
   */
  skipValidation?: boolean;
  
  /**
   * Custom metadata to merge with command metadata
   */
  metadata?: Partial<Metadata>;
}