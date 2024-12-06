import {
  DbCommand,
  DbCommandQuery,
  DbEvent,
  DbEventQuery,
  DbMultiProjectionSnapshot,
  DbMultiProjectionSnapshotQuery,
  DbSingleProjectionSnapshot,
  DbSingleProjectionSnapshotQuery,
} from './models';

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

const asc =
  <T>(id: (value: T) => string) =>
  (x: T, y: T): number =>
    id(x).localeCompare(id(y));

const desc =
  <T>(id: (value: T) => string) =>
  (x: T, y: T): number =>
    id(y).localeCompare(id(x));

const filterEvents = (events: DbEvent[], query: DbEventQuery): DbEvent[] => {
  const items = events
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
    .toSorted(asc(x => x.SortableUniqueId))
    .map(x => structuredClone(x));

  if (query.MaxCount !== null) {
    return items.slice(0, query.MaxCount);
  } else {
    return items;
  }
};

export const init = async (contextName: string) => {
  // TODO: use IndexedDB
  const events: DbEvent[] = [];
  const dissolvableEvents: DbEvent[] = [];
  const commands: DbCommand[] = [];
  const singleProjectionSnapshots: DbSingleProjectionSnapshot[] = [];
  const multiProjectionSnapshots: DbMultiProjectionSnapshot[] = [];

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
      .toSorted(asc(x => x.SortableUniqueId))
      .map(x => structuredClone(x));

  const removeAllCommandsAsync = async (): Promise<void> => {
    commands.splice(0);
  };

  const writeSingleProjectionSnapshotAsync = async (
    snapshot: DbSingleProjectionSnapshot,
  ): Promise<void> => {
    singleProjectionSnapshots.push(structuredClone(snapshot));
  };

  const getSingleProjectionSnapshotsAsync = async (
    query: DbSingleProjectionSnapshotQuery,
  ): Promise<DbSingleProjectionSnapshot[]> => {
    const items = singleProjectionSnapshots
      .filter(x => query.Id === null || x.Id === query.Id)
      .filter(
        x =>
          query.AggregateContainerGroup === null ||
          x.AggregateContainerGroup === query.AggregateContainerGroup,
      )
      .filter(
        x =>
          query.PartitionKey === null || x.PartitionKey === query.PartitionKey,
      )
      .filter(
        x => query.AggregateId === null || x.AggregateId === query.AggregateId,
      )
      .filter(
        x =>
          query.RootPartitionKey === null ||
          x.RootPartitionKey === query.RootPartitionKey,
      )
      .filter(
        x =>
          query.AggregateType === null ||
          x.AggregateType === query.AggregateType,
      )
      .filter(
        x =>
          query.PayloadVersionIdentifier === null ||
          x.PayloadVersionIdentifier === query.PayloadVersionIdentifier,
      )
      .filter(
        x =>
          query.SavedVersion === null || x.SavedVersion === query.SavedVersion,
      )
      .toSorted(desc(x => x.LastSortableUniqueId))
      .map(x => structuredClone(x));

    if (query.IsLatestOnly) {
      return items.slice(0, 1);
    } else {
      return items;
    }
  };

  const removeAllSingleProjectionSnapshotsAsync = async (): Promise<void> => {
    singleProjectionSnapshots.splice(0);
  };

  const writeMultiProjectionSnapshotAsync = async (
    payload: DbMultiProjectionSnapshot,
  ): Promise<void> => {
    multiProjectionSnapshots.push(structuredClone(payload));
  };

  const getMultiProjectionSnapshotsAsync = async (
    query: DbMultiProjectionSnapshotQuery,
  ): Promise<DbMultiProjectionSnapshot[]> => {
    const items = multiProjectionSnapshots
      .filter(
        x =>
          query.AggregateContainerGroup === null ||
          x.AggregateContainerGroup === query.AggregateContainerGroup,
      )
      .filter(
        x =>
          query.PartitionKey === null || x.PartitionKey === query.PartitionKey,
      )
      .filter(
        x =>
          query.PayloadVersionIdentifier === null ||
          x.PayloadVersionIdentifier === query.PayloadVersionIdentifier,
      )
      .toSorted(desc(x => x.LastSortableUniqueId))
      .map(x => structuredClone(x));

    if (query.IsLatestOnly) {
      return items.slice(0, 1);
    } else {
      return items;
    }
  };

  const removeAllMultiProjectionSnapshotsAsync = async (): Promise<void> => {
    multiProjectionSnapshots.splice(0);
  };

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

    writeSingleProjectionSnapshotAsync: wrapio(
      writeSingleProjectionSnapshotAsync,
    ),
    getSingleProjectionSnapshotsAsync: wrapio(
      getSingleProjectionSnapshotsAsync,
    ),
    removeAllSingleProjectionSnapshotsAsync: wrapio(
      removeAllSingleProjectionSnapshotsAsync,
    ),

    writeMultiProjectionSnapshotAsync: wrapio(
      writeMultiProjectionSnapshotAsync,
    ),
    getMultiProjectionSnapshotsAsync: wrapio(getMultiProjectionSnapshotsAsync),
    removeAllMultiProjectionSnapshotsAsync: wrapio(
      removeAllMultiProjectionSnapshotsAsync,
    ),
  };
};
