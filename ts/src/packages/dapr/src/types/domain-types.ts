import type { AggregateProjector } from '@sekiban/core';

/**
 * Domain types registry for Sekiban
 * Mirrors the C# SekibanDomainTypes
 */
export interface SekibanDomainTypes {
  /**
   * Registry of projector types by name
   */
  projectorRegistry: Map<string, new (...args: any[]) => AggregateProjector<any>>;
  
  /**
   * Event types registry
   */
  eventTypes: Record<string, any>;
  
  /**
   * Command types registry
   */
  commandTypes: Record<string, any>;
  
  /**
   * JSON serializer options
   */
  jsonSerializerOptions?: any;
}