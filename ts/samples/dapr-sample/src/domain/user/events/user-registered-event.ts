import type { IEvent } from '../../../infrastructure/simple-sekiban-executor';

export interface UserRegisteredEventData {
  id: string;
  name: string;
  email: string;
  registeredAt: string;
}

export class UserRegisteredEvent implements IEvent {
  public readonly type = 'UserRegistered';
  public readonly aggregateId: string;
  public readonly data: UserRegisteredEventData;
  public readonly version: number;
  public readonly occurredAt: string;
  
  constructor(
    aggregateId: string,
    data: UserRegisteredEventData,
    version: number = 1
  ) {
    this.aggregateId = aggregateId;
    this.data = data;
    this.version = version;
    this.occurredAt = new Date().toISOString();
  }
  
  toJSON() {
    return {
      type: this.type,
      aggregateId: this.aggregateId,
      data: this.data,
      version: this.version,
      occurredAt: this.occurredAt
    };
  }
}