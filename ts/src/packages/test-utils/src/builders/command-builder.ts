import { ICommand, PartitionKeys, Metadata } from '../../../core/src';

/**
 * Command with metadata for testing
 */
export interface TestCommand<TPayload extends ICommand> {
  type: string;
  payload: TPayload;
  partitionKeys?: PartitionKeys;
  metadata?: Metadata;
}

/**
 * Fluent builder for creating test commands
 */
export class CommandBuilder<TPayload extends ICommand> {
  private type: string;
  private payload?: TPayload;
  private partitionKeys?: PartitionKeys;
  private metadata?: Metadata;

  constructor(type: string) {
    this.type = type;
  }

  /**
   * Create a builder from an existing command
   */
  static from<T extends ICommand>(command: TestCommand<T>): CommandBuilder<T> {
    const builder = new CommandBuilder<T>(command.type);
    builder.payload = command.payload;
    builder.partitionKeys = command.partitionKeys;
    builder.metadata = command.metadata ? { ...command.metadata } : undefined;
    return builder;
  }

  /**
   * Set the command payload
   */
  withPayload(payload: TPayload): CommandBuilder<TPayload> {
    const newBuilder = this.clone();
    newBuilder.payload = payload;
    return newBuilder;
  }

  /**
   * Update the payload partially
   */
  updatePayload(partial: Partial<TPayload>): CommandBuilder<TPayload> {
    const newBuilder = this.clone();
    newBuilder.payload = { ...this.payload, ...partial } as TPayload;
    return newBuilder;
  }

  /**
   * Set the partition keys
   */
  withPartitionKeys(partitionKeys: PartitionKeys): CommandBuilder<TPayload> {
    const newBuilder = this.clone();
    newBuilder.partitionKeys = partitionKeys;
    return newBuilder;
  }

  /**
   * Set metadata
   */
  withMetadata(metadata: Metadata): CommandBuilder<TPayload> {
    const newBuilder = this.clone();
    newBuilder.metadata = metadata;
    return newBuilder;
  }

  /**
   * Build a single command
   */
  build(): TestCommand<TPayload> {
    if (!this.payload) {
      throw new Error('Payload is required');
    }

    const command: TestCommand<TPayload> = {
      type: this.type,
      payload: this.payload,
    };

    if (this.partitionKeys) {
      command.partitionKeys = this.partitionKeys;
    }

    if (this.metadata) {
      command.metadata = this.metadata;
    }

    return command;
  }

  /**
   * Build multiple commands with different payloads
   */
  buildMany(payloads: TPayload[]): TestCommand<TPayload>[] {
    return payloads.map(payload => {
      return this
        .withPayload(payload)
        .build();
    });
  }

  /**
   * Clone the builder
   */
  private clone(): CommandBuilder<TPayload> {
    const newBuilder = new CommandBuilder<TPayload>(this.type);
    newBuilder.payload = this.payload;
    newBuilder.partitionKeys = this.partitionKeys;
    newBuilder.metadata = this.metadata ? { ...this.metadata } : undefined;
    return newBuilder;
  }
}