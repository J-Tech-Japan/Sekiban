import type { IAggregateTypes, AggregateTypeInfo } from '../interfaces.js';
import type { IAggregatePayload } from '../../aggregates/aggregate-payload.js';

export class AggregateTypesImpl implements IAggregateTypes {
  constructor(
    private readonly aggregates: Map<string, new (...args: any[]) => IAggregatePayload>
  ) {}

  getAggregateTypes(): Array<AggregateTypeInfo> {
    return Array.from(this.aggregates.entries()).map(([name, constructor]) => ({
      name,
      constructor
    }));
  }

  getAggregateTypeByName(name: string): (new (...args: any[]) => IAggregatePayload) | undefined {
    return this.aggregates.get(name);
  }
}