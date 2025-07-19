import { EventDocument, IEventPayload } from '@sekiban/core';

/**
 * Timeline entry for event analysis
 */
export interface TimelineEntry {
  event: EventDocument<IEventPayload>;
  index: number;
  eventType: string;
  version: number;
  timestamp: Date;
  timeDelta: number; // ms since previous event
}

/**
 * Event ordering issue
 */
export interface OrderingIssue {
  type: 'VERSION_MISMATCH' | 'TIMESTAMP_OUT_OF_ORDER' | 'DUPLICATE_VERSION';
  index: number;
  event: EventDocument<IEventPayload>;
  expectedVersion?: number;
  actualVersion?: number;
  description: string;
}

/**
 * Event statistics
 */
export interface EventStatistics {
  totalEvents: number;
  eventTypes: Record<string, number>;
  averageTimeBetweenEvents: number;
  timeSpan: number;
  firstEvent?: Date;
  lastEvent?: Date;
}

/**
 * Event diff between consecutive events
 */
export interface EventDiff {
  fromVersion: number;
  toVersion: number;
  eventType: string;
  changes: Array<{
    field: string;
    oldValue: any;
    newValue: any;
  }>;
}

/**
 * Replay callbacks
 */
export interface ReplayCallbacks {
  onEvent?: (event: EventDocument<IEventPayload>, index: number) => void;
  onComplete?: (eventCount: number) => void;
}

/**
 * Debugger for analyzing event streams
 */
export class EventStreamDebugger {
  private events: EventDocument<IEventPayload>[];

  constructor(events: EventDocument<IEventPayload>[]) {
    this.events = [...events];
  }

  /**
   * Get timeline view of events
   */
  getTimeline(): TimelineEntry[] {
    const timeline: TimelineEntry[] = [];
    
    for (let i = 0; i < this.events.length; i++) {
      const event = this.events[i];
      const previousTime = i > 0 ? this.events[i - 1].timestamp : event.timestamp;
      
      timeline.push({
        event,
        index: i,
        eventType: event.eventType,
        version: event.version,
        timestamp: event.timestamp,
        timeDelta: event.timestamp.getTime() - previousTime.getTime()
      });
    }
    
    return timeline;
  }

  /**
   * Detect ordering issues in the event stream
   */
  detectOrderingIssues(): OrderingIssue[] {
    const issues: OrderingIssue[] = [];
    
    for (let i = 0; i < this.events.length; i++) {
      const event = this.events[i];
      
      // Check version ordering
      if (i > 0) {
        const expectedVersion = this.events[i - 1].version + 1;
        if (event.version !== expectedVersion) {
          issues.push({
            type: 'VERSION_MISMATCH',
            index: i,
            event,
            expectedVersion,
            actualVersion: event.version,
            description: `Expected version ${expectedVersion}, got ${event.version}`
          });
        }
        
        // Check timestamp ordering
        if (event.timestamp < this.events[i - 1].timestamp) {
          issues.push({
            type: 'TIMESTAMP_OUT_OF_ORDER',
            index: i,
            event,
            description: `Event timestamp is earlier than previous event`
          });
        }
      }
      
      // Check for duplicate versions
      const duplicates = this.events.filter(e => e.version === event.version);
      if (duplicates.length > 1) {
        issues.push({
          type: 'DUPLICATE_VERSION',
          index: i,
          event,
          description: `Version ${event.version} appears ${duplicates.length} times`
        });
      }
    }
    
    return issues;
  }

  /**
   * Filter events by type
   */
  filterByType(eventType: string): EventDocument<IEventPayload>[] {
    return this.events.filter(e => e.eventType === eventType);
  }

  /**
   * Filter events by time range
   */
  filterByTimeRange(start: Date, end: Date): EventDocument<IEventPayload>[] {
    return this.events.filter(e => 
      e.timestamp >= start && e.timestamp <= end
    );
  }

  /**
   * Filter events by payload predicate
   */
  filterByPayload(predicate: (payload: IEventPayload) => boolean): EventDocument<IEventPayload>[] {
    return this.events.filter(e => predicate(e.payload));
  }

  /**
   * Get event statistics
   */
  getStatistics(): EventStatistics {
    if (this.events.length === 0) {
      return {
        totalEvents: 0,
        eventTypes: {},
        averageTimeBetweenEvents: 0,
        timeSpan: 0
      };
    }

    const eventTypes: Record<string, number> = {};
    let totalTimeDelta = 0;
    
    for (let i = 0; i < this.events.length; i++) {
      const event = this.events[i];
      eventTypes[event.eventType] = (eventTypes[event.eventType] || 0) + 1;
      
      if (i > 0) {
        totalTimeDelta += event.timestamp.getTime() - this.events[i - 1].timestamp.getTime();
      }
    }
    
    const firstEvent = this.events[0].timestamp;
    const lastEvent = this.events[this.events.length - 1].timestamp;
    
    return {
      totalEvents: this.events.length,
      eventTypes,
      averageTimeBetweenEvents: this.events.length > 1 ? totalTimeDelta / (this.events.length - 1) : 0,
      timeSpan: lastEvent.getTime() - firstEvent.getTime(),
      firstEvent,
      lastEvent
    };
  }

