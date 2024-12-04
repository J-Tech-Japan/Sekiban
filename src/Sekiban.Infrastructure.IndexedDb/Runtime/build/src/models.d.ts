export type DbEvent = {
    readonly Id: string;
    readonly Payload: string;
    readonly Version: number;
    readonly CallHistories: string;
    readonly AggregateId: string;
    readonly PartitionKey: string;
    readonly DocumentType: string;
    readonly DocumentTypeName: string;
    readonly TimeStamp: string;
    readonly SortableUniqueId: string;
    readonly AggregateType: string;
    readonly RootPartitionKey: string;
};
export type DbEventQuery = {
    readonly RootPartitionKey: string | null;
    readonly PartitionKey: string | null;
    readonly AggregateTypes: readonly string[] | null;
    readonly SortableIdStart: string | null;
    readonly SortableIdEnd: string | null;
};
export type DbCommand = {
    readonly Id: string;
    readonly AggregateContainerGroup: string;
    readonly AggregateId: string;
    readonly PartitionKey: string;
    readonly DocumentType: string;
    readonly DocumentTypeName: string;
    readonly ExecutedUser: string | null;
    readonly Exception: string | null;
    readonly CallHistories: string;
    readonly Payload: string;
    readonly TimeStamp: string;
    readonly SortableUniqueId: string;
    readonly AggregateType: string;
    readonly RootPartitionKey: string;
};
export type DbCommandQuery = {
    readonly SortableIdStart: string | null;
    readonly PartitionKey: string | null;
    readonly AggregateContainerGroup: string | null;
};
