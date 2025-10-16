/**
 * IndexedDB パフォーマンステストスイート
 *
 * テストシナリオ:
 * 1. 小規模データ (100件)
 * 2. 中規模データ (10,000件)
 * 3. 大規模データ (100,000件)
 * 4. 極端条件 (全件取得、範囲指定、複雑フィルタ)
 *
 * 比較対象:
 * - 従来型 (getAll + filter)
 * - Optimized (getAll + limit + cursor)
 * - Cached (差分取得)
 * - Streaming (AsyncIterator)
 */

import type { DbEvent, DbEventQuery } from "./models";
import type { SekibanDb } from "./sekiban-db";
import {
	filterEventsChunkOptimized,
	filterEventsChunkCached,
	streamEvents,
	clearQueryCache,
	measurePerformance,
	type PerformanceMetrics,
} from "./optimized-queries";

// ==========================================
// テストデータ生成
// ==========================================

/**
 * テスト用イベント生成
 */
export const generateTestEvents = (count: number): DbEvent[] => {
	const events: DbEvent[] = [];
	const baseDate = new Date("2025-01-01T00:00:00Z").getTime();

	for (let i = 0; i < count; i++) {
		const timestamp = new Date(baseDate + i * 1000).toISOString();
		const sortableId = `${baseDate + i * 1000}-${i.toString().padStart(10, "0")}`;

		events.push({
			Id: `event-${i}`,
			Payload: JSON.stringify({ data: `Event ${i}` }),
			Version: 1,
			CallHistories: "[]",
			AggregateId: `agg-${i % 100}`, // 100種類の Aggregate
			PartitionKey: `partition-${i % 10}`, // 10種類の Partition
			DocumentType: "Event",
			DocumentTypeName: "TestEvent",
			TimeStamp: timestamp,
			SortableUniqueId: sortableId,
			AggregateType: `Type-${i % 5}`, // 5種類の AggregateType
			RootPartitionKey: "test-root",
		});
	}

	return events;
};

/**
 * テストデータを IndexedDB に投入
 */
export const seedTestData = async (
	idb: SekibanDb,
	store: "events" | "dissolvable-events",
	count: number,
): Promise<void> => {
	const events = generateTestEvents(count);

	console.log(`Seeding ${count} events to ${store}...`);

	const tx = idb.transaction(store, "readwrite");

	for (const event of events) {
		await tx.store.add(event);
	}

	await tx.done;

	console.log(`✓ Seeded ${count} events`);
};

// ==========================================
// 従来型実装 (比較用)
// ==========================================

/**
 * 従来型 filterEvents (全件取得)
 *
 * 問題: getAll() で全件メモリ展開
 */
const filterEventsLegacy = async (
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
		.toSorted((a, b) => a.SortableUniqueId.localeCompare(b.SortableUniqueId));

	return query.MaxCount !== null ? items.slice(0, query.MaxCount) : items;
};

/**
 * 従来型 filterEventsChunk (カーソルのみ)
 */
const filterEventsChunkLegacy = async (
	idb: SekibanDb,
	store: "events" | "dissolvable-events",
	query: DbEventQuery,
	chunkSize: number,
): Promise<DbEvent[]> => {
	const limit =
		query.MaxCount !== null ? Math.min(chunkSize, query.MaxCount) : chunkSize;

	if (limit <= 0) {
		return [];
	}

	const tx = idb.transaction(store, "readonly");
	const index = tx.store.index("SortableUniqueId");
	const range =
		query.SortableIdStart !== null
			? IDBKeyRange.lowerBound(query.SortableIdStart, true)
			: undefined;

	const results: DbEvent[] = [];

	let cursor = await index.openCursor(range);
	while (cursor !== null) {
		const current = cursor.value;

		// マッチング判定
		if (
			(query.RootPartitionKey === null ||
				current.RootPartitionKey === query.RootPartitionKey) &&
			(query.PartitionKey === null ||
				current.PartitionKey === query.PartitionKey) &&
			(query.AggregateTypes === null ||
				query.AggregateTypes.includes(current.AggregateType))
		) {
			results.push(current);
		}

		if (results.length >= limit) {
			break;
		}

		cursor = await cursor.continue();
	}

	await tx.done;

	return results;
};

// ==========================================
// テストシナリオ実行
// ==========================================

export interface TestScenarioResult {
	scenario: string;
	dataSize: number;
	legacy?: PerformanceMetrics;
	legacyChunk?: PerformanceMetrics;
	optimized: PerformanceMetrics;
	cached: PerformanceMetrics;
	streaming: PerformanceMetrics;
}

/**
 * シナリオ1: 小規模データ (100件) - 全件取得
 */
