import { gzip, gunzip } from 'node:zlib';
import { promisify } from 'node:util';
import type { QueryResult, ListQueryResult, SekibanDomainTypes } from '@sekiban/core';
import { Result, ok, err } from '@sekiban/core';

const gzipAsync = promisify(gzip);
const gunzipAsync = promisify(gunzip);

/**
 * Serializable query result matching C# SerializableQueryResult
 */
export interface SerializableQueryResult {
  resultTypeName: string;
  queryTypeName: string;
  compressedResultJson: Buffer;
  compressedQueryJson: Buffer;
  resultAssemblyVersion: string;
}

/**
 * Serializable list query result matching C# SerializableListQueryResult
 */
export interface SerializableListQueryResult {
  totalCount?: number;
  totalPages?: number;
  currentPage?: number;
  pageSize?: number;
  recordTypeName: string;
  queryTypeName: string;
  compressedItemsJson: Buffer;
  compressedQueryJson: Buffer;
  itemsAssemblyVersion: string;
}

/**
 * Create a SerializableQueryResult from a query result
 */
export async function createSerializableQueryResult(
  result: QueryResult<any>,
  query: any,
  domainTypes: SekibanDomainTypes
): Promise<SerializableQueryResult> {
  const resultType = result.value?.constructor.name || 'unknown';
  const queryType = query.constructor.name;
  
  // Serialize result and query
  const resultJson = JSON.stringify(result.value);
  const queryJson = JSON.stringify(query);
  
  // Compress
  const compressedResultJson = await gzipAsync(resultJson);
  const compressedQueryJson = await gzipAsync(queryJson);
  
  return {
    resultTypeName: resultType,
    queryTypeName: queryType,
    compressedResultJson,
    compressedQueryJson,
    resultAssemblyVersion: '0.0.0.0' // TypeScript doesn't have assembly versions
  };
}

/**
 * Create a SerializableQueryResult from a Result
 */
export async function createSerializableQueryResultFromResult(
  result: Result<any, any>,
  query: any,
  domainTypes: SekibanDomainTypes
): Promise<Result<SerializableQueryResult, any>> {
  if (result.isErr()) {
    return err(result.error);
  }
  
  const queryResult: QueryResult<any> = {
    value: result.value,
    query: query.constructor.name,
    projectionVersion: 0
  };
  
  const serializable = await createSerializableQueryResult(queryResult, query, domainTypes);
  return ok(serializable);
}

/**
 * Create a SerializableListQueryResult from a list query result
 */
export async function createSerializableListQueryResult(
  result: ListQueryResult<any>,
  query: any,
  domainTypes: SekibanDomainTypes
): Promise<SerializableListQueryResult> {
  console.log('[createSerializableListQueryResult] Input:', {
    resultExists: !!result,
    itemsExists: !!result.items,
    itemsCount: result.items?.length,
    totalCount: result.totalCount,
    firstItem: result.items?.[0]
  });
  
  const queryType = query.constructor.name;
  let recordTypeName = 'unknown';
  
  if (result.items && result.items.length > 0) {
    recordTypeName = result.items[0].constructor.name;
    console.log('[createSerializableListQueryResult] First item constructor:', recordTypeName);
    console.log('[createSerializableListQueryResult] First item type:', typeof result.items[0]);
    console.log('[createSerializableListQueryResult] First item is Object:', result.items[0].constructor === Object);
  }
  
  // Serialize items and query
  const itemsJson = JSON.stringify(result.items || []);
  const queryJson = JSON.stringify(query);
  
  console.log('[createSerializableListQueryResult] Serialized items:', {
    itemsJsonLength: itemsJson.length,
    itemsJsonPreview: itemsJson.substring(0, 200) + (itemsJson.length > 200 ? '...' : '')
  });
  
  // Compress
  const compressedItemsJson = await gzipAsync(itemsJson);
  const compressedQueryJson = await gzipAsync(queryJson);
  
  console.log('[createSerializableListQueryResult] Compressed sizes:', {
    compressedItemsSize: compressedItemsJson.length,
    compressedQuerySize: compressedQueryJson.length
  });
  
  return {
    totalCount: result.totalCount,
    totalPages: result.totalPages,
    currentPage: result.pageNumber,
    pageSize: result.pageSize,
    recordTypeName,
    queryTypeName: queryType,
    compressedItemsJson,
    compressedQueryJson,
    itemsAssemblyVersion: '0.0.0.0' // TypeScript doesn't have assembly versions
  };
}

