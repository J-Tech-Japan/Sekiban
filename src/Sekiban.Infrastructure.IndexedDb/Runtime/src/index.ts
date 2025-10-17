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
import { connect, type SekibanDb } from "./sekiban-db";

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

const filterEvents = async (
	idb: SekibanDb,
	store: "events" | "dissolvable-events",
	query: DbEventQuery,
): Promise<DbEvent[]> => {
	const shard: DbEvent[] = await (() => {
		if (query.SortableIdStart !== null && query.SortableIdEnd !== null) {
			const key = IDBKeyRange.bound(
				query.SortableIdStart,
				query.SortableIdEnd,
				true,
				true,
			);
			return idb.getAllFromIndex(store, "SortableUniqueId", key);
		}

		if (query.SortableIdStart !== null) {
			const key = IDBKeyRange.lowerBound(query.SortableIdStart, true);
			return idb.getAllFromIndex(store, "SortableUniqueId", key);
		}

		if (query.SortableIdEnd !== null) {
			const key = IDBKeyRange.upperBound(query.SortableIdEnd, true);
			return idb.getAllFromIndex(store, "SortableUniqueId", key);
		}

		if (query.PartitionKey !== null) {
			const key = IDBKeyRange.only(query.PartitionKey);
			return idb.getAllFromIndex(store, "PartitionKey", key);
		}

		if (query.RootPartitionKey !== null) {
			const key = IDBKeyRange.only(query.RootPartitionKey);
			return idb.getAllFromIndex(store, "RootPartitionKey", key);
		}

		return idb.getAll(store);
	})();

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
				query.SortableIdStart < x.SortableUniqueId,
		)
		.filter(
			(x) =>
				query.SortableIdEnd === null ||
				x.SortableUniqueId < query.SortableIdEnd,
		)
		.toSorted(asc((x) => x.SortableUniqueId));

	return query.MaxCount !== null ? items.slice(0, query.MaxCount) : items;
};

const filterBlobs = async (
	idb: SekibanDb,
	store:
		| "single-projection-state-blobs"
		| "multi-projection-state-blobs"
		| "multi-projection-events-blobs",
	query: DbBlobQuery,
): Promise<DbBlob[]> => {
	const shard: DbBlob[] = await (() => {
		if (query.Name !== null) {
			const key = IDBKeyRange.only(query.Name);
			return idb.getAllFromIndex(store, "Name", key);
		}

		return idb.getAll(store);
	})();

	const items = shard.toSorted(asc((x) => x.Id));

	return query.MaxCount !== null ? items.slice(0, query.MaxCount) : items;
};

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
	): Promise<DbCommand[]> => {
		const shard: DbCommand[] = await (() => {
			if (query.SortableIdStart != null) {
				const key = IDBKeyRange.lowerBound(query.SortableIdStart, true);
				return idb.getAllFromIndex("commands", "SortableUniqueId", key);
			}

			if (query.PartitionKey !== null) {
				const key = IDBKeyRange.only(query.PartitionKey);
				return idb.getAllFromIndex("commands", "PartitionKey", key);
			}

			if (query.AggregateContainerGroup !== null) {
				const key = IDBKeyRange.lowerBound(query.AggregateContainerGroup, true);
				return idb.getAllFromIndex("commands", "AggregateContainerGroup", key);
			}

			return idb.getAll("commands");
		})();

		const items = shard
			.filter(
				(x) =>
					query.AggregateContainerGroup === null ||
					x.AggregateContainerGroup === query.AggregateContainerGroup,
			)
			.filter(
				(x) =>
					query.SortableIdStart === null ||
					query.SortableIdStart < x.SortableUniqueId,
			)
			.toSorted(asc((x) => x.SortableUniqueId));

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

		const shard: DbSingleProjectionSnapshot[] = await (() => {
			if (query.AggregateId !== null) {
				const key = IDBKeyRange.only(query.AggregateId);
				return idb.getAllFromIndex(
					"single-projection-snapshots",
					"AggregateId",
					key,
				);
			}

			if (query.AggregateContainerGroup !== null) {
				const key = IDBKeyRange.only(query.AggregateContainerGroup);
				return idb.getAllFromIndex(
					"single-projection-snapshots",
					"AggregateContainerGroup",
					key,
				);
			}

			if (query.PartitionKey !== null) {
				const key = IDBKeyRange.only(query.PartitionKey);
				return idb.getAllFromIndex(
					"single-projection-snapshots",
					"PartitionKey",
					key,
				);
			}

			if (query.RootPartitionKey !== null) {
				const key = IDBKeyRange.only(query.RootPartitionKey);
				return idb.getAllFromIndex(
					"single-projection-snapshots",
					"RootPartitionKey",
					key,
				);
			}

			if (query.AggregateType !== null) {
				const key = IDBKeyRange.only(query.AggregateType);
				return idb.getAllFromIndex(
					"single-projection-snapshots",
					"AggregateType",
					key,
				);
			}

			if (query.PayloadVersionIdentifier !== null) {
				const key = IDBKeyRange.only(query.PayloadVersionIdentifier);
				return idb.getAllFromIndex(
					"single-projection-snapshots",
					"PayloadVersionIdentifier",
					key,
				);
			}

			if (query.SavedVersion !== null) {
				const key = IDBKeyRange.only(query.SavedVersion);
				return idb.getAllFromIndex(
					"single-projection-snapshots",
					"SavedVersion",
					key,
				);
			}

			return idb.getAll("single-projection-snapshots");
		})();

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
		const shard: DbMultiProjectionSnapshot[] = await (() => {
			if (query.PartitionKey !== null) {
				const key = IDBKeyRange.only(query.PartitionKey);
				return idb.getAllFromIndex(
					"multi-projection-snapshots",
					"PartitionKey",
					key,
				);
			}

			if (query.AggregateContainerGroup !== null) {
				const key = IDBKeyRange.only(query.AggregateContainerGroup);
				return idb.getAllFromIndex(
					"multi-projection-snapshots",
					"AggregateContainerGroup",
					key,
				);
			}

			if (query.PayloadVersionIdentifier !== null) {
				const key = IDBKeyRange.only(query.PayloadVersionIdentifier);
				return idb.getAllFromIndex(
					"multi-projection-snapshots",
					"PayloadVersionIdentifier",
					key,
				);
			}

			return idb.getAll("multi-projection-snapshots");
		})();

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
