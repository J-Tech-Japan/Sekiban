import { IEventPayload } from '../events/event-payload'

/**
 * Test event for user creation
 */
export class UserCreated implements IEventPayload {
  constructor(
    public readonly name: string,
    public readonly email: string
  ) {}
}

/**
 * Test event for user updates
 */
export class UserUpdated implements IEventPayload {
  constructor(
    public readonly name?: string,
    public readonly email?: string
  ) {}
}

/**
 * Test event for user deletion
 */
export class UserDeleted implements IEventPayload {
  constructor(
    public readonly reason: string
  ) {}
}