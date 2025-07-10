import { z } from 'zod';
import { Result, ok, err } from 'neverthrow';
import { SchemaRegistry, CompatibilityMode, EventSchema } from '../schema/schema-registry';
import { UpcasterRegistry } from '../upcaster';

/**
 * Versioned event with type and version information
 */
export interface VersionedEvent {
  eventType: string;
  version: number;
  payload: any;
}

/**
 * Event versioning strategies
 */
export enum VersioningStrategy {
  /** Keep events in their original version */
  KEEP_ORIGINAL = 'KEEP_ORIGINAL',
  /** Always upcast to the latest version */
  UPCAST_TO_LATEST = 'UPCAST_TO_LATEST',
  /** Upcast to a specific target version */
  UPCAST_TO_VERSION = 'UPCAST_TO_VERSION',
}

/**
 * Configuration for event versioning system
 */
export interface EventVersioningConfig {
  /** Default compatibility mode for schema registration */
  defaultCompatibilityMode: CompatibilityMode;
  /** Whether to validate payloads on write */
  validateOnWrite: boolean;
  /** Whether to validate payloads on read */
  validateOnRead: boolean;
  /** Versioning strategy to use */
  strategy: VersioningStrategy;
  /** Target version for UPCAST_TO_VERSION strategy */
  targetVersion?: number;
}

/**
 * Error that occurs during event versioning
 */
export class EventVersioningError extends Error {
  constructor(
    message: string,
    public readonly eventType: string,
    public readonly version: number,
    public readonly cause?: Error
  ) {
    super(message);
    this.name = 'EventVersioningError';
  }
}

/**
 * System for managing event versioning and schema evolution
 */
export class EventVersioningSystem {
  constructor(
    private readonly schemaRegistry: SchemaRegistry,
    private readonly upcasterRegistry: UpcasterRegistry,
    private readonly config: EventVersioningConfig
  ) {}

  /**
   * Process an event through the versioning system
   */
  async processEvent(event: VersionedEvent): Promise<Result<VersionedEvent, EventVersioningError>> {
    try {
      // Validate if configured
      if (this.config.validateOnRead) {
        const validation = this.schemaRegistry.validatePayload(
          event.eventType,
          event.version,
          event.payload
        );
        
        if (!validation.isValid) {
          return err(new EventVersioningError(
            `Event validation failed: ${validation.errors?.format()._errors.join(', ')}`,
            event.eventType,
            event.version
          ));
        }
      }

      // Apply versioning strategy
      let processedEvent = event;
      
      switch (this.config.strategy) {
        case VersioningStrategy.KEEP_ORIGINAL:
          // No transformation needed
          break;
          
        case VersioningStrategy.UPCAST_TO_LATEST:
          const latestVersion = this.schemaRegistry.getLatestVersion(event.eventType);
          if (event.version < latestVersion) {
            const upcastResult = await this.upcastEvent(event, latestVersion);
            if (upcastResult.isErr()) {
              return err(upcastResult.error);
            }
            processedEvent = upcastResult.value;
          }
          break;
          
        case VersioningStrategy.UPCAST_TO_VERSION:
          if (!this.config.targetVersion) {
            return err(new EventVersioningError(
              'Target version not specified for UPCAST_TO_VERSION strategy',
              event.eventType,
              event.version
            ));
          }
          if (event.version < this.config.targetVersion) {
            const upcastResult = await this.upcastEvent(event, this.config.targetVersion);
            if (upcastResult.isErr()) {
              return err(upcastResult.error);
            }
            processedEvent = upcastResult.value;
          }
          break;
      }

      return ok(processedEvent);
    } catch (error) {
      return err(new EventVersioningError(
        `Failed to process event: ${error instanceof Error ? error.message : 'Unknown error'}`,
        event.eventType,
        event.version,
        error instanceof Error ? error : undefined
      ));
    }
  }

  /**
   * Process multiple events in batch
   */
  async processEvents(events: VersionedEvent[]): Promise<Result<VersionedEvent, EventVersioningError>[]> {
    return Promise.all(events.map(event => this.processEvent(event)));
  }

