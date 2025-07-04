import { z } from 'zod';

/**
 * Event schema definition
 */
export interface EventSchema {
  eventType: string;
  version: number;
  schema: z.ZodSchema<any>;
  description?: string;
  deprecated?: boolean;
  deprecationReason?: string;
  createdAt?: Date;
  createdBy?: string;
}

/**
 * Schema version information
 */
export interface SchemaVersion {
  version: number;
  createdAt: Date;
  createdBy?: string;
  description?: string;
}

/**
 * Compatibility modes for schema evolution
 */
export enum CompatibilityMode {
  /** New schema can read data written by old schema */
  BACKWARD = 'BACKWARD',
  /** Old schema can read data written by new schema */
  FORWARD = 'FORWARD',
  /** Both backward and forward compatible */
  FULL = 'FULL',
  /** No compatibility checking */
  NONE = 'NONE',
}

/**
 * Schema compatibility result
 */
export interface CompatibilityResult {
  isCompatible: boolean;
  errors: string[];
}

/**
 * Schema validation result
 */
export interface ValidationResult<T = any> {
  isValid: boolean;
  data?: T;
  errors?: z.ZodError;
}

/**
 * Migration guide between schema versions
 */
export interface MigrationGuide {
  fromVersion: number;
  toVersion: number;
  addedFields: string[];
  removedFields: string[];
  modifiedFields: string[];
  unchangedFields: string[];
}

/**
 * Schema validation error
 */
export class SchemaValidationError extends Error {
  constructor(message: string, public errors?: z.ZodError) {
    super(message);
    this.name = 'SchemaValidationError';
  }
}

/**
 * Registry for managing event schemas
 */
export class SchemaRegistry {
  private schemas = new Map<string, EventSchema>();
  
  /**
   * Register a new event schema
   */
  register(schema: EventSchema): void {
    const key = this.getKey(schema.eventType, schema.version);
    
    if (this.schemas.has(key)) {
      throw new Error(`Schema for ${schema.eventType} v${schema.version} already exists`);
    }
    
    // Add metadata if not provided
    if (!schema.createdAt) {
      schema.createdAt = new Date();
    }
    
    this.schemas.set(key, schema);
  }
  
  /**
   * Get a specific schema version
   */
  getSchema(eventType: string, version: number): EventSchema | undefined {
    const key = this.getKey(eventType, version);
    return this.schemas.get(key);
  }
  
  /**
   * Get the latest version number for an event type
   */
  getLatestVersion(eventType: string): number {
    const versions = this.getVersions(eventType);
    return versions.length > 0 ? Math.max(...versions) : 0;
  }
  
  /**
   * Get all version numbers for an event type
   */
  getVersions(eventType: string): number[] {
    const versions: number[] = [];
    
    for (const [key, schema] of this.schemas) {
      if (schema.eventType === eventType) {
        versions.push(schema.version);
      }
    }
    
    return versions.sort((a, b) => a - b);
  }
  
  /**
   * Validate an event payload against its schema
   */
  validatePayload<T = any>(
    eventType: string,
    version: number,
    payload: any
  ): ValidationResult<T> {
    const schema = this.getSchema(eventType, version);
    
    if (!schema) {
      throw new Error(`Schema not found for ${eventType} v${version}`);
    }
    
    try {
      const data = schema.schema.parse(payload);
      return { isValid: true, data };
    } catch (error) {
      if (error instanceof z.ZodError) {
        return { isValid: false, errors: error };
      }
      throw error;
    }
  }
  
