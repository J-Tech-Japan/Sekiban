import type { IQuery } from '../../../infrastructure/simple-sekiban-executor';

export interface UserQueryResult {
  id: string;
  name: string;
  email: string;
  createdAt: string;
}

export class GetUserQuery implements IQuery<UserQueryResult> {
  public readonly type = 'GetUser';
  public readonly userId: string;
  public readonly asOf?: string;
  
  constructor(userId: string, asOf?: string) {
    if (!userId || typeof userId !== 'string') {
      throw new Error('User ID is required and must be a string');
    }
    this.userId = userId;
    this.asOf = asOf;
  }
  
  toJSON() {
    return {
      type: this.type,
      userId: this.userId,
      asOf: this.asOf
    };
  }
}