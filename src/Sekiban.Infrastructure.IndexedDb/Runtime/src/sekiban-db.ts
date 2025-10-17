import { type DBSchema, type IDBPDatabase, openDB } from "idb";
import type {
	DbBlob,
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
			TimeStamp: DbEvent["TimeStamp"];
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
			TimeStamp: DbEvent["TimeStamp"];
			SortableUniqueId: DbEvent["SortableUniqueId"];
		};
	};

	commands: {
		key: string;
		value: DbCommand;
		indexes: {
			SortableUniqueId: DbCommand["SortableUniqueId"];
			PartitionKey: DbCommand["PartitionKey"];
			TimeStamp: DbCommand["TimeStamp"];
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
			TimeStamp: DbSingleProjectionSnapshot["TimeStamp"];
			PayloadVersionIdentifier: DbSingleProjectionSnapshot["PayloadVersionIdentifier"];
			SavedVersion: DbSingleProjectionSnapshot["SavedVersion"];
			LastSortableUniqueId: DbSingleProjectionSnapshot["LastSortableUniqueId"];
		};
	};

	"multi-projection-snapshots": {
		key: string;
		value: DbMultiProjectionSnapshot;
		indexes: {
			AggregateContainerGroup: DbMultiProjectionSnapshot["AggregateContainerGroup"];
			PartitionKey: DbMultiProjectionSnapshot["PartitionKey"];
			TimeStamp: DbMultiProjectionSnapshot["TimeStamp"];
			PayloadVersionIdentifier: DbMultiProjectionSnapshot["PayloadVersionIdentifier"];
			LastSortableUniqueId: DbMultiProjectionSnapshot["LastSortableUniqueId"];
		};
	};

	"single-projection-state-blobs": {
		key: string;
		value: DbBlob;
		indexes: {
			Name: DbBlob["Name"];
		};
	};

	"multi-projection-state-blobs": {
		key: string;
		value: DbBlob;
		indexes: {
			Name: DbBlob["Name"];
		};
	};

	"multi-projection-events-blobs": {
		key: string;
		value: DbBlob;
		indexes: {
			Name: DbBlob["Name"];
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
			events.createIndex("TimeStamp", "TimeStamp");
			events.createIndex("SortableUniqueId", "SortableUniqueId");

			const dissolvableEvents = db.createObjectStore("dissolvable-events", {
				keyPath: "Id",
			});
			dissolvableEvents.createIndex("RootPartitionKey", "RootPartitionKey");
			dissolvableEvents.createIndex("PartitionKey", "PartitionKey");
			dissolvableEvents.createIndex("AggregateType", "AggregateType");
			dissolvableEvents.createIndex("TimeStamp", "TimeStamp");
			dissolvableEvents.createIndex("SortableUniqueId", "SortableUniqueId");

			const commands = db.createObjectStore("commands", {
				keyPath: "Id",
			});
			commands.createIndex("SortableUniqueId", "SortableUniqueId");
			commands.createIndex("PartitionKey", "PartitionKey");
			commands.createIndex("TimeStamp", "TimeStamp");
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
			singleProjectionSnapshots.createIndex("TimeStamp", "TimeStamp");
			singleProjectionSnapshots.createIndex("AggregateType", "AggregateType");
			singleProjectionSnapshots.createIndex(
				"PayloadVersionIdentifier",
				"PayloadVersionIdentifier",
			);
			singleProjectionSnapshots.createIndex("SavedVersion", "SavedVersion");
			singleProjectionSnapshots.createIndex(
				"LastSortableUniqueId",
				"LastSortableUniqueId",
			);

			const multiProjectionSnapshots = db.createObjectStore(
				"multi-projection-snapshots",
				{ keyPath: "Id" },
			);
			multiProjectionSnapshots.createIndex(
				"AggregateContainerGroup",
				"AggregateContainerGroup",
			);
			multiProjectionSnapshots.createIndex("TimeStamp", "TimeStamp");
			multiProjectionSnapshots.createIndex("PartitionKey", "PartitionKey");
			multiProjectionSnapshots.createIndex(
				"PayloadVersionIdentifier",
				"PayloadVersionIdentifier",
			);
			multiProjectionSnapshots.createIndex(
				"LastSortableUniqueId",
				"LastSortableUniqueId",
			);

			const singleProjectionStateBlobs = db.createObjectStore(
				"single-projection-state-blobs",
				{
					keyPath: "Id",
				},
			);
			singleProjectionStateBlobs.createIndex("Name", "Name");

			const multiProjectionStateBlobs = db.createObjectStore(
				"multi-projection-state-blobs",
				{
					keyPath: "Id",
				},
			);
			multiProjectionStateBlobs.createIndex("Name", "Name");

			const multiProjectionEventsBlobs = db.createObjectStore(
				"multi-projection-events-blobs",
				{
					keyPath: "Id",
				},
			);
			multiProjectionEventsBlobs.createIndex("Name", "Name");
		},
	});
