import { describe, it, expect, vi } from 'vitest';
import { EventStreamDebugger } from './event-stream-debugger';
import { 
  EventDocument, 
  IEventPayload,
  PartitionKeys,
  SortableUniqueId 
} from '@sekiban/core';

// Test events
interface UserCreated extends IEventPayload {
  userId: string;
  name: string;
}

interface UserUpdated extends IEventPayload {
  userId: string;
  name?: string;
  email?: string;
}

describe('EventStreamDebugger', () => {
  const createTestEvents = (): EventDocument<IEventPayload>[] => {
    const partitionKeys = PartitionKeys.create('user-123', 'users');
    
    return [
      {
        id: '1',
        eventType: 'UserCreated',
        payload: { userId: 'user-123', name: 'John' },
        version: 1,
        timestamp: new Date('2024-01-01T10:00:00Z'),
        sortableUniqueId: SortableUniqueId.generate(),
        partitionKeys
      },
      {
        id: '2',
        eventType: 'UserUpdated',
        payload: { userId: 'user-123', name: 'John Doe' },
        version: 2,
        timestamp: new Date('2024-01-01T11:00:00Z'),
        sortableUniqueId: SortableUniqueId.generate(),
        partitionKeys
      },
      {
        id: '3',
        eventType: 'UserUpdated',
        payload: { userId: 'user-123', email: 'john@example.com' },
        version: 3,
        timestamp: new Date('2024-01-01T12:00:00Z'),
        sortableUniqueId: SortableUniqueId.generate(),
        partitionKeys
      }
    ];
  };

  describe('Event Stream Analysis', () => {
    it('should analyze event stream timeline', () => {
      const events = createTestEvents();
      const streamDebugger = new EventStreamDebugger(events);
      
      const timeline = streamDebugger.getTimeline();
      
      expect(timeline).toHaveLength(3);
      expect(timeline[0].eventType).toBe('UserCreated');
      expect(timeline[0].timeDelta).toBe(0);
      expect(timeline[1].timeDelta).toBe(3600000); // 1 hour in ms
      expect(timeline[2].timeDelta).toBe(3600000);
    });

    it('should detect event ordering issues', () => {
      const events = createTestEvents();
      // Swap events to create ordering issue
      [events[1], events[2]] = [events[2], events[1]];
      
      const streamDebugger = new EventStreamDebugger(events);
      const issues = streamDebugger.detectOrderingIssues();
      
      // We should get multiple issues because of swap
      expect(issues.length).toBeGreaterThan(0);
      const versionIssue = issues.find(i => i.type === 'VERSION_MISMATCH');
      expect(versionIssue).toBeDefined();
      expect(versionIssue?.actualVersion).toBe(3);
    });

    it('should detect timestamp anomalies', () => {
      const events = createTestEvents();
      // Make third event have earlier timestamp
      events[2].timestamp = new Date('2024-01-01T09:00:00Z');
      
      const streamDebugger = new EventStreamDebugger(events);
      const issues = streamDebugger.detectOrderingIssues();
      
      expect(issues).toHaveLength(1);
      expect(issues[0].type).toBe('TIMESTAMP_OUT_OF_ORDER');
    });
  });

  describe('Event Filtering', () => {
    it('should filter events by type', () => {
      const events = createTestEvents();
      const streamDebugger = new EventStreamDebugger(events);
      
      const filtered = streamDebugger.filterByType('UserUpdated');
      
      expect(filtered).toHaveLength(2);
      expect(filtered.every(e => e.eventType === 'UserUpdated')).toBe(true);
    });

    it('should filter events by time range', () => {
      const events = createTestEvents();
      const streamDebugger = new EventStreamDebugger(events);
      
      const filtered = streamDebugger.filterByTimeRange(
        new Date('2024-01-01T10:30:00Z'),
        new Date('2024-01-01T11:30:00Z')
      );
      
      expect(filtered).toHaveLength(1);
      expect(filtered[0].eventType).toBe('UserUpdated');
      expect(filtered[0].version).toBe(2);
    });

    it('should filter events by payload content', () => {
      const events = createTestEvents();
      const streamDebugger = new EventStreamDebugger(events);
      
      const filtered = streamDebugger.filterByPayload(payload => 
        'email' in payload && payload.email !== undefined
      );
      
      expect(filtered).toHaveLength(1);
      expect(filtered[0].version).toBe(3);
    });
  });

  describe('Event Statistics', () => {
    it('should calculate event statistics', () => {
      const events = createTestEvents();
      const streamDebugger = new EventStreamDebugger(events);
      
      const stats = streamDebugger.getStatistics();
      
      expect(stats.totalEvents).toBe(3);
      expect(stats.eventTypes['UserCreated']).toBe(1);
      expect(stats.eventTypes['UserUpdated']).toBe(2);
      expect(stats.averageTimeBetweenEvents).toBe(3600000); // 1 hour
      expect(stats.timeSpan).toBe(7200000); // 2 hours
    });
  });

  describe('Event Replay', () => {
    it('should replay events with callbacks', () => {
      const events = createTestEvents();
      const streamDebugger = new EventStreamDebugger(events);
      
      const replayLog: string[] = [];
      
      streamDebugger.replay({
        onEvent: (event, index) => {
          replayLog.push(`Event ${index}: ${event.eventType}`);
        },
        onComplete: (count) => {
          replayLog.push(`Replay complete: ${count} events`);
        }
      });
      
      expect(replayLog).toEqual([
        'Event 0: UserCreated',
        'Event 1: UserUpdated',
        'Event 2: UserUpdated',
        'Replay complete: 3 events'
      ]);
    });

    it('should replay events up to specific version', () => {
      const events = createTestEvents();
      const streamDebugger = new EventStreamDebugger(events);
      
      const replayedEvents: EventDocument<IEventPayload>[] = [];
      
      streamDebugger.replayToVersion(2, {
        onEvent: (event) => {
          replayedEvents.push(event);
        }
      });
      
      expect(replayedEvents).toHaveLength(2);
      expect(replayedEvents[1].version).toBe(2);
    });

    it('should replay events up to specific time', () => {
      const events = createTestEvents();
      const streamDebugger = new EventStreamDebugger(events);
      
      const replayedEvents: EventDocument<IEventPayload>[] = [];
      
      streamDebugger.replayToTime(new Date('2024-01-01T11:30:00Z'), {
        onEvent: (event) => {
          replayedEvents.push(event);
        }
      });
      
      expect(replayedEvents).toHaveLength(2);
    });
  });

  describe('Event Diffing', () => {
    it('should diff consecutive events', () => {
      const events = createTestEvents();
      const streamDebugger = new EventStreamDebugger(events);
      
      const diffs = streamDebugger.diffEvents();
      
      expect(diffs).toHaveLength(2);
      expect(diffs[0].changes).toContainEqual({
        field: 'name',
        oldValue: 'John',
        newValue: 'John Doe'
      });
      expect(diffs[1].changes).toContainEqual({
        field: 'email',
        oldValue: undefined,
        newValue: 'john@example.com'
      });
    });
  });

  describe('Export Formats', () => {
    it('should export as markdown table', () => {
      const events = createTestEvents();
      const streamDebugger = new EventStreamDebugger(events);
      
      const markdown = streamDebugger.exportAsMarkdown();
      
      expect(markdown).toContain('| Version | Event Type | Timestamp |');
      expect(markdown).toContain('| 1 | UserCreated |');
      expect(markdown).toContain('| 2 | UserUpdated |');
      expect(markdown).toContain('| 3 | UserUpdated |');
    });

    it('should export as CSV', () => {
      const events = createTestEvents();
      const streamDebugger = new EventStreamDebugger(events);
      
      const csv = streamDebugger.exportAsCSV();
      
      expect(csv).toContain('Version,Event Type,Timestamp,Payload');
      expect(csv).toContain('1,UserCreated,');
      expect(csv).toContain('2,UserUpdated,');
    });

    it('should export as JSON with formatting', () => {
      const events = createTestEvents();
      const streamDebugger = new EventStreamDebugger(events);
      
      const json = streamDebugger.exportAsJSON(true);
      const parsed = JSON.parse(json);
      
      expect(parsed.events).toHaveLength(3);
      expect(parsed.statistics).toBeDefined();
      expect(parsed.issues).toBeDefined();
    });
  });

  describe('Event Visualization', () => {
    it('should generate ASCII timeline', () => {
      const events = createTestEvents();
      const streamDebugger = new EventStreamDebugger(events);
      
      const timeline = streamDebugger.generateASCIITimeline();
      
      expect(timeline).toContain('UserCreated');
      expect(timeline).toContain('UserUpdated');
      expect(timeline).toContain('──');
      expect(timeline).toContain('10:00');
      expect(timeline).toContain('11:00');
      expect(timeline).toContain('12:00');
    });
  });
});