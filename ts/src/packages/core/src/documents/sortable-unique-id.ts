import { generateUuid, getUnixTimestamp, createVersion7 } from '../utils';
import { Result, ok, err } from 'neverthrow';
import { ValidationError } from '../result';

/**
 * Represents a sortable unique identifier for events
 */
export class SortableUniqueId {
  private static counter = 0;
  private constructor(readonly value: string) {}

  /**
   * Generates a new sortable unique ID
   */
  static generate(): SortableUniqueId {
    const timestamp = Date.now();
    const timestampHex = timestamp.toString(16).padStart(12, '0');
    
    // Add counter to ensure uniqueness even in rapid generation
    this.counter = (this.counter + 1) % 0xFFFF;
    const counterHex = this.counter.toString(16).padStart(4, '0');
    
    const uuid = createVersion7();
    // Extract the random part from UUID v7 (everything after the timestamp and counter)
    const randomPart = uuid.substring(18);
    const value = `${timestampHex}${counterHex}${randomPart.replace(/-/g, '')}`;
    return new SortableUniqueId(value);
  }

  /**
   * Creates a SortableUniqueId from a string value
   */
  static fromString(value: string): Result<SortableUniqueId, ValidationError> {
    if (!value || value.length < 32) {
      return err(new ValidationError('Invalid SortableUniqueId format'));
    }
    
    // Basic validation - should be hex characters
    if (!/^[0-9a-f]+$/i.test(value)) {
      return err(new ValidationError('Invalid SortableUniqueId format'));
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
   * Compares two SortableUniqueIds
   */
  static compare(a: SortableUniqueId, b: SortableUniqueId): number {
    if (a.value < b.value) return -1;
    if (a.value > b.value) return 1;
    return 0;
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
      timestamp: parseInt(match[1], 10),
      uniqueId: match[2],
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