import { generateUuid, getUnixTimestamp, createVersion7 } from '../utils/index';
import { Result, ok, err } from 'neverthrow';
import { ValidationError } from '../result/errors';

// Constants from lib.ts
const SafeMilliseconds = 5000;
const TickNumberOfLength = 19;
const IdNumberOfLength = 11;
const IdModBase = Math.pow(10, IdNumberOfLength);
const TicksPerSecond = 10_000_000;
const TicksFromUnixToCSharp = 621_355_968_000_000_000;

/**
 * Represents a sortable unique identifier for events
 * Format: 30 digits (19 for ticks + 11 for ID hash)
 */
export class SortableUniqueId {
  private constructor(readonly value: string) {}

  /**
   * Creates a new sortable unique ID (alias for generate)
   */
  static create(): SortableUniqueId {
    return SortableUniqueId.generate();
  }

  /**
   * Generates a new sortable unique ID
   */
  static generate(): SortableUniqueId {
    const timestamp = new Date();
    const tickString = SortableUniqueId.getTickString(timestamp);
    const idString = SortableUniqueId.getIdString(generateUuid());
    const value = tickString + idString;
    return new SortableUniqueId(value);
  }

  /**
   * Creates a SortableUniqueId from a string value
   */
  static fromString(value: string): Result<SortableUniqueId, ValidationError> {
    if (!value || value.length !== 30) {
      return err(new ValidationError('Invalid SortableUniqueId format: must be exactly 30 digits'));
    }
    
    // Basic validation - should be numeric characters only
    if (!/^\d{30}$/.test(value)) {
      return err(new ValidationError('Invalid SortableUniqueId format: must contain only digits'));
    }
    
    return ok(new SortableUniqueId(value));
  }

  /**
   * Converts to string
   */
  toString(): string {
    return this.value;
  }

  /**
   * Gets the timestamp from the ID
   */
  getTicks(): Date {
    const ticksString = this.value.slice(0, TickNumberOfLength);
    const csharpTicks = BigInt(ticksString);
    // Convert C# ticks to JavaScript timestamp
    // First subtract the C# epoch offset, then convert from 100-nanosecond intervals to milliseconds
    const jsTicks = Number(csharpTicks - BigInt(TicksFromUnixToCSharp)) / 10000;
    return new Date(jsTicks);
  }

  /**
   * Compares two SortableUniqueIds
   */
  static compare(a: SortableUniqueId, b: SortableUniqueId): number {
    if (a.value < b.value) return -1;
    if (a.value > b.value) return 1;
    return 0;
  }

  /**
   * Comparison methods
   */
  isEarlierThan(other: SortableUniqueId): boolean {
    return this.value < other.value;
  }

  isEarlierThanOrEqual(other: SortableUniqueId): boolean {
    return this.value <= other.value;
  }

  isLaterThan(other: SortableUniqueId): boolean {
    return this.value > other.value;
  }

  isLaterThanOrEqual(other: SortableUniqueId): boolean {
    return this.value >= other.value;
  }

  // Helper methods
  private static getTickString(timestamp: Date): string {
    const ticks = SortableUniqueId.systemTimeToCSharpTicks(timestamp);
    return SortableUniqueId.formatTick(BigInt(ticks));
  }

  private static systemTimeToCSharpTicks(timestamp: Date): number {
    const durationSinceUnix = timestamp.getTime() - Date.UTC(1970, 0, 1);
    const ticksSinceUnix = Math.floor(durationSinceUnix * 10000);
    return ticksSinceUnix + TicksFromUnixToCSharp;
  }

  private static formatTick(ticks: bigint): string {
    return ticks.toString().padStart(TickNumberOfLength, '0');
  }

  private static getIdString(id: string): string {
    const hash = SortableUniqueId.generateIdHash(id);
    return SortableUniqueId.formatId(hash);
  }

  private static generateIdHash(id: string): number {
    let hash = 0;
    for (let i = 0; i < id.length; i++) {
      hash = (31 * hash + id.charCodeAt(i)) & 0xffffffff;
    }
    return Math.abs(hash);
  }

  private static formatId(hash: number): string {
    return (hash % IdModBase).toString().padStart(IdNumberOfLength, '0');
  }

  /**
   * Creates a safe ID with a timestamp adjusted by SafeMilliseconds
   */
  static getSafeIdFromUtc(): string {
    const safeTimestamp = new Date(Date.now() - SafeMilliseconds);
    const tickString = SortableUniqueId.getTickString(safeTimestamp);
    const idString = SortableUniqueId.getIdString('00000000-0000-0000-0000-000000000000');
    return tickString + idString;
  }
}

/**
 * Legacy interface for backward compatibility
 */
export interface ISortableUniqueId {
  timestamp: number;
  uniqueId: string;
}

/**
 * Legacy utility functions
 */
export const SortableUniqueIdUtils = {
  create(timestamp?: Date): ISortableUniqueId {
    return {
      timestamp: timestamp ? getUnixTimestamp(timestamp) : getUnixTimestamp(),
      uniqueId: generateUuid(),
    };
  },

  toString(id: ISortableUniqueId): string {
    return `${id.timestamp.toString().padStart(10, '0')}-${id.uniqueId}`;
  },

  fromString(value: string): ISortableUniqueId | null {
    const match = value.match(/^(\d{10})-(.+)$/);
    if (!match) {
      return null;
    }
    
    return {
      timestamp: parseInt(match[1]!, 10),
      uniqueId: match[2]!,
    };
  },

  compare(a: ISortableUniqueId, b: ISortableUniqueId): number {
    if (a.timestamp !== b.timestamp) {
      return a.timestamp - b.timestamp;
    }
    return a.uniqueId.localeCompare(b.uniqueId);
  },

  isBefore(a: ISortableUniqueId, b: ISortableUniqueId): boolean {
    return SortableUniqueIdUtils.compare(a, b) < 0;
  },

  isAfter(a: ISortableUniqueId, b: ISortableUniqueId): boolean {
    return SortableUniqueIdUtils.compare(a, b) > 0;
  },

  minForTimestamp(timestamp: Date): ISortableUniqueId {
    return {
      timestamp: getUnixTimestamp(timestamp),
      uniqueId: '00000000-0000-0000-0000-000000000000',
    };
  },

  maxForTimestamp(timestamp: Date): ISortableUniqueId {
    return {
      timestamp: getUnixTimestamp(timestamp),
      uniqueId: 'ffffffff-ffff-ffff-ffff-ffffffffffff',
    };
  },
};