import { describe, it, expect, beforeEach } from 'vitest';
import {
  ISnapshotStrategy,
  CountBasedSnapshotStrategy,
  TimeBasedSnapshotStrategy,
  HybridSnapshotStrategy,
  NoSnapshotStrategy,
} from './strategies';

describe('Snapshot Strategies', () => {
  describe('CountBasedSnapshotStrategy', () => {
    let strategy: CountBasedSnapshotStrategy;

    beforeEach(() => {
      strategy = new CountBasedSnapshotStrategy(100);
    });

    it('should trigger snapshot when event count threshold is reached', () => {
      expect(strategy.shouldTakeSnapshot(100, 0, null)).toBe(true);
      expect(strategy.shouldTakeSnapshot(150, 50, null)).toBe(true);
      expect(strategy.shouldTakeSnapshot(200, 100, null)).toBe(true);
    });

    it('should not trigger snapshot when threshold not reached', () => {
      expect(strategy.shouldTakeSnapshot(50, 0, null)).toBe(false);
      expect(strategy.shouldTakeSnapshot(99, 0, null)).toBe(false);
      expect(strategy.shouldTakeSnapshot(149, 50, null)).toBe(false);
    });

    it('should handle edge cases correctly', () => {
      expect(strategy.shouldTakeSnapshot(0, 0, null)).toBe(false);
      expect(strategy.shouldTakeSnapshot(1, 0, null)).toBe(false);
      expect(strategy.shouldTakeSnapshot(100, 100, null)).toBe(false);
    });

    it('should respect configured threshold', () => {
      const smallThreshold = new CountBasedSnapshotStrategy(10);
      expect(smallThreshold.shouldTakeSnapshot(10, 0, null)).toBe(true);
      expect(smallThreshold.shouldTakeSnapshot(9, 0, null)).toBe(false);
      
      const largeThreshold = new CountBasedSnapshotStrategy(1000);
      expect(largeThreshold.shouldTakeSnapshot(999, 0, null)).toBe(false);
      expect(largeThreshold.shouldTakeSnapshot(1000, 0, null)).toBe(true);
    });
  });

  describe('TimeBasedSnapshotStrategy', () => {
    let strategy: TimeBasedSnapshotStrategy;
    const oneHourMs = 60 * 60 * 1000;

    beforeEach(() => {
      strategy = new TimeBasedSnapshotStrategy(oneHourMs);
    });

    it('should trigger snapshot when no previous snapshot exists', () => {
      expect(strategy.shouldTakeSnapshot(1, 0, null)).toBe(true);
      expect(strategy.shouldTakeSnapshot(100, 50, null)).toBe(true);
    });

    it('should trigger snapshot when time interval has passed', () => {
      const oldSnapshot = new Date(Date.now() - oneHourMs - 1000);
      expect(strategy.shouldTakeSnapshot(100, 50, oldSnapshot)).toBe(true);
    });

    it('should not trigger snapshot when within time interval', () => {
      const recentSnapshot = new Date(Date.now() - (oneHourMs / 2));
      expect(strategy.shouldTakeSnapshot(100, 50, recentSnapshot)).toBe(false);
    });

    it('should handle edge case of exact interval', () => {
      const exactInterval = new Date(Date.now() - oneHourMs);
      expect(strategy.shouldTakeSnapshot(100, 50, exactInterval)).toBe(true);
    });

    it('should work with different intervals', () => {
      const fiveMinutes = new TimeBasedSnapshotStrategy(5 * 60 * 1000);
      const oldSnapshot = new Date(Date.now() - 6 * 60 * 1000);
      const recentSnapshot = new Date(Date.now() - 4 * 60 * 1000);
      
      expect(fiveMinutes.shouldTakeSnapshot(100, 50, oldSnapshot)).toBe(true);
      expect(fiveMinutes.shouldTakeSnapshot(100, 50, recentSnapshot)).toBe(false);
    });
  });

  describe('HybridSnapshotStrategy', () => {
    let strategy: HybridSnapshotStrategy;
    const oneHourMs = 60 * 60 * 1000;

    beforeEach(() => {
      strategy = new HybridSnapshotStrategy(100, oneHourMs);
    });

    it('should trigger on count threshold even if time not reached', () => {
      const recentSnapshot = new Date(Date.now() - 1000); // 1 second ago
      expect(strategy.shouldTakeSnapshot(100, 0, recentSnapshot)).toBe(true);
    });

    it('should trigger on time threshold even if count not reached', () => {
      const oldSnapshot = new Date(Date.now() - oneHourMs - 1000);
      expect(strategy.shouldTakeSnapshot(50, 0, oldSnapshot)).toBe(true);
    });

    it('should trigger when both thresholds are met', () => {
      const oldSnapshot = new Date(Date.now() - oneHourMs - 1000);
      expect(strategy.shouldTakeSnapshot(150, 50, oldSnapshot)).toBe(true);
    });

    it('should not trigger when neither threshold is met', () => {
      const recentSnapshot = new Date(Date.now() - 1000);
      expect(strategy.shouldTakeSnapshot(50, 0, recentSnapshot)).toBe(false);
    });

    it('should handle null last snapshot time', () => {
      expect(strategy.shouldTakeSnapshot(50, 0, null)).toBe(true); // Time-based triggers
      expect(strategy.shouldTakeSnapshot(100, 0, null)).toBe(true); // Both trigger
    });

    it('should work with custom thresholds', () => {
      const custom = new HybridSnapshotStrategy(10, 5 * 60 * 1000); // 10 events or 5 minutes
      const fourMinutesAgo = new Date(Date.now() - 4 * 60 * 1000);
      const sixMinutesAgo = new Date(Date.now() - 6 * 60 * 1000);
      
      expect(custom.shouldTakeSnapshot(5, 0, fourMinutesAgo)).toBe(false);
      expect(custom.shouldTakeSnapshot(10, 0, fourMinutesAgo)).toBe(true);
      expect(custom.shouldTakeSnapshot(5, 0, sixMinutesAgo)).toBe(true);
    });
  });

  describe('NoSnapshotStrategy', () => {
    let strategy: NoSnapshotStrategy;

    beforeEach(() => {
      strategy = new NoSnapshotStrategy();
    });

    it('should never trigger snapshots', () => {
      expect(strategy.shouldTakeSnapshot(0, 0, null)).toBe(false);
      expect(strategy.shouldTakeSnapshot(1000, 0, null)).toBe(false);
      expect(strategy.shouldTakeSnapshot(1000, 999, new Date())).toBe(false);
      
      const veryOldSnapshot = new Date(Date.now() - 365 * 24 * 60 * 60 * 1000);
      expect(strategy.shouldTakeSnapshot(10000, 0, veryOldSnapshot)).toBe(false);
    });
  });

  describe('Strategy Factory', () => {
    it('should create strategies from configuration', () => {
      const countStrategy = ISnapshotStrategy.fromConfig({
        strategy: 'count',
        countThreshold: 50,
      });
      expect(countStrategy).toBeInstanceOf(CountBasedSnapshotStrategy);

      const timeStrategy = ISnapshotStrategy.fromConfig({
        strategy: 'time',
        timeIntervalMs: 60000,
      });
      expect(timeStrategy).toBeInstanceOf(TimeBasedSnapshotStrategy);

      const hybridStrategy = ISnapshotStrategy.fromConfig({
        strategy: 'hybrid',
        countThreshold: 100,
        timeIntervalMs: 3600000,
      });
      expect(hybridStrategy).toBeInstanceOf(HybridSnapshotStrategy);

      const noStrategy = ISnapshotStrategy.fromConfig({
        strategy: 'none',
      });
      expect(noStrategy).toBeInstanceOf(NoSnapshotStrategy);
    });

    it('should use default values when not provided', () => {
      const countStrategy = ISnapshotStrategy.fromConfig({
        strategy: 'count',
      });
      expect(countStrategy).toBeInstanceOf(CountBasedSnapshotStrategy);

      const timeStrategy = ISnapshotStrategy.fromConfig({
        strategy: 'time',
      });
      expect(timeStrategy).toBeInstanceOf(TimeBasedSnapshotStrategy);

      const hybridStrategy = ISnapshotStrategy.fromConfig({
        strategy: 'hybrid',
      });
      expect(hybridStrategy).toBeInstanceOf(HybridSnapshotStrategy);
    });

    it('should throw for invalid strategy type', () => {
      expect(() => 
        ISnapshotStrategy.fromConfig({
          strategy: 'invalid' as any,
        })
      ).toThrow('Unknown snapshot strategy: invalid');
    });
  });
});