export const testSmallDataFullScan = async (
	idb: SekibanDb,
	store: "events" | "dissolvable-events",
): Promise<TestScenarioResult> => {
	await seedTestData(idb, store, 100);

	const query: DbEventQuery = {
		RootPartitionKey: "test-root",
		PartitionKey: null,
		AggregateTypes: null,
		SortableIdStart: null,
		SortableIdEnd: null,
		MaxCount: null,
	};

	const legacy = await measurePerformance(() =>
		filterEventsLegacy(idb, store, query)
	);
	const legacyChunk = await measurePerformance(() =>
		filterEventsChunkLegacy(idb, store, query, 100)
	);
	const optimized = await measurePerformance(() =>
		filterEventsChunkOptimized(idb, store, query, 100, 0)
	);

	clearQueryCache();
	await filterEventsChunkCached(idb, store, query, 100, 0);
	const cached = await measurePerformance(() =>
		filterEventsChunkCached(idb, store, query, 100, 0)
	);

	const streamResults: DbEvent[] = [];
	const streamStart = performance.now();
	for await (const chunk of streamEvents(idb, store, query, 100)) {
		streamResults.push(...chunk);
	}
	const streamEnd = performance.now();
	const streaming: PerformanceMetrics = {
		duration: streamEnd - streamStart,
		itemCount: streamResults.length,
		throughput: streamResults.length / ((streamEnd - streamStart) / 1000),
	};

	return {
		scenario: "Small Data Full Scan (100 events)",
		dataSize: 100,
		legacy,
		legacyChunk,
		optimized,
		cached,
		streaming,
	};
};

/**
 * シナリオ2: 中規模データ (10,000件) - チャンク取得
 */
export const testMediumDataChunked = async (
	idb: SekibanDb,
	store: "events" | "dissolvable-events",
): Promise<TestScenarioResult> => {
	await seedTestData(idb, store, 10000);

	const query: DbEventQuery = {
		RootPartitionKey: "test-root",
		PartitionKey: null,
		AggregateTypes: null,
		SortableIdStart: null,
		SortableIdEnd: null,
		MaxCount: 1000,
	};

	const legacyChunk = await measurePerformance(() =>
		filterEventsChunkLegacy(idb, store, query, 1000)
	);
	const optimized = await measurePerformance(() =>
		filterEventsChunkOptimized(idb, store, query, 1000, 0)
	);

	clearQueryCache();
	await filterEventsChunkCached(idb, store, query, 1000, 0);
	const cached = await measurePerformance(() =>
		filterEventsChunkCached(idb, store, query, 1000, 0)
	);

	const streamResults: DbEvent[] = [];
	const streamStart = performance.now();
	for await (const chunk of streamEvents(idb, store, query, 1000)) {
		streamResults.push(...chunk);
	}
	const streamEnd = performance.now();
	const streaming: PerformanceMetrics = {
		duration: streamEnd - streamStart,
		itemCount: streamResults.length,
		throughput: streamResults.length / ((streamEnd - streamStart) / 1000),
	};

	return {
		scenario: "Medium Data Chunked (10,000 events, 1000/chunk)",
		dataSize: 10000,
		legacyChunk,
		optimized,
		cached,
		streaming,
	};
};

/**
 * シナリオ3: 大規模データ (100,000件) - 範囲指定取得
 */
export const testLargeDataRangeScan = async (
	idb: SekibanDb,
	store: "events" | "dissolvable-events",
): Promise<TestScenarioResult> => {
	await seedTestData(idb, store, 100000);

	const baseDate = new Date("2025-01-01T00:00:00Z").getTime();
	const startKey = `${baseDate + 10000 * 1000}-${(10000).toString().padStart(10, "0")}`;
	const endKey = `${baseDate + 20000 * 1000}-${(20000).toString().padStart(10, "0")}`;

	const query: DbEventQuery = {
		RootPartitionKey: "test-root",
		PartitionKey: null,
		AggregateTypes: null,
		SortableIdStart: startKey,
		SortableIdEnd: endKey,
		MaxCount: 1000,
	};

	const legacyChunk = await measurePerformance(() =>
		filterEventsChunkLegacy(idb, store, query, 1000)
	);
	const optimized = await measurePerformance(() =>
		filterEventsChunkOptimized(idb, store, query, 1000, 0)
	);

	clearQueryCache();
	await filterEventsChunkCached(idb, store, query, 1000, 0);
	const cached = await measurePerformance(() =>
		filterEventsChunkCached(idb, store, query, 1000, 0)
	);

	const streamResults: DbEvent[] = [];
	const streamStart = performance.now();
	for await (const chunk of streamEvents(idb, store, query, 1000)) {
		streamResults.push(...chunk);
	}
	const streamEnd = performance.now();
	const streaming: PerformanceMetrics = {
		duration: streamEnd - streamStart,
		itemCount: streamResults.length,
		throughput: streamResults.length / ((streamEnd - streamStart) / 1000),
	};

	return {
		scenario: "Large Data Range Scan (100,000 events, range 10k-20k)",
		dataSize: 100000,
		legacyChunk,
		optimized,
		cached,
		streaming,
	};
};

