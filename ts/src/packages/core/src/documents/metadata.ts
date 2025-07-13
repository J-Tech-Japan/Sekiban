/**
 * Metadata for commands and events
 */
export interface Metadata {
  /**
   * The user who initiated the action
   */
  userId?: string;
  
  /**
   * The correlation ID for tracking related operations
   */
  correlationId?: string;
  
  /**
   * The causation ID linking cause and effect
   */
  causationId?: string;
  
  /**
   * The timestamp when the action occurred
   */
  timestamp: Date;
  
  /**
   * The user who executed the action (for C# compatibility)
   */
  executedUser?: string;
  
  /**
   * Additional custom metadata
   */
  custom?: Record<string, unknown>;
}

/**
 * Builder for creating metadata
 */
export class MetadataBuilder {
  private metadata: Metadata;

  constructor() {
    this.metadata = {
      timestamp: new Date(),
    };
  }

  /**
   * Sets the user ID
   */
  withUserId(userId: string): MetadataBuilder {
    this.metadata.userId = userId;
    return this;
  }

  /**
   * Sets the correlation ID
   */
  withCorrelationId(correlationId: string): MetadataBuilder {
    this.metadata.correlationId = correlationId;
    return this;
  }

  /**
   * Sets the causation ID
   */
  withCausationId(causationId: string): MetadataBuilder {
    this.metadata.causationId = causationId;
    return this;
  }

  /**
   * Sets the timestamp
   */
  withTimestamp(timestamp: Date): MetadataBuilder {
    this.metadata.timestamp = timestamp;
    return this;
  }

  /**
   * Sets the executed user (for C# compatibility)
   */
  withExecutedUser(executedUser: string): MetadataBuilder {
    this.metadata.executedUser = executedUser;
    return this;
  }

  /**
   * Adds custom metadata
   */
  withCustom(key: string, value: unknown): MetadataBuilder {
    if (!this.metadata.custom) {
      this.metadata.custom = {};
    }
    this.metadata.custom[key] = value;
    return this;
  }

  /**
   * Adds multiple custom metadata entries
   */
  withCustomData(data: Record<string, unknown>): MetadataBuilder {
    if (!this.metadata.custom) {
      this.metadata.custom = {};
    }
    Object.assign(this.metadata.custom, data);
    return this;
  }

  /**
   * Builds the metadata
   */
  build(): Metadata {
    return { ...this.metadata };
  }
}

/**
 * Utility functions for working with metadata
 */
export const Metadata = {
  /**
   * Creates empty metadata with current timestamp
   */
  create(): Metadata {
    return new MetadataBuilder().build();
  },

  /**
   * Creates metadata with a user ID
   */
  withUser(userId: string): Metadata {
    return new MetadataBuilder().withUserId(userId).build();
  },

  /**
   * Creates metadata for a correlated operation
   */
  correlated(correlationId: string, causationId?: string): Metadata {
    let builder = new MetadataBuilder().withCorrelationId(correlationId);
    if (causationId) {
      builder = builder.withCausationId(causationId);
    }
    return builder.build();
  },

  /**
   * Merges two metadata objects
   */
  merge(base: Metadata, override: Partial<Metadata>): Metadata {
    const result: Metadata = {
      ...base,
      ...override,
      timestamp: override.timestamp ?? base.timestamp,
    };

    if (base.custom || override.custom) {
      result.custom = {
        ...base.custom,
        ...override.custom,
      };
    }

    return result;
  },

  /**
   * Creates a builder from existing metadata
   */
  builder(base?: Metadata): MetadataBuilder {
    const builder = new MetadataBuilder();
    if (base) {
      if (base.userId) builder.withUserId(base.userId);
      if (base.correlationId) builder.withCorrelationId(base.correlationId);
      if (base.causationId) builder.withCausationId(base.causationId);
      if (base.timestamp) builder.withTimestamp(base.timestamp);
      if (base.executedUser) builder.withExecutedUser(base.executedUser);
      if (base.custom) builder.withCustomData(base.custom);
    }
    return builder;
  },
};