  /**
   * Replay events with callbacks
   */
  replay(callbacks: ReplayCallbacks): void {
    for (let i = 0; i < this.events.length; i++) {
      callbacks.onEvent?.(this.events[i], i);
    }
    callbacks.onComplete?.(this.events.length);
  }

  /**
   * Replay events up to specific version
   */
  replayToVersion(version: number, callbacks: ReplayCallbacks): void {
    let count = 0;
    for (let i = 0; i < this.events.length; i++) {
      if (this.events[i].version > version) break;
      callbacks.onEvent?.(this.events[i], i);
      count++;
    }
    callbacks.onComplete?.(count);
  }

  /**
   * Replay events up to specific time
   */
  replayToTime(time: Date, callbacks: ReplayCallbacks): void {
    let count = 0;
    for (let i = 0; i < this.events.length; i++) {
      if (this.events[i].timestamp > time) break;
      callbacks.onEvent?.(this.events[i], i);
      count++;
    }
    callbacks.onComplete?.(count);
  }

  /**
   * Diff consecutive events
   */
  diffEvents(): EventDiff[] {
    const diffs: EventDiff[] = [];
    
    for (let i = 1; i < this.events.length; i++) {
      const prev = this.events[i - 1];
      const curr = this.events[i];
      
      const changes: EventDiff['changes'] = [];
      const prevPayload = prev.payload as any;
      const currPayload = curr.payload as any;
      
      // Find all keys in both payloads
      const allKeys = new Set([
        ...Object.keys(prevPayload),
        ...Object.keys(currPayload)
      ]);
      
      for (const key of allKeys) {
        if (prevPayload[key] !== currPayload[key]) {
          changes.push({
            field: key,
            oldValue: prevPayload[key],
            newValue: currPayload[key]
          });
        }
      }
      
      diffs.push({
        fromVersion: prev.version,
        toVersion: curr.version,
        eventType: curr.eventType,
        changes
      });
    }
    
    return diffs;
  }

  /**
   * Export as markdown table
   */
  exportAsMarkdown(): string {
    let markdown = '| Version | Event Type | Timestamp | Payload Summary |\n';
    markdown += '|---------|------------|-----------|----------------|\n';
    
    for (const event of this.events) {
      const timestamp = event.timestamp.toISOString();
      const payloadSummary = JSON.stringify(event.payload).substring(0, 50) + '...';
      markdown += `| ${event.version} | ${event.eventType} | ${timestamp} | ${payloadSummary} |\n`;
    }
    
    return markdown;
  }

  /**
   * Export as CSV
   */
  exportAsCSV(): string {
    let csv = 'Version,Event Type,Timestamp,Payload\n';
    
    for (const event of this.events) {
      const timestamp = event.timestamp.toISOString();
      const payload = JSON.stringify(event.payload).replace(/"/g, '""');
      csv += `${event.version},${event.eventType},${timestamp},"${payload}"\n`;
    }
    
    return csv;
  }

  /**
   * Export as JSON with metadata
   */
  exportAsJSON(includeAnalysis: boolean = false): string {
    const data: any = {
      events: this.events,
      metadata: {
        count: this.events.length,
        exported: new Date().toISOString()
      }
    };
    
    if (includeAnalysis) {
      data.statistics = this.getStatistics();
      data.issues = this.detectOrderingIssues();
      data.timeline = this.getTimeline();
    }
    
    return JSON.stringify(data, null, 2);
  }

  /**
   * Generate ASCII timeline visualization
   */
  generateASCIITimeline(): string {
    if (this.events.length === 0) return 'No events';
    
    let timeline = '';
    const timeFormatter = (date: Date) => {
      const hours = date.getUTCHours().toString().padStart(2, '0');
      const minutes = date.getUTCMinutes().toString().padStart(2, '0');
      return `${hours}:${minutes}`;
    };
    
    for (let i = 0; i < this.events.length; i++) {
      const event = this.events[i];
      const time = timeFormatter(event.timestamp);
      
      if (i > 0) {
        timeline += '│\n';
        timeline += '├──\n';
        timeline += '│\n';
      }
      
      timeline += `● ${time} - ${event.eventType} (v${event.version})\n`;
    }
    
    return timeline;
  }
}