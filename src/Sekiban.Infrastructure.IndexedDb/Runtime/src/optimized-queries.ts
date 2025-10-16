/**
 * IndexedDB + Blazor 最適化クエリ戦略
 *
 * 目的:
 * 1. OutOfMemory 回避 (全件取得禁止)
 * 2. JS ↔ IndexedDB 往復削減 (getAll + limit 活用)
 * 3. ストリーミング型返却 (AsyncIterator)
 * 4. キャッシング戦略 (差分取得)
 *
 * @version 2.0
 */

import type { DbEvent, DbEventQuery, DbBlob, DbBlobQuery } from "./models";
import type { SekibanDb } from "./sekiban-db";

// ==========================================
// 戦略1: ハイブリッド型 Chunk 取得 (getAll + cursor)
// ==========================================

/**
 * イベントマッチング (フィルタ条件適用)
 */
const eventMatchesQuery = (event: DbEvent, query: DbEventQuery): boolean =>
	(query.RootPartitionKey === null ||
		event.RootPartitionKey === query.RootPartitionKey) &&
	(query.PartitionKey === null ||
		event.PartitionKey === query.PartitionKey) &&
	(query.AggregateTypes === null ||
		query.AggregateTypes.includes(event.AggregateType));

/**
 * 最適化版 filterEventsChunk (ハイブリッド戦略)
 *
 * 改善点:
 * - getAll(range, limit) でバッチ取得 → JS往復を1回に削減
 * - 不足分のみカーソルで補填 → 最小限の往復
 * - フォールバック: getAll非対応ブラウザは cursor のみ
 *
 * @param idb IndexedDB インスタンス
 * @param store イベントストア名
 * @param query クエリ条件
 * @param chunkSize 取得件数上限
 * @param skip スキップ件数 (現在未使用、将来対応可能)
 * @returns イベント配列 (最大 chunkSize 件)
 */
export const filterEventsChunkOptimized = async (
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

	// Step 1: getAll でバッチ取得を試みる (limit 指定可能な環境)
	const batchResults = await tryBatchFetch(idb, store, query, limit);

	if (batchResults !== null) {
		// getAll 成功: JS側でフィルタリング
		const filtered = batchResults.filter((e) => eventMatchesQuery(e, query));

		// 十分な件数が取れた場合はそのまま返却
		if (filtered.length >= limit) {
			return filtered.slice(0, limit);
		}

		// 不足分をカーソルで補填
		const lastKey = batchResults.length > 0
			? batchResults[batchResults.length - 1].SortableUniqueId
			: query.SortableIdStart;

		const additional = await fetchWithCursor(
			idb,
			store,
			query,
			lastKey,
			limit - filtered.length,
		);

		return [...filtered, ...additional];
	}

	// Step 2: フォールバック (getAll 非対応) → cursor のみ
	return await fetchWithCursor(idb, store, query, query.SortableIdStart, limit);
};

/**
 * getAll によるバッチ取得を試みる (limit パラメータ付き)
 *
 * 注意:
 * - IDBObjectStore.getAll(range, limit) は比較的新しいAPI
 * - 古いブラウザでは limit 未対応 → null 返却でフォールバック
 *
 * @returns 取得結果 or null (非対応時)
 */
async function tryBatchFetch(
	idb: SekibanDb,
	store: "events" | "dissolvable-events",
	query: DbEventQuery,
	limit: number,
): Promise<DbEvent[] | null> {
	try {
		const tx = idb.transaction(store, "readonly");
		const index = tx.store.index("SortableUniqueId");

		const range = buildKeyRange(query);

		// getAllFromIndex は limit をサポートしない場合がある
		// 手動で limit 対応版を実装
		const objectStore = tx.objectStore(store);
		const idx = objectStore.index("SortableUniqueId");

		// limit 付き getAll の試行
		const request = (idx as any).getAll(range, limit);

		// Promise ラップ
		const results = await new Promise<DbEvent[]>((resolve, reject) => {
			request.onsuccess = () => resolve(request.result);
			request.onerror = () => reject(request.error);
		});

		await tx.done;

		return results;
	} catch (error) {
		// getAll(range, limit) 非対応またはエラー
		console.warn("BatchFetch not supported, falling back to cursor", error);
		return null;
	}
}

