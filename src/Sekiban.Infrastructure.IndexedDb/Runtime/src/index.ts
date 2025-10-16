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
import { type SekibanDb, connect } from "./sekiban-db";

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

const eventMatchesQuery = (event: DbEvent, query: DbEventQuery): boolean =>
	(query.RootPartitionKey === null ||
		event.RootPartitionKey === query.RootPartitionKey) &&
	(query.PartitionKey === null ||
		event.PartitionKey === query.PartitionKey) &&
	(query.AggregateTypes === null ||
		query.AggregateTypes.includes(event.AggregateType));

const filterEvents = async (
	idb: SekibanDb,
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

// Get a single chunk of events
// Returns empty array when no more events are available
const filterEventsChunk = async (
	idb: SekibanDb,
	store: "events" | "dissolvable-events",
	query: DbEventQuery,
	chunkSize: number,
	_skip: number,
): Promise<DbEvent[]> => {
	const limit =
		query.MaxCount !== null ? Math.min(chunkSize, query.MaxCount) : chunkSize;

	if (limit <= 0) {
		return [];
	}

	const tx = idb.transaction(store, "readonly");
	const index = tx.store.index("SortableUniqueId");
	const range =
		query.SortableIdStart !== null && query.SortableIdEnd !== null
			? IDBKeyRange.bound(
					query.SortableIdStart,
					query.SortableIdEnd,
					true,
					true,
				)
			: query.SortableIdStart !== null
				? IDBKeyRange.lowerBound(query.SortableIdStart, true)
				: query.SortableIdEnd !== null
					? IDBKeyRange.upperBound(query.SortableIdEnd, true)
					: undefined;

	const results: DbEvent[] = [];

	let cursor = await index.openCursor(range);
	while (cursor !== null) {
		const current = cursor.value;

		if (!eventMatchesQuery(current, query)) {
			cursor = await cursor.continue();
			continue;
		}

		results.push(current);

		if (results.length >= limit) {
			break;
		}

		cursor = await cursor.continue();
	}

	await tx.done;

	return results;
};
const filterBlobs = async (
	idb: SekibanDb,
	store:
		| "single-projection-state-blobs"
		| "multi-projection-state-blobs"
		| "multi-projection-events-blobs",
	query: DbBlobQuery,
): Promise<DbBlob[]> => {
	const shard =
		query.Name !== null
			? await idb.getAllFromIndex(store, "Name", query.Name)
			: await idb.getAll(store);

	const items = shard.toSorted(asc((x) => x.Id));

	return query.MaxCount !== null ? items.slice(0, query.MaxCount) : items;
};

const operations = (idb: SekibanDb) => {
	const writeEventAsync = async (event: DbEvent): Promise<void> => {
		await idb.add("events", event);
	};

	const getEventsAsync = async (query: DbEventQuery): Promise<DbEvent[]> =>
		await filterEvents(idb, "events", query);

		// Get single chunk by advancing SortableUniqueId to avoid loading all events
	const getEventsAsyncChunk = async (params: {
		query: DbEventQuery;
		chunkSize: number;
		skip: number;
	}): Promise<DbEvent[]> =>
		await filterEventsChunk(
			idb,
			"events",
			params.query,
			params.chunkSize,
			params.skip,
		);

	const removeAllEventsAsync = async (): Promise<void> => {
		await idb.clear("events");
	};

	const writeDissolvableEventAsync = async (event: DbEvent): Promise<void> => {
		await idb.add("dissolvable-events", event);
	};

	const getDissolvableEventsAsync = async (
		query: DbEventQuery,
	): Promise<DbEvent[]> => await filterEvents(idb, "dissolvable-events", query);

		// Same chunking logic for dissolvable events
	const getDissolvableEventsAsyncChunk = async (params: {
		query: DbEventQuery;
		chunkSize: number;
		skip: number;
	}): Promise<DbEvent[]> =>
		await filterEventsChunk(
			idb,
			"dissolvable-events",
			params.query,
			params.chunkSize,
			params.skip,
		);

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
		getEventsAsyncChunk,
		removeAllEventsAsync,

		writeDissolvableEventAsync,
		getDissolvableEventsAsync,
		getDissolvableEventsAsyncChunk,
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
