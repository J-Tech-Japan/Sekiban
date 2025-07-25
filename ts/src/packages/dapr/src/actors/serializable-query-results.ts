import { gzip, gunzip } from 'node:zlib';
import { promisify } from 'node:util';
import type { IListQueryResult, IQueryResult, SekibanDomainTypes } from '@sekiban/core';
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
  result: IQueryResult<any>,
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
  
  const queryResult: IQueryResult<any> = {
    value: result.value,
    hasError: false,
    error: null
  };
  
  const serializable = await createSerializableQueryResult(queryResult, query, domainTypes);
  return ok(serializable);
}

/**
 * Create a SerializableListQueryResult from a list query result
 */
export async function createSerializableListQueryResult(
  result: IListQueryResult<any>,
  query: any,
  domainTypes: SekibanDomainTypes
): Promise<SerializableListQueryResult> {
  const queryType = query.constructor.name;
  let recordTypeName = 'unknown';
  
  if (result.values && result.values.length > 0) {
    recordTypeName = result.values[0].constructor.name;
  }
  
  // Serialize items and query
  const itemsJson = JSON.stringify(result.values || []);
  const queryJson = JSON.stringify(query);
  
  // Compress
  const compressedItemsJson = await gzipAsync(itemsJson);
  const compressedQueryJson = await gzipAsync(queryJson);
  
  return {
    totalCount: result.totalCount,
    totalPages: result.totalPages,
    currentPage: result.currentPage,
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
  result: Result<IListQueryResult<any>, any>,
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
): Promise<Result<IQueryResult<any>, any>> {
  try {
    const resultJson = (await gunzipAsync(serialized.compressedResultJson)).toString('utf-8');
    const value = JSON.parse(resultJson);
    
    return ok({
      value,
      hasError: false,
      error: null
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
): Promise<Result<IListQueryResult<any>, any>> {
  try {
    const itemsJson = (await gunzipAsync(serialized.compressedItemsJson)).toString('utf-8');
    const values = JSON.parse(itemsJson);
    
    return ok({
      values,
      totalCount: serialized.totalCount,
      totalPages: serialized.totalPages,
      currentPage: serialized.currentPage,
      pageSize: serialized.pageSize,
      hasError: false,
      error: null
    });
  } catch (error) {
    return err(error);
  }
}