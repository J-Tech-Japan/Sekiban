import { type DBSchema, type IDBPDatabase, openDB } from "idb";
import type {
	DbCommand,
	DbEvent,
	DbMultiProjectionSnapshot,
	DbSingleProjectionSnapshot,
} from "./models";

interface SekibanDbSchema extends DBSchema {
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

export type SekibanDb = IDBPDatabase<SekibanDbSchema>;

export const connect = async (contextName: string): Promise<SekibanDb> =>
	openDB<SekibanDbSchema>(contextName, 1, {
		blocked: (current, blocked) => {
			throw new Error(`upgrade blocked (${current} -> ${blocked})`);
		},
		upgrade: (db) => {
			const events = db.createObjectStore("events", {
				keyPath: "Id",
			});
			events.createIndex("RootPartitionKey", "RootPartitionKey");
			events.createIndex("PartitionKey", "PartitionKey");
			events.createIndex("AggregateType", "AggregateType");
			events.createIndex("SortableUniqueId", "SortableUniqueId");

			const dissolvableEvents = db.createObjectStore("dissolvable-events", {
				keyPath: "Id",
			});
			dissolvableEvents.createIndex("RootPartitionKey", "RootPartitionKey");
			dissolvableEvents.createIndex("PartitionKey", "PartitionKey");
			dissolvableEvents.createIndex("AggregateType", "AggregateType");
			dissolvableEvents.createIndex("SortableUniqueId", "SortableUniqueId");

			const commands = db.createObjectStore("commands", {
				keyPath: "Id",
			});
			commands.createIndex("SortableUniqueId", "SortableUniqueId");
			commands.createIndex("PartitionKey", "PartitionKey");
			commands.createIndex(
				"AggregateContainerGroup",
				"AggregateContainerGroup",
			);

			const singleProjectionSnapshots = db.createObjectStore(
				"single-projection-snapshots",
				{
					keyPath: "Id",
				},
			);
			singleProjectionSnapshots.createIndex("Id", "Id");
			singleProjectionSnapshots.createIndex(
				"AggregateContainerGroup",
				"AggregateContainerGroup",
			);
			singleProjectionSnapshots.createIndex("PartitionKey", "PartitionKey");
			singleProjectionSnapshots.createIndex("AggregateId", "AggregateId");
			singleProjectionSnapshots.createIndex(
				"RootPartitionKey",
				"RootPartitionKey",
			);
			singleProjectionSnapshots.createIndex("AggregateType", "AggregateType");
			singleProjectionSnapshots.createIndex(
				"PayloadVersionIdentifier",
				"PayloadVersionIdentifier",
			);
			singleProjectionSnapshots.createIndex("SavedVersion", "SavedVersion");

			const multiProjectionSnapshots = db.createObjectStore(
				"multi-projection-snapshots",
				{ keyPath: "Id" },
			);
			multiProjectionSnapshots.createIndex(
				"AggregateContainerGroup",
				"AggregateContainerGroup",
			);
			multiProjectionSnapshots.createIndex("PartitionKey", "PartitionKey");
			multiProjectionSnapshots.createIndex(
				"PayloadVersionIdentifier",
				"PayloadVersionIdentifier",
			);
		},
	});
