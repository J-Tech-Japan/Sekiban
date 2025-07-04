import type { SnapshotConfiguration } from './types';

/**
 * Interface for snapshot strategy implementations
 */
export interface ISnapshotStrategy {
  /**
   * Determines whether a snapshot should be taken
   * @param eventCount Current total event count
   * @param lastSnapshotEventCount Event count at last snapshot
   * @param lastSnapshotTime When the last snapshot was taken
   * @returns Whether to take a snapshot now
   */
  shouldTakeSnapshot(
    eventCount: number,
    lastSnapshotEventCount: number,
    lastSnapshotTime: Date | null
  ): boolean;
}

// Add static factory method to interface
export namespace ISnapshotStrategy {
  /**
   * Create a strategy from configuration
   */
  export function fromConfig(config: SnapshotConfiguration): ISnapshotStrategy {
    switch (config.strategy) {
      case 'count':
        return new CountBasedSnapshotStrategy(config.countThreshold ?? 100);
      
      case 'time':
        return new TimeBasedSnapshotStrategy(config.timeIntervalMs ?? 3600000); // 1 hour default
      
      case 'hybrid':
        return new HybridSnapshotStrategy(
          config.countThreshold ?? 100,
          config.timeIntervalMs ?? 3600000
        );
      
      case 'none':
        return new NoSnapshotStrategy();
      
      default:
        throw new Error(`Unknown snapshot strategy: ${config.strategy}`);
    }
  }
}

/**
 * Takes snapshots based on event count
 */
export class CountBasedSnapshotStrategy implements ISnapshotStrategy {
  constructor(private readonly threshold: number) {
    if (threshold <= 0) {
      throw new Error('Count threshold must be positive');
    }
  }

  shouldTakeSnapshot(
    eventCount: number,
    lastSnapshotEventCount: number,
    lastSnapshotTime: Date | null
  ): boolean {
    const eventsSinceSnapshot = eventCount - lastSnapshotEventCount;
    return eventsSinceSnapshot >= this.threshold;
  }
}

/**
 * Takes snapshots based on time intervals
 */
export class TimeBasedSnapshotStrategy implements ISnapshotStrategy {
  constructor(private readonly intervalMs: number) {
    if (intervalMs <= 0) {
      throw new Error('Time interval must be positive');
    }
  }

  shouldTakeSnapshot(
    eventCount: number,
    lastSnapshotEventCount: number,
    lastSnapshotTime: Date | null
  ): boolean {
    // Always take snapshot if no previous snapshot exists
    if (!lastSnapshotTime) {
      return true;
    }

    const timeSinceSnapshot = Date.now() - lastSnapshotTime.getTime();
    return timeSinceSnapshot >= this.intervalMs;
  }
}

/**
 * Takes snapshots based on both event count and time
 */
export class HybridSnapshotStrategy implements ISnapshotStrategy {
  private readonly countStrategy: CountBasedSnapshotStrategy;
  private readonly timeStrategy: TimeBasedSnapshotStrategy;

  constructor(
    eventThreshold: number,
    timeIntervalMs: number
  ) {
    this.countStrategy = new CountBasedSnapshotStrategy(eventThreshold);
    this.timeStrategy = new TimeBasedSnapshotStrategy(timeIntervalMs);
  }

  shouldTakeSnapshot(
    eventCount: number,
    lastSnapshotEventCount: number,
    lastSnapshotTime: Date | null
  ): boolean {
    // Trigger if either condition is met
    return (
      this.countStrategy.shouldTakeSnapshot(eventCount, lastSnapshotEventCount, lastSnapshotTime) ||
      this.timeStrategy.shouldTakeSnapshot(eventCount, lastSnapshotEventCount, lastSnapshotTime)
    );
  }
}

/**
 * Never takes snapshots (opt-out strategy)
 */
export class NoSnapshotStrategy implements ISnapshotStrategy {
  shouldTakeSnapshot(
    eventCount: number,
    lastSnapshotEventCount: number,
    lastSnapshotTime: Date | null
  ): boolean {
    return false;
  }
}

/**
 * Custom strategy that can be extended
 */
export abstract class CustomSnapshotStrategy implements ISnapshotStrategy {
  abstract shouldTakeSnapshot(
    eventCount: number,
    lastSnapshotEventCount: number,
    lastSnapshotTime: Date | null
  ): boolean;
}

/**
 * Default strategy configurations
 */
export const DefaultStrategies = {
  /**
   * Conservative strategy for low-traffic aggregates
   */
  conservative: (): ISnapshotStrategy => new HybridSnapshotStrategy(500, 24 * 60 * 60 * 1000), // 500 events or 24 hours

  /**
   * Balanced strategy for medium-traffic aggregates
   */
  balanced: (): ISnapshotStrategy => new HybridSnapshotStrategy(100, 60 * 60 * 1000), // 100 events or 1 hour

  /**
   * Aggressive strategy for high-traffic aggregates
   */
  aggressive: (): ISnapshotStrategy => new HybridSnapshotStrategy(20, 5 * 60 * 1000), // 20 events or 5 minutes

  /**
   * Development strategy for testing
   */
  development: (): ISnapshotStrategy => new CountBasedSnapshotStrategy(10), // Every 10 events
} as const;