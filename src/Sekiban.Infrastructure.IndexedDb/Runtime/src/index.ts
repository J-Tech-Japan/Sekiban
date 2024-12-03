import {DbEvent, DbEventQuery} from './models';

const wrapio =
  <T, U>(func: (value: T) => Promise<U>) =>
  async (input: string): Promise<string> => {
    const args = JSON.parse(input);

    const result = await func(args);

    const output = JSON.stringify(result);
    return output;
  };

export const init = async (contextName: string) => {
  // TODO: use IndexedDB
  const events = new Array<DbEvent>();

  const writeEventAsync = async (event: DbEvent): Promise<void> => {
    events.push(structuredClone(event));
  };

  const getEventsAsync = async (query: DbEventQuery): Promise<DbEvent[]> =>
    events
      .filter(
        x =>
          query.RootPartitionKey === null ||
          x.RootPartitionKey === query.RootPartitionKey,
      )
      .filter(
        x =>
          query.PartitionKey === null || x.PartitionKey === query.PartitionKey,
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
      );

  return {
    writeEventAsync: wrapio(writeEventAsync),
    getEventsAsync: wrapio(getEventsAsync),
  };
};