/**
 * シナリオ4: 複雑フィルタ (AggregateType + PartitionKey)
 */
export const testComplexFilter = async (
	idb: SekibanDb,
	store: "events" | "dissolvable-events",
): Promise<TestScenarioResult> => {
	await seedTestData(idb, store, 10000);

	const query: DbEventQuery = {
		RootPartitionKey: "test-root",
		PartitionKey: "partition-5",
		AggregateTypes: ["Type-1", "Type-3"],
		SortableIdStart: null,
		SortableIdEnd: null,
		MaxCount: 500,
	};

	const legacyChunk = await measurePerformance(() =>
		filterEventsChunkLegacy(idb, store, query, 500)
	);
	const optimized = await measurePerformance(() =>
		filterEventsChunkOptimized(idb, store, query, 500, 0)
	);

	clearQueryCache();
	await filterEventsChunkCached(idb, store, query, 500, 0);
	const cached = await measurePerformance(() =>
		filterEventsChunkCached(idb, store, query, 500, 0)
	);

	const streamResults: DbEvent[] = [];
	const streamStart = performance.now();
	for await (const chunk of streamEvents(idb, store, query, 500)) {
		streamResults.push(...chunk);
	}
	const streamEnd = performance.now();
	const streaming: PerformanceMetrics = {
		duration: streamEnd - streamStart,
		itemCount: streamResults.length,
		throughput: streamResults.length / ((streamEnd - streamStart) / 1000),
	};

	return {
		scenario: "Complex Filter (PartitionKey + AggregateTypes)",
		dataSize: 10000,
		legacyChunk,
		optimized,
		cached,
		streaming,
	};
};

/**
 * 全テストシナリオ実行
 */
export const runAllPerformanceTests = async (
	idb: SekibanDb,
	store: "events" | "dissolvable-events",
): Promise<TestScenarioResult[]> => {
	console.log("🚀 Starting Performance Tests...\n");

	const results: TestScenarioResult[] = [];

	// シナリオ1
	console.log("📊 Scenario 1: Small Data Full Scan");
	results.push(await testSmallDataFullScan(idb, store));
	await idb.clear(store); // クリーンアップ

	// シナリオ2
	console.log("📊 Scenario 2: Medium Data Chunked");
	results.push(await testMediumDataChunked(idb, store));
	await idb.clear(store);

	// シナリオ3
	console.log("📊 Scenario 3: Large Data Range Scan");
	results.push(await testLargeDataRangeScan(idb, store));
	await idb.clear(store);

	// シナリオ4
	console.log("📊 Scenario 4: Complex Filter");
	results.push(await testComplexFilter(idb, store));
	await idb.clear(store);

	console.log("\n✅ All Performance Tests Completed\n");

	return results;
};

/**
 * 結果表示 (Markdown Table)
 */
export const printResults = (results: TestScenarioResult[]): void => {
	console.log("## Performance Test Results\n");

	for (const result of results) {
		console.log(`### ${result.scenario}`);
		console.log(`- Data Size: ${result.dataSize.toLocaleString()} events\n`);

		console.log("| Strategy | Duration (ms) | Items | Throughput (items/sec) |");
		console.log("|----------|---------------|-------|------------------------|");

		if (result.legacy) {
			console.log(
				`| Legacy (getAll) | ${result.legacy.duration.toFixed(2)} | ${result.legacy.itemCount} | ${result.legacy.throughput.toFixed(0)} |`,
			);
		}
		if (result.legacyChunk) {
			console.log(
				`| Legacy Chunk (cursor) | ${result.legacyChunk.duration.toFixed(2)} | ${result.legacyChunk.itemCount} | ${result.legacyChunk.throughput.toFixed(0)} |`,
			);
		}

		console.log(
			`| **Optimized** | ${result.optimized.duration.toFixed(2)} | ${result.optimized.itemCount} | ${result.optimized.throughput.toFixed(0)} |`,
		);
		console.log(
			`| **Cached** | ${result.cached.duration.toFixed(2)} | ${result.cached.itemCount} | ${result.cached.throughput.toFixed(0)} |`,
		);
		console.log(
			`| **Streaming** | ${result.streaming.duration.toFixed(2)} | ${result.streaming.itemCount} | ${result.streaming.throughput.toFixed(0)} |`,
		);

		console.log("");
	}
};
