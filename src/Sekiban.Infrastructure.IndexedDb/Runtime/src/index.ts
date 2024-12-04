import {DbCommand, DbCommandQuery, DbEvent, DbEventQuery} from './models';

const wrapio =
  <T, U>(func: (value: T) => Promise<U>) =>
  async (input: string | null): Promise<string | null> => {
    const args =
      input !== undefined && input !== null ? JSON.parse(input) : null;

    const result = await func(args);

    const output =
      result !== undefined && result !== null ? JSON.stringify(result) : null;
    return output;
  };

const filterEvents = (events: DbEvent[], query: DbEventQuery): DbEvent[] =>
  events
    .filter(
      x =>
        query.RootPartitionKey === null ||
        x.RootPartitionKey === query.RootPartitionKey,
    )
    .filter(
      x => query.PartitionKey === null || x.PartitionKey === query.PartitionKey,
    )
    .filter(
      x =>
        query.AggregateTypes === null ||
        query.AggregateTypes.includes(x.AggregateType),
    )
    .filter(
      x =>
        query.SortableIdStart === null ||
        query.SortableIdStart <= x.SortableUniqueId,
    )
    .filter(
      x =>
        query.SortableIdEnd === null ||
        x.SortableUniqueId <= query.SortableIdEnd,
    )
    .map(x => structuredClone(x));

export const init = async (contextName: string) => {
  // TODO: use IndexedDB
  const events: DbEvent[] = [];
  const dissolvableEvents: DbEvent[] = [];
  const commands: DbCommand[] = [];

  const writeEventAsync = async (event: DbEvent): Promise<void> => {
    events.push(structuredClone(event));
  };

  const getEventsAsync = async (query: DbEventQuery): Promise<DbEvent[]> =>
    filterEvents(events, query);

  const removeAllEventsAsync = async (): Promise<void> => {
    events.splice(0);
  };

  const writeDissolvableEventAsync = async (event: DbEvent): Promise<void> => {
    dissolvableEvents.push(structuredClone(event));
  };

  const getDissolvableEventsAsync = async (
    query: DbEventQuery,
  ): Promise<DbEvent[]> => filterEvents(dissolvableEvents, query);

  const removeAllDissolvableEventsAsync = async (): Promise<void> => {
    dissolvableEvents.splice(0);
  };

  const writeCommandAsync = async (command: DbCommand): Promise<void> => {
    commands.push(structuredClone(command));
  };

  const getCommandsAsync = async (
    query: DbCommandQuery,
  ): Promise<DbCommand[]> =>
    commands
      .filter(
        x =>
          query.SortableIdStart === null ||
          query.SortableIdStart <= x.SortableUniqueId,
      )
      .filter(
        x =>
          query.PartitionKey === null || x.PartitionKey === query.PartitionKey,
      )
      .filter(
        x =>
          query.AggregateContainerGroup === null ||
          x.AggregateContainerGroup === query.AggregateContainerGroup,
      )
      .map(x => structuredClone(x));

  const removeAllCommandsAsync = async (): Promise<void> => {};

  const removeAllSingleProjectionSnapshotsAsync = async (): Promise<void> => {};
  const removeAllMultiProjectionSnapshotsAsync = async (): Promise<void> => {};

  return {
    writeEventAsync: wrapio(writeEventAsync),
    getEventsAsync: wrapio(getEventsAsync),
    removeAllEventsAsync: wrapio(removeAllEventsAsync),

    writeDissolvableEventAsync: wrapio(writeDissolvableEventAsync),
    getDissolvableEventsAsync: wrapio(getDissolvableEventsAsync),
    removeAllDissolvableEventsAsync: wrapio(removeAllDissolvableEventsAsync),

    writeCommandAsync: wrapio(writeCommandAsync),
    getCommandsAsync: wrapio(getCommandsAsync),
    removeAllCommandsAsync: wrapio(removeAllCommandsAsync),

    removeAllSingleProjectionSnapshotsAsync: wrapio(
      removeAllSingleProjectionSnapshotsAsync,
    ),
    removeAllMultiProjectionSnapshotsAsync: wrapio(
      removeAllMultiProjectionSnapshotsAsync,
    ),
  };
};
