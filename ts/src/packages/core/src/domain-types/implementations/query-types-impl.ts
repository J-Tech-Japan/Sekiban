import type { IQueryTypes, QueryTypeInfo } from '../interfaces.js';

export class QueryTypesImpl implements IQueryTypes {
  constructor(
    private readonly queries: Map<string, new (...args: any[]) => any>
  ) {}

  getQueryTypes(): Array<QueryTypeInfo> {
    return Array.from(this.queries.entries()).map(([name, constructor]) => ({
      name,
      constructor
    }));
  }

  getQueryTypeByName(name: string): (new (...args: any[]) => any) | undefined {
    return this.queries.get(name);
  }
}