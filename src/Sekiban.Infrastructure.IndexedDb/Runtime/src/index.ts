import { type IDBPDatabase, openDB } from "idb";
import type {
	DbCommand,
	DbCommandQuery,
	DbEvent,
	DbEventQuery,
	DbMultiProjectionSnapshot,
	DbMultiProjectionSnapshotQuery,
	DbSingleProjectionSnapshot,
	DbSingleProjectionSnapshotQuery,
} from "./models";
import type { SekibanDb } from "./schema";

const log = (() => {
	if (globalThis?.process?.versions?.node === undefined) {
		return async () => {};
	}

	let seq = 0;

	return async (name: string, value: unknown): Promise<void> => {
		const line = JSON.stringify({
			seq: seq++,
			event: name,
			payload: value ?? null,
		});

		const fs = await import("node:fs/promises");
		await fs.mkdir("/tmp/sekiban/").catch(() => {});
		await fs.writeFile("/tmp/sekiban/runtime.log", `${line}\n`, {
			flag: "a",
		});
	};
})();

// biome-ignore lint/suspicious/noExplicitAny: ignore param/return types for deserialize/serialize
const wrapio = <T extends Record<string, (args: any) => Promise<any>>>(
	operations: T,
	trace?: boolean,
) =>
	Object.fromEntries(
		Object.entries(operations).map(([name, func]) => [
			name,
			async (input: string | null): Promise<string | null> => {
				const args =
					input !== undefined && input !== null ? JSON.parse(input) : null;

				if (trace === true) {
					await log(`${name}:args`, args);
				}

				const result = await func(args);

				if (trace === true) {
					await log(`${name}:result`, result);
				}

				const output =
					result !== undefined && result !== null
						? JSON.stringify(result)
						: null;

				return output;
			},
		]),
	) as Record<keyof T, (input: string | null) => Promise<string | null>>;

const asc =
	<T>(id: (value: T) => string) =>
	(x: T, y: T): number =>
		id(x).localeCompare(id(y));

const desc =
	<T>(id: (value: T) => string) =>
	(x: T, y: T): number =>
		id(y).localeCompare(id(x));

const filterEvents = async (
	idb: IDBPDatabase<SekibanDb>,
	store: "events" | "dissolvable-events",
	query: DbEventQuery,
): Promise<DbEvent[]> => {
	const shard =
		query.PartitionKey !== null
			? await idb.getAllFromIndex(store, "PartitionKey", query.PartitionKey)
			: await idb.getAll(store);

	const items = shard
		.filter(
			(x) =>
				query.RootPartitionKey === null ||
				x.RootPartitionKey === query.RootPartitionKey,
		)
		.filter(
			(x) =>
				query.PartitionKey === null || x.PartitionKey === query.PartitionKey,
		)
		.filter(
			(x) =>
				query.AggregateTypes === null ||
				query.AggregateTypes.includes(x.AggregateType),
		)
		.filter(
			(x) =>
				query.SortableIdStart === null ||
				query.SortableIdStart <= x.SortableUniqueId,
		)
		.filter(
			(x) =>
				query.SortableIdEnd === null ||
				x.SortableUniqueId <= query.SortableIdEnd,
		)
		.toSorted(asc((x) => x.SortableUniqueId));

	return query.MaxCount !== null ? items.slice(0, query.MaxCount) : items;
};

type Store = {
	readonly events: DbEvent[];
	readonly dissolvableEvents: DbEvent[];
	readonly commands: DbCommand[];
	readonly singleProjectionSnapshots: DbSingleProjectionSnapshot[];
	readonly multiProjectionSnapshots: DbMultiProjectionSnapshot[];
};