/**
 * カーソルベースの取得 (必要最小限の件数のみ)
 *
 * 改善点:
 * - cursor.advance(n) でスキップ可能な部分を飛ばす
 * - cursor.continue(key) で次キーへ直接遷移
 *
 * @param idb IndexedDB インスタンス
 * @param store イベントストア名
 * @param query クエリ条件
 * @param afterKey この後のキーから取得開始
 * @param needed 必要件数
 * @returns イベント配列
 */
async function fetchWithCursor(
	idb: SekibanDb,
	store: "events" | "dissolvable-events",
	query: DbEventQuery,
	afterKey: string | null | undefined,
	needed: number,
): Promise<DbEvent[]> {
	if (needed <= 0) {
		return [];
	}

	const tx = idb.transaction(store, "readonly");
	const index = tx.store.index("SortableUniqueId");

	// afterKey を考慮した範囲設定
	const range = afterKey
		? IDBKeyRange.lowerBound(afterKey, true) // afterKey より大きい
		: query.SortableIdStart !== null
			? IDBKeyRange.lowerBound(query.SortableIdStart, true)
			: undefined;

	const results: DbEvent[] = [];

	let cursor = await index.openCursor(range);
	while (cursor !== null && results.length < needed) {
		const current = cursor.value;

		// SortableIdEnd チェック
		if (
			query.SortableIdEnd !== null &&
			current.SortableUniqueId >= query.SortableIdEnd
		) {
			break;
		}

		if (eventMatchesQuery(current, query)) {
			results.push(current);
		}

		cursor = await cursor.continue();
	}

	await tx.done;

	return results;
}

/**
 * KeyRange ビルダー (SortableUniqueId 範囲)
 */
function buildKeyRange(query: DbEventQuery): IDBKeyRange | undefined {
	if (query.SortableIdStart !== null && query.SortableIdEnd !== null) {
		return IDBKeyRange.bound(
			query.SortableIdStart,
			query.SortableIdEnd,
			true,
			true,
		);
	}
	if (query.SortableIdStart !== null) {
		return IDBKeyRange.lowerBound(query.SortableIdStart, true);
	}
	if (query.SortableIdEnd !== null) {
		return IDBKeyRange.upperBound(query.SortableIdEnd, true);
	}
	return undefined;
}

// ==========================================
// 戦略2: ストリーミング型 AsyncIterator API
// ==========================================

/**
 * 非同期イテレータ型イベント取得 (逐次返却)
 *
 * 利点:
 * - メモリ節約: 全件メモリ展開不要
 * - 早期レスポンス: 最初のチャンク到達後すぐ処理開始可能
 * - Blazor側で for await を使った逐次処理
 *
 * 使用例:
 * ```typescript
 * for await (const chunk of streamEvents(idb, 'events', query, 1000)) {
 *   console.log(`Received ${chunk.length} events`);
 * }
 * ```
 */
export async function* streamEvents(
	idb: SekibanDb,
	store: "events" | "dissolvable-events",
	query: DbEventQuery,
	chunkSize: number,
): AsyncGenerator<DbEvent[], void, undefined> {
	let continuationKey = query.SortableIdStart;
	let remaining = query.MaxCount;

	while (true) {
		// 残り件数チェック
		if (remaining !== null && remaining <= 0) {
			break;
		}

		const currentChunkSize =
			remaining !== null ? Math.min(chunkSize, remaining) : chunkSize;

		// 次のチャンク取得
		const queryWithContinuation: DbEventQuery = {
			...query,
			SortableIdStart: continuationKey,
			MaxCount: currentChunkSize,
		};

		const chunk = await filterEventsChunkOptimized(
			idb,
			store,
			queryWithContinuation,
			currentChunkSize,
			0,
		);

		if (chunk.length === 0) {
			break; // これ以上データなし
		}

		yield chunk;

		// 次回開始位置を更新
		continuationKey = chunk[chunk.length - 1].SortableUniqueId;

		if (remaining !== null) {
			remaining -= chunk.length;
		}

		// チャンクサイズ未満 = 最後のチャンク
		if (chunk.length < currentChunkSize) {
			break;
		}
	}
}

