import { IEvent } from './event';

/**
 * Interface for writing events to storage
 * Matches C# IEventWriter
 */
export interface IEventWriter {
  /**
   * Save events to storage
   */
  saveEvents<TEvent extends IEvent>(events: TEvent[]): Promise<void>;
}