/**
 * Create a SerializableListQueryResult from a Result
 */
export async function createSerializableListQueryResultFromResult(
  result: Result<ListQueryResult<any>, any>,
  query: any,
  domainTypes: SekibanDomainTypes
): Promise<Result<SerializableListQueryResult, any>> {
  if (result.isErr()) {
    return err(result.error);
  }
  
  const serializable = await createSerializableListQueryResult(result.value, query, domainTypes);
  return ok(serializable);
}

/**
 * Deserialize a query result
 */
export async function deserializeQueryResult(
  serialized: SerializableQueryResult,
  domainTypes: SekibanDomainTypes
): Promise<Result<QueryResult<any>, any>> {
  try {
    const resultJson = (await gunzipAsync(serialized.compressedResultJson)).toString('utf-8');
    const value = JSON.parse(resultJson);
    
    return ok({
      value,
      query: serialized.queryTypeName,
      projectionVersion: 0
    });
  } catch (error) {
    return err(error);
  }
}

/**
 * Deserialize a list query result
 */
export async function deserializeListQueryResult(
  serialized: SerializableListQueryResult,
  domainTypes: SekibanDomainTypes
): Promise<Result<ListQueryResult<any>, any>> {
  try {
    console.log('[deserializeListQueryResult] Input:', {
      hasCompressedItemsJson: !!serialized.compressedItemsJson,
      compressedItemsSize: serialized.compressedItemsJson?.length,
      totalCount: serialized.totalCount,
      recordTypeName: serialized.recordTypeName
    });
    
    // Handle Buffer that comes from JSON parsing
    let buffer: Buffer;
    if (serialized.compressedItemsJson && typeof serialized.compressedItemsJson === 'object' && 
        'type' in serialized.compressedItemsJson && serialized.compressedItemsJson.type === 'Buffer' &&
        'data' in serialized.compressedItemsJson && Array.isArray(serialized.compressedItemsJson.data)) {
      // It's a JSON representation of a Buffer
      buffer = Buffer.from(serialized.compressedItemsJson.data);
      console.log('[deserializeListQueryResult] Converted JSON Buffer to actual Buffer:', buffer.length);
    } else {
      // It's already a Buffer
      buffer = serialized.compressedItemsJson;
    }
    
    const itemsJson = (await gunzipAsync(buffer)).toString('utf-8');
    console.log('[deserializeListQueryResult] Decompressed items:', {
      itemsJsonLength: itemsJson.length,
      itemsJsonPreview: itemsJson.substring(0, 200) + (itemsJson.length > 200 ? '...' : '')
    });
    
    const values = JSON.parse(itemsJson);
    console.log('[deserializeListQueryResult] Parsed values:', {
      isArray: Array.isArray(values),
      valuesCount: values.length,
      firstValue: values[0]
    });
    
    const result = {
      items: values,
      totalCount: serialized.totalCount || 0,
      totalPages: serialized.totalPages || 0,
      pageNumber: serialized.currentPage || 1,
      pageSize: serialized.pageSize || 0,
      hasNextPage: (serialized.currentPage || 1) < (serialized.totalPages || 0),
      hasPreviousPage: (serialized.currentPage || 1) > 1,
      query: serialized.queryTypeName
    };
    
    console.log('[deserializeListQueryResult] Result:', {
      itemsCount: result.items.length,
      totalCount: result.totalCount
    });
    
    return ok(result);
  } catch (error) {
    console.error('[deserializeListQueryResult] Error:', error);
    return err(error);
  }
}