  /**
   * Check compatibility between schemas
   */
  checkCompatibility(
    newSchema: EventSchema,
    mode: CompatibilityMode
  ): CompatibilityResult {
    if (mode === CompatibilityMode.NONE) {
      return { isCompatible: true, errors: [] };
    }
    
    const currentVersion = newSchema.version - 1;
    const currentSchema = this.getSchema(newSchema.eventType, currentVersion);
    
    if (!currentSchema) {
      // No previous version, always compatible
      return { isCompatible: true, errors: [] };
    }
    
    const errors: string[] = [];
    
    // Extract field information from Zod schemas
    const currentFields = this.extractFields(currentSchema.schema);
    const newFields = this.extractFields(newSchema.schema);
    
    switch (mode) {
      case CompatibilityMode.BACKWARD:
        // New schema must be able to read old data
        // Check for removed required fields
        for (const field of currentFields.required) {
          if (!newFields.all.has(field)) {
            errors.push(`Field "${field}" was removed`);
          }
        }
        break;
        
      case CompatibilityMode.FORWARD:
        // Old schema must be able to read new data
        // Check for added required fields
        for (const field of newFields.required) {
          if (!currentFields.all.has(field)) {
            errors.push(`Required field "${field}" was added`);
          }
        }
        break;
        
      case CompatibilityMode.FULL:
        // Both backward and forward compatible
        // No removed fields
        for (const field of currentFields.all) {
          if (!newFields.all.has(field)) {
            errors.push(`Field "${field}" was removed`);
          }
        }
        // No added required fields
        for (const field of newFields.required) {
          if (!currentFields.all.has(field)) {
            errors.push(`Required field "${field}" was added`);
          }
        }
        break;
    }
    
    return {
      isCompatible: errors.length === 0,
      errors
    };
  }
  
  /**
   * Generate a migration guide between versions
   */
  getMigrationGuide(
    eventType: string,
    fromVersion: number,
    toVersion: number
  ): MigrationGuide {
    const fromSchema = this.getSchema(eventType, fromVersion);
    const toSchema = this.getSchema(eventType, toVersion);
    
    if (!fromSchema || !toSchema) {
      throw new Error(`Schema not found for ${eventType} v${fromVersion} or v${toVersion}`);
    }
    
    const fromFields = this.extractFields(fromSchema.schema);
    const toFields = this.extractFields(toSchema.schema);
    
    const addedFields = Array.from(toFields.all).filter(f => !fromFields.all.has(f));
    const removedFields = Array.from(fromFields.all).filter(f => !toFields.all.has(f));
    const unchangedFields = Array.from(fromFields.all).filter(f => toFields.all.has(f));
    
    // For modified fields, we'd need deeper schema analysis
    // For now, we'll leave this empty
    const modifiedFields: string[] = [];
    
    return {
      fromVersion,
      toVersion,
      addedFields,
      removedFields,
      modifiedFields,
      unchangedFields
    };
  }
  
  /**
   * Mark a schema as deprecated
   */
  deprecateSchema(
    eventType: string,
    version: number,
    reason: string
  ): void {
    const schema = this.getSchema(eventType, version);
    
    if (!schema) {
      throw new Error(`Schema not found for ${eventType} v${version}`);
    }
    
    schema.deprecated = true;
    schema.deprecationReason = reason;
  }
  
  /**
   * Export all schemas
   */
  exportSchemas(): EventSchema[] {
    return Array.from(this.schemas.values());
  }
  
  /**
   * Import schemas
   */
  importSchemas(schemas: EventSchema[]): void {
    for (const schema of schemas) {
      this.register(schema);
    }
  }
  
  /**
   * Get schema key
   */
  private getKey(eventType: string, version: number): string {
    return `${eventType}:${version}`;
  }
  
  /**
   * Extract field information from Zod schema
   */
  private extractFields(schema: z.ZodSchema<any>): {
    all: Set<string>;
    required: Set<string>;
    optional: Set<string>;
  } {
    const all = new Set<string>();
    const required = new Set<string>();
    const optional = new Set<string>();
    
    // Handle ZodObject
    if (schema instanceof z.ZodObject) {
      const shape = schema.shape;
      
      for (const [key, value] of Object.entries(shape)) {
        all.add(key);
        
        if (value instanceof z.ZodOptional || value instanceof z.ZodNullable) {
          optional.add(key);
        } else {
          required.add(key);
        }
      }
    }
    
    return { all, required, optional };
  }
}