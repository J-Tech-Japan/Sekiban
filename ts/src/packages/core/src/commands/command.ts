/**
 * Legacy command interface for backwards compatibility
 * @deprecated Use schema-registry/command-schema.ts instead
 */
export interface ICommand<TAggregateType extends string = string> {
  commandType: string;
  aggregateType: TAggregateType;
}