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

	readonly MaxCount: number | null;
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

export type DbSingleProjectionSnapshot = {
	readonly Id: string;
	readonly AggregateContainerGroup: string;
	readonly Snapshot: string | null;
	readonly LastEventId: string;
	readonly LastSortableUniqueId: string;
	readonly SavedVersion: number;
	readonly PayloadVersionIdentifier: string;
	readonly AggregateId: string;
	readonly PartitionKey: string;
	readonly DocumentType: string;
	readonly DocumentTypeName: string;
	readonly TimeStamp: string;
	readonly SortableUniqueId: string;
	readonly AggregateType: string;
	readonly RootPartitionKey: string;
};

export type DbSingleProjectionSnapshotQuery = {
	readonly Id: string | null;
	readonly AggregateContainerGroup: string | null;
	readonly PartitionKey: string | null;
	readonly AggregateId: string | null;
	readonly RootPartitionKey: string | null;
	readonly AggregateType: string | null;
	readonly PayloadVersionIdentifier: string | null;
	readonly SavedVersion: number | null;

	readonly IsLatestOnly: boolean;
};

export type DbMultiProjectionSnapshot = {
	readonly Id: string;
	readonly AggregateContainerGroup: string;
	readonly PartitionKey: string;
	readonly DocumentType: string;
	readonly DocumentTypeName: string;
	readonly TimeStamp: string;
	readonly SortableUniqueId: string;
	readonly AggregateType: string;
	readonly RootPartitionKey: string;
	readonly LastEventId: string;
	readonly LastSortableUniqueId: string;
	readonly SavedVersion: number;
	readonly PayloadVersionIdentifier: string;
};

export type DbMultiProjectionSnapshotQuery = {
	readonly AggregateContainerGroup: string | null;
	readonly PartitionKey: string | null;
	readonly PayloadVersionIdentifier: string | null;

	readonly IsLatestOnly: boolean;
};

export type DbBlob = {
	readonly Id: string;
	readonly Name: string;
	readonly Payload: string;
	readonly IsGzipped: boolean;
};

export type DbBlobQuery = {
	readonly Name: string | null;

	readonly MaxCount: number | null;
};
