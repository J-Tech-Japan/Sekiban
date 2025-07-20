import { ResultAsync } from 'neverthrow';
import { IEvent } from './event';
import { EventRetrievalInfo } from './event-retrieval-info';

/**
 * Interface for reading events from storage
 * Matches C# IEventReader
 */
export interface IEventReader {
  /**
   * Get events based on retrieval information
   */
  getEvents(eventRetrievalInfo: EventRetrievalInfo): ResultAsync<readonly IEvent[], Error>;
}