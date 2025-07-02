import { describe, it, expect } from 'vitest';
import * as Sekiban from './index';

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
    expect(Sekiban.getCurrentUtcTimestamp).toBeDefined();
  });

  it('should export document utilities', () => {
    expect(Sekiban.PartitionKeys).toBeDefined();
    expect(Sekiban.SortableUniqueId).toBeDefined();
    expect(Sekiban.Metadata).toBeDefined();
  });

  it('should export event types', () => {
    expect(Sekiban.EventBuilder).toBeDefined();
    expect(Sekiban.InMemoryEventStream).toBeDefined();
  });

  it('should export aggregate types', () => {
    expect(Sekiban.AggregateProjector).toBeDefined();
    expect(Sekiban.createProjector).toBeDefined();
  });

  it('should export command types', () => {
    expect(Sekiban.CommandHandler).toBeDefined();
    expect(Sekiban.CommandHandlerRegistry).toBeDefined();
  });

  it('should export query types', () => {
    expect(Sekiban.QueryHandler).toBeDefined();
    expect(Sekiban.QueryHandlerRegistry).toBeDefined();
    expect(Sekiban.MultiProjection).toBeDefined();
  });

  it('should export executor types', () => {
    expect(Sekiban.InMemorySekibanExecutor).toBeDefined();
    expect(Sekiban.InMemorySekibanExecutorBuilder).toBeDefined();
  });
});