  /**
   * Register a new schema with compatibility checking
   */
  async registerSchema(schema: Omit<EventSchema, 'createdAt'>): Promise<Result<void, EventVersioningError>> {
    try {
      // Check compatibility
      const compatibility = this.schemaRegistry.checkCompatibility(
        schema as EventSchema,
        this.config.defaultCompatibilityMode
      );
      
      if (!compatibility.isCompatible) {
        return err(new EventVersioningError(
          `Schema registration failed - compatibility errors: ${compatibility.errors.join(', ')}`,
          schema.eventType,
          schema.version
        ));
      }

      // Register the schema
      this.schemaRegistry.register(schema as EventSchema);
      return ok(undefined);
    } catch (error) {
      return err(new EventVersioningError(
        `Failed to register schema: ${error instanceof Error ? error.message : 'Unknown error'}`,
        schema.eventType,
        schema.version,
        error instanceof Error ? error : undefined
      ));
    }
  }

  /**
   * Upcast an event to a target version
   */
  private async upcastEvent(
    event: VersionedEvent,
    targetVersion: number
  ): Promise<Result<VersionedEvent, EventVersioningError>> {
    let currentVersion = event.version;
    let currentPayload = event.payload;

    // Apply upcasters sequentially
    while (currentVersion < targetVersion) {
      const upcaster = this.upcasterRegistry.getUpcaster(
        event.eventType,
        currentVersion,
        currentVersion + 1
      );

      if (!upcaster) {
        // No direct path, stop here
        break;
      }

      try {
        currentPayload = await upcaster.upcast(currentPayload);
        currentVersion++;
      } catch (error) {
        return err(new EventVersioningError(
          `Failed to upcast from v${currentVersion} to v${currentVersion + 1}: ${error instanceof Error ? error.message : 'Unknown error'}`,
          event.eventType,
          currentVersion,
          error instanceof Error ? error : undefined
        ));
      }
    }

    // Validate the final payload if configured
    if (this.config.validateOnRead && currentVersion !== event.version) {
      const validation = this.schemaRegistry.validatePayload(
        event.eventType,
        currentVersion,
        currentPayload
      );
      
      if (!validation.isValid) {
        return err(new EventVersioningError(
          `Upcasted event validation failed: ${validation.errors?.format()._errors.join(', ')}`,
          event.eventType,
          currentVersion
        ));
      }
    }

    return ok({
      eventType: event.eventType,
      version: currentVersion,
      payload: currentPayload,
    });
  }

  /**
   * Generate migration code template for schema changes
   */
  generateMigrationCode(eventType: string, fromVersion: number, toVersion: number): string {
    const guide = this.schemaRegistry.getMigrationGuide(eventType, fromVersion, toVersion);
    
    return `// Migration from ${eventType} v${fromVersion} to v${toVersion}
// Added fields: ${guide.addedFields.join(', ') || 'none'}
// Removed fields: ${guide.removedFields.join(', ') || 'none'}
// Modified fields: ${guide.modifiedFields.join(', ') || 'none'}

export const ${eventType}V${fromVersion}ToV${toVersion}Upcaster = {
  eventType: '${eventType}',
  fromVersion: ${fromVersion},
  toVersion: ${toVersion},
  upcast: (payload: any) => {
    // TODO: Implement transformation logic
    return {
      ...payload,
      // Add new fields with defaults
${guide.addedFields.map(field => `      ${field}: undefined, // TODO: Set appropriate default`).join('\n')}
    };
  },
};`;
  }

  /**
   * Check if a migration path exists between versions
   */
  canMigrate(eventType: string, fromVersion: number, toVersion: number): boolean {
    if (fromVersion === toVersion) {
      return true;
    }
    
    if (fromVersion > toVersion) {
      // We don't support downcasting
      return false;
    }

    // Check if we can upcast step by step
    let currentVersion = fromVersion;
    while (currentVersion < toVersion) {
      const upcaster = this.upcasterRegistry.getUpcaster(
        eventType,
        currentVersion,
        currentVersion + 1
      );
      
      if (!upcaster) {
        return false;
      }
      
      currentVersion++;
    }
    
    return true;
  }

  /**
   * Get schema changelog for an event type
   */
  getSchemaChangelog(eventType: string): Array<{
    version: number;
    description?: string;
    createdAt?: Date;
    deprecated?: boolean;
    deprecationReason?: string;
  }> {
    const versions = this.schemaRegistry.getVersions(eventType);
    
    return versions.map(version => {
      const schema = this.schemaRegistry.getSchema(eventType, version);
      if (!schema) {
        throw new Error(`Schema not found for ${eventType} v${version}`);
      }
      
      return {
        version: schema.version,
        description: schema.description,
        createdAt: schema.createdAt,
        deprecated: schema.deprecated,
        deprecationReason: schema.deprecationReason,
      };
    });
  }
}