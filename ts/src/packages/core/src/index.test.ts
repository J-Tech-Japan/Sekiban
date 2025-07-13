import { describe, it, expect } from 'vitest';
import * as Sekiban from './index.js';

describe('@sekiban/core', () => {
  it('should export VERSION', () => {
    expect(Sekiban.VERSION).toBe('0.0.1');
  });

  it('should export Result types', () => {
    expect(Sekiban.ok).toBeDefined();
    expect(Sekiban.err).toBeDefined();
    expect(Sekiban.Result).toBeDefined();
  });

  it('should export error types', () => {
    expect(Sekiban.SekibanError).toBeDefined();
    expect(Sekiban.CommandValidationError).toBeDefined();
    expect(Sekiban.EventStoreError).toBeDefined();
  });

  it('should export utility functions', () => {
    expect(Sekiban.generateUuid).toBeDefined();
    expect(Sekiban.createVersion7).toBeDefined();
    expect(Sekiban.isValidUuid).toBeDefined();
  });

  it('should export document utilities', () => {
    expect(Sekiban.PartitionKeys).toBeDefined();
    expect(Sekiban.SortableUniqueId).toBeDefined();
    expect(Sekiban.Metadata).toBeDefined();
  });

  it('should export event types', () => {
    expect(Sekiban.Event).toBeDefined();
    expect(Sekiban.createEvent).toBeDefined();
    expect(Sekiban.InMemoryEventStore).toBeDefined();
  });

  it('should export aggregate types', () => {
    expect(Sekiban.AggregateProjector).toBeDefined();
    expect(Sekiban.createProjector).toBeDefined();
  });

  it('should export command types', () => {
    expect(Sekiban.validateCommand).toBeDefined();
    expect(Sekiban.required).toBeDefined();
    expect(Sekiban.email).toBeDefined();
  });

  it('should export query types', () => {
    expect(Sekiban.MultiProjectionState).toBeDefined();
  });

  it('should export storage provider types', () => {
    expect(Sekiban.InMemoryEventStore).toBeDefined();
  });
});