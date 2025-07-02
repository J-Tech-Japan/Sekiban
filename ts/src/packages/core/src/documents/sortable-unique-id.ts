import { generateUuid, getUnixTimestamp } from '../utils';

/**
 * Represents a sortable unique identifier for events
 */
export interface SortableUniqueId {
  /**
   * The timestamp component (Unix timestamp in seconds)
   */
  timestamp: number;
  
  /**
   * The unique identifier component
   */
  uniqueId: string;
}

/**
 * Utility functions for working with sortable unique IDs
 */
export const SortableUniqueId = {
  /**
   * Creates a new sortable unique ID
   */
  create(timestamp?: Date): SortableUniqueId {
    return {
      timestamp: timestamp ? getUnixTimestamp(timestamp) : getUnixTimestamp(),
      uniqueId: generateUuid(),
    };
  },

  /**
   * Converts a sortable unique ID to a string
   */
  toString(id: SortableUniqueId): string {
    return `${id.timestamp.toString().padStart(10, '0')}-${id.uniqueId}`;
  },

  /**
   * Parses a sortable unique ID from a string
   */
  fromString(value: string): SortableUniqueId | null {
    const match = value.match(/^(\d{10})-(.+)$/);
    if (!match) {
      return null;
    }
    
    return {
      timestamp: parseInt(match[1], 10),
      uniqueId: match[2],
    };
  },

  /**
   * Compares two sortable unique IDs
   */
  compare(a: SortableUniqueId, b: SortableUniqueId): number {
    if (a.timestamp !== b.timestamp) {
      return a.timestamp - b.timestamp;
    }
    return a.uniqueId.localeCompare(b.uniqueId);
  },

  /**
   * Checks if one sortable unique ID is before another
   */
  isBefore(a: SortableUniqueId, b: SortableUniqueId): boolean {
    return SortableUniqueId.compare(a, b) < 0;
  },

  /**
   * Checks if one sortable unique ID is after another
   */
  isAfter(a: SortableUniqueId, b: SortableUniqueId): boolean {
    return SortableUniqueId.compare(a, b) > 0;
  },

  /**
   * Gets the minimum sortable unique ID for a given timestamp
   */
  minForTimestamp(timestamp: Date): SortableUniqueId {
    return {
      timestamp: getUnixTimestamp(timestamp),
      uniqueId: '00000000-0000-0000-0000-000000000000',
    };
  },

  /**
   * Gets the maximum sortable unique ID for a given timestamp
   */
  maxForTimestamp(timestamp: Date): SortableUniqueId {
    return {
      timestamp: getUnixTimestamp(timestamp),
      uniqueId: 'ffffffff-ffff-ffff-ffff-ffffffffffff',
    };
  },
};