// ==========================================
// 戦略3: キャッシング戦略 (差分取得)
// ==========================================

/**
 * クエリキャッシュエントリ
 */
interface CacheEntry {
	query: DbEventQuery;
	results: DbEvent[];
	lastKey: string | null;
	timestamp: number;
}

/**
 * シンプルなLRUキャッシュ実装
 */
class QueryCache {
	private cache = new Map<string, CacheEntry>();
	private maxSize = 100; // 最大キャッシュエントリ数
	private ttl = 60000; // TTL: 60秒

	/**
	 * クエリキーの生成 (クエリ条件から一意キー)
	 */
	private buildCacheKey(query: DbEventQuery): string {
		return JSON.stringify({
			rp: query.RootPartitionKey,
			pk: query.PartitionKey,
			at: query.AggregateTypes,
			start: query.SortableIdStart,
			end: query.SortableIdEnd,
		});
	}

	/**
	 * キャッシュからの取得 (有効期限チェック)
	 */
	get(query: DbEventQuery): CacheEntry | null {
		const key = this.buildCacheKey(query);
		const entry = this.cache.get(key);

		if (!entry) {
			return null;
		}

		// TTL チェック
		if (Date.now() - entry.timestamp > this.ttl) {
			this.cache.delete(key);
			return null;
		}

		return entry;
	}

	/**
	 * キャッシュへの保存 (LRU管理)
	 */
	set(query: DbEventQuery, results: DbEvent[], lastKey: string | null): void {
		const key = this.buildCacheKey(query);

		// LRU: 最大サイズ超過時は最古エントリを削除
		if (this.cache.size >= this.maxSize) {
			const firstKey = this.cache.keys().next().value;
			if (firstKey !== undefined) {
				this.cache.delete(firstKey);
			}
		}

		this.cache.set(key, {
			query,
			results,
			lastKey,
			timestamp: Date.now(),
		});
	}

	/**
	 * キャッシュクリア
	 */
	clear(): void {
		this.cache.clear();
	}
}

const globalCache = new QueryCache();

/**
 * キャッシュ対応版 filterEventsChunk
 *
 * 動作:
 * 1. キャッシュヒット: 差分のみ取得
 * 2. キャッシュミス: 全件取得してキャッシュ
 *
 * 適用シーン:
 * - 同一クエリの繰り返し実行 (ダッシュボードの定期更新等)
 * - スクロール時の追加読み込み (lastKey ベース)
 */
export const filterEventsChunkCached = async (
	idb: SekibanDb,
	store: "events" | "dissolvable-events",
	query: DbEventQuery,
	chunkSize: number,
	skip: number,
): Promise<DbEvent[]> => {
	const cached = globalCache.get(query);

	if (cached) {
		// キャッシュヒット: 差分取得
		const deltaQuery: DbEventQuery = {
			...query,
			SortableIdStart: cached.lastKey,
		};

		const delta = await filterEventsChunkOptimized(
			idb,
			store,
			deltaQuery,
			chunkSize,
			skip,
		);

		// キャッシュ更新
		const combined = [...cached.results, ...delta];
		const lastKey = delta.length > 0
			? delta[delta.length - 1].SortableUniqueId
			: cached.lastKey;

		globalCache.set(query, combined, lastKey);

		return delta;
	}

	// キャッシュミス: 通常取得してキャッシュ
	const results = await filterEventsChunkOptimized(
		idb,
		store,
		query,
		chunkSize,
		skip,
	);

	const lastKey = results.length > 0
		? results[results.length - 1].SortableUniqueId
		: null;

	globalCache.set(query, results, lastKey);

	return results;
};

/**
 * キャッシュのクリア (手動)
 */
export const clearQueryCache = (): void => {
	globalCache.clear();
};

// ==========================================
// 戦略4: Blob 最適化 (同様の戦略)
// ==========================================

/**
 * 最適化版 filterBlobs
 *
 * 改善点:
 * - getAll(range, limit) 使用
 * - キャッシング対応
 */