const operations = (store: Store, idb: IDBPDatabase<SekibanDb>) => {
	const writeEventAsync = async (event: DbEvent): Promise<void> => {
		await idb.add("events", event);
	};

	const getEventsAsync = async (query: DbEventQuery): Promise<DbEvent[]> =>
		await filterEvents(idb, "events", query);

	const removeAllEventsAsync = async (): Promise<void> => {
		await idb.clear("events");
	};

	const writeDissolvableEventAsync = async (event: DbEvent): Promise<void> => {
		await idb.add("dissolvable-events", event);
	};

	const getDissolvableEventsAsync = async (
		query: DbEventQuery,
	): Promise<DbEvent[]> => await filterEvents(idb, "dissolvable-events", query);

	const removeAllDissolvableEventsAsync = async (): Promise<void> => {
		await idb.clear("dissolvable-events");
	};

	const writeCommandAsync = async (command: DbCommand): Promise<void> => {
		await idb.add("commands", command);
	};

	const getCommandsAsync = async (
		query: DbCommandQuery,
	): Promise<DbCommand[]> => {
		const shard =
			query.PartitionKey !== null
				? await idb.getAllFromIndex(
						"commands",
						"PartitionKey",
						query.PartitionKey,
					)
				: await idb.getAll("commands");

		const items = shard
			.filter(
				(x) =>
					query.AggregateContainerGroup === null ||
					x.AggregateContainerGroup === query.AggregateContainerGroup,
			)
			.filter(
				(x) =>
					query.SortableIdStart === null ||
					query.SortableIdStart <= x.SortableUniqueId,
			);

		return items;
	};

	const removeAllCommandsAsync = async (): Promise<void> => {
		await idb.clear("commands");
	};

	const writeSingleProjectionSnapshotAsync = async (
		snapshot: DbSingleProjectionSnapshot,
	): Promise<void> => {
		await idb.add("single-projection-snapshots", snapshot);
	};

	const getSingleProjectionSnapshotsAsync = async (
		query: DbSingleProjectionSnapshotQuery,
	): Promise<DbSingleProjectionSnapshot[]> => {
		if (query.Id !== null) {
			const item = await idb.getFromIndex(
				"single-projection-snapshots",
				"Id",
				query.Id,
			);
			return item !== undefined ? [item] : [];
		}

		const shard =
			query.PartitionKey !== null
				? await idb.getAllFromIndex(
						"single-projection-snapshots",
						"PartitionKey",
						query.PartitionKey,
					)
				: await idb.getAll("single-projection-snapshots");

		const items = shard
			.filter(
				(x) =>
					query.AggregateContainerGroup === null ||
					x.AggregateContainerGroup === query.AggregateContainerGroup,
			)
			.filter(
				(x) =>
					query.AggregateId === null || x.AggregateId === query.AggregateId,
			)
			.filter(
				(x) =>
					query.RootPartitionKey === null ||
					x.RootPartitionKey === query.RootPartitionKey,
			)
			.filter(
				(x) =>
					query.AggregateType === null ||
					x.AggregateType === query.AggregateType,
			)
			.filter(
				(x) =>
					query.PayloadVersionIdentifier === null ||
					x.PayloadVersionIdentifier === query.PayloadVersionIdentifier,
			)
			.filter(
				(x) =>
					query.SavedVersion === null || x.SavedVersion === query.SavedVersion,
			)
			.toSorted(desc((x) => x.LastSortableUniqueId));

		return query.IsLatestOnly ? items.slice(0, 1) : items;
	};

	const removeAllSingleProjectionSnapshotsAsync = async (): Promise<void> => {
		await idb.clear("single-projection-snapshots");
	};

	const writeMultiProjectionSnapshotAsync = async (
		snapshot: DbMultiProjectionSnapshot,
	): Promise<void> => {
		await idb.add("multi-projection-snapshots", snapshot);
	};

	const getMultiProjectionSnapshotsAsync = async (
		query: DbMultiProjectionSnapshotQuery,
	): Promise<DbMultiProjectionSnapshot[]> => {
		const shard =
			query.PartitionKey !== null
				? await idb.getAllFromIndex(
						"multi-projection-snapshots",
						"PartitionKey",
						query.PartitionKey,
					)
				: await idb.getAll("multi-projection-snapshots");

		const items = shard
			.filter(
				(x) =>
					query.AggregateContainerGroup === null ||
					x.AggregateContainerGroup === query.AggregateContainerGroup,
			)
			.filter(
				(x) =>
					query.PayloadVersionIdentifier === null ||
					x.PayloadVersionIdentifier === query.PayloadVersionIdentifier,
			)
			.toSorted(desc((x) => x.LastSortableUniqueId));

		return query.IsLatestOnly ? items.slice(0, 1) : items;
	};

	const removeAllMultiProjectionSnapshotsAsync = async (): Promise<void> => {
		await idb.clear("multi-projection-snapshots");
	};

	return {
		writeEventAsync,
		getEventsAsync,
		removeAllEventsAsync,

		writeDissolvableEventAsync,
		getDissolvableEventsAsync,
		removeAllDissolvableEventsAsync,

		writeCommandAsync,
		getCommandsAsync,
		removeAllCommandsAsync,

		writeSingleProjectionSnapshotAsync,
		getSingleProjectionSnapshotsAsync,
		removeAllSingleProjectionSnapshotsAsync,

		writeMultiProjectionSnapshotAsync,
		getMultiProjectionSnapshotsAsync,
		removeAllMultiProjectionSnapshotsAsync,
	};
};

const memStore = (): Store => ({
	events: [],
	dissolvableEvents: [],
	commands: [],
	singleProjectionSnapshots: [],
	multiProjectionSnapshots: [],
});

const stores = new Map<
	string,
	{
		mem: Store;
		idb: IDBPDatabase<SekibanDb>;
	}
>();

const idbStore = async (
	contextName: string,
): Promise<IDBPDatabase<SekibanDb>> =>
	openDB<SekibanDb>(contextName, 1, {
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

export const init = async (contextName: string) => {
	let store = stores.get(contextName);

	if (store === undefined) {
		store = {
			mem: memStore(),
			idb: await idbStore(contextName),
		};
	}

	stores.set(contextName, store);
	return wrapio(operations(store.mem, store.idb), true);
};
