// Temporary declaration file for @sekiban/core
// This will be removed once the package is published to npm
declare module '@sekiban/core' {
  export const defineEvent: any;
  export const defineCommand: any;
  export function defineProjector<T = any>(definition: any): any;
  export class SekibanError extends Error {
    type: string;
  }
  export const defineQuery: any;
  export const PartitionKeys: any;
  export const EmptyAggregatePayload: any;
  export const ok: any;
  export const err: any;
  export const okAsync: any;
  export const errAsync: any;
  export const globalRegistry: any;
  export const createSchemaDomainTypes: any;
  export const SchemaDomainTypes: any;
  export type SekibanDomainTypes = any;
  export type ISekibanExecutor = any;
  export type Event = any;
  export type Aggregate = any;
  export type Result<T, E> = any;
  export type ResultAsync<T, E> = any;
}