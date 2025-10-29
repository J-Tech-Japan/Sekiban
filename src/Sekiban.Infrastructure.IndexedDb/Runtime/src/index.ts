import type {
	IDBPCursorWithValue,
	IDBPTransaction,
	StoreNames,
	StoreValue,
} from "idb";
import type {
	DbBlob,
	DbBlobQuery,
	DbCommand,
	DbCommandQuery,
	DbEvent,
	DbEventQuery,
	DbMultiProjectionSnapshot,
	DbMultiProjectionSnapshotQuery,
	DbSingleProjectionSnapshot,
	DbSingleProjectionSnapshotQuery,
} from "./models";
import { connect, type SekibanDb, type SekibanDbSchema } from "./sekiban-db";

// biome-ignore lint/suspicious/noExplicitAny: ignore param/return types for deserialize/serialize
const wrapio = <T extends Record<string, (args: any) => Promise<any>>>(
	operations: T,
) =>
	Object.fromEntries(
		Object.entries(operations).map(([name, func]) => [
			name,
			async (input: string | null): Promise<string | null> => {
				const args =
					input !== undefined && input !== null ? JSON.parse(input) : null;

				const result = await func(args);

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

const filterStore = async <StoreName extends StoreNames<SekibanDbSchema>>({
	idb,
	store,
	openCursor,
	maxCount,
	filter,
	orderBy,
}: {
	idb: SekibanDb;
	store: StoreName;
	openCursor: (
		tx: IDBPTransaction<SekibanDbSchema, [StoreName], "readonly">,
	) => Promise<IDBPCursorWithValue<
		SekibanDbSchema,
		[StoreName],
		StoreName,
		unknown,
		"readonly"
	> | null>;
	maxCount: number | null;
	filter: (x: StoreValue<SekibanDbSchema, StoreName>) => boolean;
	orderBy: (
		x: StoreValue<SekibanDbSchema, StoreName>,
		y: StoreValue<SekibanDbSchema, StoreName>,
	) => number;
}): Promise<StoreValue<SekibanDbSchema, StoreName>[]> => {
	const tx = idb.transaction(store, "readonly");
	let cursor = await openCursor(tx);
	if (cursor === null) {
		return [];
	}

	const items: StoreValue<SekibanDbSchema, StoreName>[] = [];
	maxCount ??= Number.MAX_SAFE_INTEGER;

	while (cursor !== null && items.length < maxCount) {
		if (filter(cursor.value)) {
			items.push(cursor.value);
		}

		cursor = await cursor.continue();
	}

	items.sort(orderBy);
	return items;
};

const filterEvents = async (
	idb: SekibanDb,
	store: "events" | "dissolvable-events",
	query: DbEventQuery,
): Promise<DbEvent[]> =>
	await filterStore({
		idb,
		store,

		openCursor: (tx) => {
			if (query.SortableIdStart !== null && query.SortableIdEnd !== null) {
				const range = IDBKeyRange.bound(
					query.SortableIdStart,
					query.SortableIdEnd,
					true,
					true,
				);
				return tx.store.index("SortableUniqueId").openCursor(range);
			}

			if (query.SortableIdStart !== null) {
				const range = IDBKeyRange.lowerBound(query.SortableIdStart, true);
				return tx.store.index("SortableUniqueId").openCursor(range);
			}

			if (query.SortableIdEnd !== null) {
				const range = IDBKeyRange.upperBound(query.SortableIdEnd, true);
				return tx.store.index("SortableUniqueId").openCursor(range);
			}

			if (query.PartitionKey !== null) {
				const range = IDBKeyRange.only(query.PartitionKey);
				return tx.store.index("PartitionKey").openCursor(range);
			}

			if (query.RootPartitionKey !== null) {
				const range = IDBKeyRange.only(query.RootPartitionKey);
				return tx.store.index("RootPartitionKey").openCursor(range);
			}

			return tx.store.openCursor();
		},

		maxCount: query.MaxCount,

		filter: (x) =>
			(query.SortableIdStart === null ||
				query.SortableIdStart < x.SortableUniqueId) &&
			//
			(query.SortableIdEnd === null ||
				x.SortableUniqueId < query.SortableIdEnd) &&
			//
			(query.PartitionKey === null || x.PartitionKey === query.PartitionKey) &&
			//
			(query.AggregateTypes === null ||
				query.AggregateTypes.includes(x.AggregateType)) &&
			//
			(query.RootPartitionKey === null ||
				x.RootPartitionKey === query.RootPartitionKey) &&
			//
			true,

		orderBy: asc((x) => x.SortableUniqueId),
	});

const filterBlobs = async (
	idb: SekibanDb,
	store:
		| "single-projection-state-blobs"
		| "multi-projection-state-blobs"
		| "multi-projection-events-blobs",
	query: DbBlobQuery,
): Promise<DbBlob[]> =>
	await filterStore({
		idb,
		store,

		openCursor: (tx) => {
			if (query.Name !== null) {
				const range = IDBKeyRange.only(query.Name);
				return tx.store.index("Name").openCursor(range);
			}

			return tx.store.openCursor();
		},

		maxCount: query.MaxCount,

		filter: (x) => query.Name === null || x.Name === query.Name,

		orderBy: asc((x) => x.Id),
	});

const operations = (idb: SekibanDb) => {
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
	): Promise<DbCommand[]> =>
		await filterStore({
			idb,
			store: "commands",

			openCursor: (tx) => {
				if (query.SortableIdStart != null) {
					const range = IDBKeyRange.lowerBound(query.SortableIdStart, true);
					return tx.store.index("SortableUniqueId").openCursor(range);
				}

				if (query.PartitionKey !== null) {
					const range = IDBKeyRange.only(query.PartitionKey);
					return tx.store.index("PartitionKey").openCursor(range);
				}

				if (query.AggregateContainerGroup !== null) {
					const range = IDBKeyRange.lowerBound(
						query.AggregateContainerGroup,
						true,
					);
					return tx.store.index("AggregateContainerGroup").openCursor(range);
				}

				return tx.store.openCursor();
			},

			maxCount: null,

			filter: (x) =>
				(query.AggregateContainerGroup === null ||
					x.AggregateContainerGroup === query.AggregateContainerGroup) &&
				//
				(query.SortableIdStart === null ||
					query.SortableIdStart < x.SortableUniqueId) &&
				//
				true,

			orderBy: asc((x) => x.SortableUniqueId),
		});

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

		return await filterStore({
			idb,
			store: "single-projection-snapshots",

			openCursor: (tx) => {
				if (query.AggregateId !== null) {
					const key = IDBKeyRange.only(query.AggregateId);
					return tx.store.index("AggregateId").openCursor(key);
				}

				if (query.AggregateContainerGroup !== null) {
					const key = IDBKeyRange.only(query.AggregateContainerGroup);
					return tx.store.index("AggregateContainerGroup").openCursor(key);
				}

				if (query.PartitionKey !== null) {
					const key = IDBKeyRange.only(query.PartitionKey);
					return tx.store.index("PartitionKey").openCursor(key);
				}

				if (query.RootPartitionKey !== null) {
					const key = IDBKeyRange.only(query.RootPartitionKey);
					return tx.store.index("RootPartitionKey").openCursor(key);
				}

				if (query.AggregateType !== null) {
					const key = IDBKeyRange.only(query.AggregateType);
					return tx.store.index("AggregateType").openCursor(key);
				}

				if (query.PayloadVersionIdentifier !== null) {
					const key = IDBKeyRange.only(query.PayloadVersionIdentifier);
					return tx.store.index("PayloadVersionIdentifier").openCursor(key);
				}

				if (query.SavedVersion !== null) {
					const key = IDBKeyRange.only(query.SavedVersion);
					return tx.store.index("SavedVersion").openCursor(key);
				}

				return tx.store.openCursor();
			},

			maxCount: query.IsLatestOnly ? 1 : null,

			filter: (x) =>
				(query.AggregateContainerGroup === null ||
					x.AggregateContainerGroup === query.AggregateContainerGroup) &&
				//
				(query.AggregateId === null || x.AggregateId === query.AggregateId) &&
				//
				(query.RootPartitionKey === null ||
					x.RootPartitionKey === query.RootPartitionKey) &&
				//
				(query.AggregateType === null ||
					x.AggregateType === query.AggregateType) &&
				//
				(query.PayloadVersionIdentifier === null ||
					x.PayloadVersionIdentifier === query.PayloadVersionIdentifier) &&
				//
				(query.SavedVersion === null ||
					x.SavedVersion === query.SavedVersion) &&
				//
				true,

			orderBy: desc((x) => x.LastSortableUniqueId),
		});
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
	): Promise<DbMultiProjectionSnapshot[]> =>
		await filterStore({
			idb,
			store: "multi-projection-snapshots",

			openCursor: (tx) => {
				if (query.PartitionKey !== null) {
					const key = IDBKeyRange.only(query.PartitionKey);
					return tx.store.index("PartitionKey").openCursor(key);
				}

				if (query.AggregateContainerGroup !== null) {
					const key = IDBKeyRange.only(query.AggregateContainerGroup);
					return tx.store.index("AggregateContainerGroup").openCursor(key);
				}

				if (query.PayloadVersionIdentifier !== null) {
					const key = IDBKeyRange.only(query.PayloadVersionIdentifier);
					return tx.store.index("PayloadVersionIdentifier").openCursor(key);
				}

				return tx.store.openCursor();
			},

			maxCount: query.IsLatestOnly ? 1 : null,

			filter: (x) =>
				(query.AggregateContainerGroup === null ||
					x.AggregateContainerGroup === query.AggregateContainerGroup) &&
				(query.PayloadVersionIdentifier === null ||
					x.PayloadVersionIdentifier === query.PayloadVersionIdentifier) &&
				true,

			orderBy: desc((x) => x.LastSortableUniqueId),
		});

	const removeAllMultiProjectionSnapshotsAsync = async (): Promise<void> => {
		await idb.clear("multi-projection-snapshots");
	};

	const writeSingleProjectionStateBlobAsync = async (
		blob: DbBlob,
	): Promise<void> => {
		await idb.add("single-projection-state-blobs", blob);
	};

	const getSingleProjectionStateBlobsAsync = async (
		query: DbBlobQuery,
	): Promise<DbBlob[]> =>
		await filterBlobs(idb, "single-projection-state-blobs", query);

	const writeMultiProjectionStateBlobAsync = async (
		blob: DbBlob,
	): Promise<void> => {
		await idb.add("multi-projection-state-blobs", blob);
	};

	const getMultiProjectionStateBlobsAsync = async (
		query: DbBlobQuery,
	): Promise<DbBlob[]> =>
		await filterBlobs(idb, "multi-projection-state-blobs", query);

	const writeMultiProjectionEventsBlobAsync = async (
		blob: DbBlob,
	): Promise<void> => {
		await idb.add("multi-projection-events-blobs", blob);
	};

	const getMultiProjectionEventsBlobsAsync = async (
		query: DbBlobQuery,
	): Promise<DbBlob[]> =>
		await filterBlobs(idb, "multi-projection-events-blobs", query);

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

		writeSingleProjectionStateBlobAsync,
		getSingleProjectionStateBlobsAsync,

		writeMultiProjectionStateBlobAsync,
		getMultiProjectionStateBlobsAsync,

		writeMultiProjectionEventsBlobAsync,
		getMultiProjectionEventsBlobsAsync,
	};
};

const contexts = new Map<string, SekibanDb>();

export const init = async (contextName: string) => {
	let context = contexts.get(contextName);

	if (context === undefined) {
		context = await connect(contextName);
	}

	contexts.set(contextName, context);
	return wrapio(operations(context));
};
