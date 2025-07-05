import type { IProjectorTypes, ProjectorTypeInfo } from '../interfaces.js';
import type { AggregateProjector } from '../../aggregates/aggregate-projector.js';
import type { IAggregatePayload } from '../../aggregates/aggregate-payload.js';

export class ProjectorTypesImpl implements IProjectorTypes {
  constructor(
    private readonly projectors: Map<string, new (...args: any[]) => AggregateProjector<IAggregatePayload>>
  ) {}

  getProjectorTypes(): Array<ProjectorTypeInfo> {
    return Array.from(this.projectors.entries()).map(([name, constructor]) => {
      // Create a temporary instance to get the aggregateTypeName
      const instance = new constructor();
      return {
        name,
        constructor,
        aggregateTypeName: instance.aggregateTypeName
      };
    });
  }

  getProjectorByName(name: string): (new (...args: any[]) => AggregateProjector<IAggregatePayload>) | undefined {
    return this.projectors.get(name);
  }

  getProjectorForAggregate(aggregateType: string): (new (...args: any[]) => AggregateProjector<IAggregatePayload>) | undefined {
    for (const [_, projectorClass] of this.projectors) {
      const instance = new projectorClass();
      if (instance.aggregateTypeName === aggregateType) {
        return projectorClass;
      }
    }
    return undefined;
  }
}