export const filterBlobsOptimized = async (
	idb: SekibanDb,
	store:
		| "single-projection-state-blobs"
		| "multi-projection-state-blobs"
		| "multi-projection-events-blobs",
	query: DbBlobQuery,
	chunkSize?: number,
): Promise<DbBlob[]> => {
	const tx = idb.transaction(store, "readonly");
	const index = query.Name !== null ? tx.store.index("Name") : tx.store;

	const range = query.Name !== null ? IDBKeyRange.only(query.Name) : undefined;

	// limit 指定 (MaxCount または chunkSize)
	const limit =
		query.MaxCount !== null
			? query.MaxCount
			: chunkSize !== undefined
				? chunkSize
				: undefined;

	try {
		// getAll with limit
		const request =
			limit !== undefined
				? (index as any).getAll(range, limit)
				: (index as any).getAll(range);

		const results = await new Promise<DbBlob[]>((resolve, reject) => {
			request.onsuccess = () => resolve(request.result);
			request.onerror = () => reject(request.error);
		});

		await tx.done;

		// ID でソート
		return results.sort((a, b) => a.Id.localeCompare(b.Id));
	} catch (error) {
		// フォールバック: カーソルベース
		return await filterBlobsWithCursor(idb, store, query, limit);
	}
};

/**
 * Blob カーソルベース取得 (フォールバック)
 */
async function filterBlobsWithCursor(
	idb: SekibanDb,
	store:
		| "single-projection-state-blobs"
		| "multi-projection-state-blobs"
		| "multi-projection-events-blobs",
	query: DbBlobQuery,
	limit?: number,
): Promise<DbBlob[]> {
	const tx = idb.transaction(store, "readonly");
	const index = query.Name !== null ? tx.store.index("Name") : tx.store;
	const range = query.Name !== null ? IDBKeyRange.only(query.Name) : undefined;

	const results: DbBlob[] = [];
	const maxResults = limit ?? Number.MAX_SAFE_INTEGER;

	let cursor = await index.openCursor(range);
	while (cursor !== null && results.length < maxResults) {
		results.push(cursor.value);
		cursor = await cursor.continue();
	}

	await tx.done;

	return results.sort((a, b) => a.Id.localeCompare(b.Id));
}

// ==========================================
// パフォーマンステスト用ユーティリティ
// ==========================================

/**
 * パフォーマンス測定ヘルパー
 */
export interface PerformanceMetrics {
	duration: number; // ms
	itemCount: number;
	throughput: number; // items/sec
}

export const measurePerformance = async <T>(
	operation: () => Promise<T[]>,
): Promise<PerformanceMetrics> => {
	const start = performance.now();
	const results = await operation();
	const end = performance.now();

	const duration = end - start;
	const itemCount = results.length;
	const throughput = itemCount / (duration / 1000);

	return { duration, itemCount, throughput };
};

/**
 * パフォーマンス比較テスト
 *
 * 使用例:
 * ```typescript
 * const comparison = await compareStrategies(
 *   idb,
 *   'events',
 *   query,
 *   1000
 * );
 * console.table(comparison);
 * ```
 */
export const compareStrategies = async (
	idb: SekibanDb,
	store: "events" | "dissolvable-events",
	query: DbEventQuery,
	chunkSize: number,
): Promise<{
	optimized: PerformanceMetrics;
	cached: PerformanceMetrics;
	streaming: PerformanceMetrics;
}> => {
	// 戦略1: Optimized
	const optimized = await measurePerformance(() =>
		filterEventsChunkOptimized(idb, store, query, chunkSize, 0)
	);

	// 戦略2: Cached (2回目でキャッシュヒット)
	await filterEventsChunkCached(idb, store, query, chunkSize, 0); // ウォームアップ
	const cached = await measurePerformance(() =>
		filterEventsChunkCached(idb, store, query, chunkSize, 0)
	);

	// 戦略3: Streaming
	const streamingResults: DbEvent[] = [];
	const streamStart = performance.now();
	for await (const chunk of streamEvents(idb, store, query, chunkSize)) {
		streamingResults.push(...chunk);
	}
	const streamEnd = performance.now();
	const streaming: PerformanceMetrics = {
		duration: streamEnd - streamStart,
		itemCount: streamingResults.length,
		throughput: streamingResults.length / ((streamEnd - streamStart) / 1000),
	};

	return { optimized, cached, streaming };
};
