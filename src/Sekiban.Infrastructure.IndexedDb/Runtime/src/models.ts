export type DbEvent = {
  Id: string;
  Payload: string;
  Version: number;
  CallHistories: string;
  AggregateId: string;
  PartitionKey: string;
  DocumentType: string;
  DocumentTypeName: string;
  TimeStamp: string;
  SortableUniqueId: string;
  AggregateType: string;
  RootPartitionKey: string;
};

export type DbEventQuery = {
  RootPartitionKey: string | null;
  PartitionKey: string | null;
  AggregateTypes: string[] | null;
  SortableIdStart: string | null;
  SortableIdEnd: string | null;
};
