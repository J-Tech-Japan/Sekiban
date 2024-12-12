import type { DBSchema } from "idb";
import type {
	DbCommand,
	DbEvent,
	DbMultiProjectionSnapshot,
	DbSingleProjectionSnapshot,
} from "./models";

export interface SekibanDb extends DBSchema {
	events: {
		key: string;
		value: DbEvent;
		indexes: {
			RootPartitionKey: DbEvent["RootPartitionKey"];
			PartitionKey: DbEvent["PartitionKey"];
			AggregateType: DbEvent["AggregateType"];
			SortableUniqueId: DbEvent["SortableUniqueId"];
		};
	};

	"dissolvable-events": {
		key: string;
		value: DbEvent;
		indexes: {
			RootPartitionKey: DbEvent["RootPartitionKey"];
			PartitionKey: DbEvent["PartitionKey"];
			AggregateType: DbEvent["AggregateType"];
			SortableUniqueId: DbEvent["SortableUniqueId"];
		};
	};

	commands: {
		key: string;
		value: DbCommand;
		indexes: {
			SortableUniqueId: DbCommand["SortableUniqueId"];
			PartitionKey: DbCommand["PartitionKey"];
			AggregateContainerGroup: DbCommand["AggregateContainerGroup"];
		};
	};

	"single-projection-snapshots": {
		key: string;
		value: DbSingleProjectionSnapshot;
		indexes: {
			Id: DbSingleProjectionSnapshot["Id"];
			AggregateContainerGroup: DbSingleProjectionSnapshot["AggregateContainerGroup"];
			PartitionKey: DbSingleProjectionSnapshot["PartitionKey"];
			AggregateId: DbSingleProjectionSnapshot["AggregateId"];
			RootPartitionKey: DbSingleProjectionSnapshot["RootPartitionKey"];
			AggregateType: DbSingleProjectionSnapshot["AggregateType"];
			PayloadVersionIdentifier: DbSingleProjectionSnapshot["PayloadVersionIdentifier"];
			SavedVersion: DbSingleProjectionSnapshot["SavedVersion"];
		};
	};

	"multi-projection-snapshots": {
		key: string;
		value: DbMultiProjectionSnapshot;
		indexes: {
			AggregateContainerGroup: DbMultiProjectionSnapshot["AggregateContainerGroup"];
			PartitionKey: DbMultiProjectionSnapshot["PartitionKey"];
			PayloadVersionIdentifier: DbMultiProjectionSnapshot["PayloadVersionIdentifier"];
		};
	};
}
