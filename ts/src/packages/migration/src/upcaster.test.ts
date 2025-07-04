import { describe, it, expect, beforeEach } from 'vitest'
import { 
  Upcaster, 
  UpcasterRegistry, 
  upcastEvent,
  createUpcasterChain
} from './upcaster'
import { IEvent, PartitionKeys, SortableUniqueId } from '@sekiban/core'

// Test event types
interface UserCreatedV1 {
  fullName: string
  email: string
}

interface UserCreatedV2 {
  firstName: string
  lastName: string
  email: string
}

interface UserCreatedV3 {
  firstName: string
  lastName: string
  email: string
  createdAt: Date
}

describe('Upcaster', () => {
  const partitionKeys = PartitionKeys.generate('User')
  
  const createEvent = (payload: any, version: number): IEvent => ({
    sortableUniqueId: SortableUniqueId.generate(new Date()).value,
    eventType: 'UserCreated',
    payload,
    aggregateId: partitionKeys.aggregateId,
    partitionKeys,
    version,
    meta: { schemaVersion: version }
  })

  describe('individual upcasters', () => {
    it('should upcast from v1 to v2', () => {
      const upcaster: Upcaster<UserCreatedV1, UserCreatedV2> = {
        eventType: 'UserCreated',
        fromVersion: 1,
        toVersion: 2,
        upcast: (payload: UserCreatedV1) => {
          const [firstName, lastName] = payload.fullName.split(' ')
          return {
            firstName: firstName || '',
            lastName: lastName || '',
            email: payload.email
          }
        }
      }

      const v1Payload: UserCreatedV1 = {
        fullName: 'John Doe',
        email: 'john@example.com'
      }

      const v2Payload = upcaster.upcast(v1Payload)
      
      expect(v2Payload.firstName).toBe('John')
      expect(v2Payload.lastName).toBe('Doe')
      expect(v2Payload.email).toBe('john@example.com')
    })

    it('should upcast from v2 to v3', () => {
      const upcaster: Upcaster<UserCreatedV2, UserCreatedV3> = {
        eventType: 'UserCreated',
        fromVersion: 2,
        toVersion: 3,
        upcast: (payload: UserCreatedV2) => ({
          ...payload,
          createdAt: new Date('2024-01-01')
        })
      }

      const v2Payload: UserCreatedV2 = {
        firstName: 'John',
        lastName: 'Doe',
        email: 'john@example.com'
      }

      const v3Payload = upcaster.upcast(v2Payload)
      
      expect(v3Payload.firstName).toBe('John')
      expect(v3Payload.lastName).toBe('Doe')
      expect(v3Payload.email).toBe('john@example.com')
      expect(v3Payload.createdAt).toEqual(new Date('2024-01-01'))
    })
  })

  describe('UpcasterRegistry', () => {
    let registry: UpcasterRegistry

    beforeEach(() => {
      registry = new UpcasterRegistry()
    })

    it('should register and retrieve upcasters', () => {
      const upcaster: Upcaster<UserCreatedV1, UserCreatedV2> = {
        eventType: 'UserCreated',
        fromVersion: 1,
        toVersion: 2,
        upcast: (payload) => {
          const [firstName, lastName] = payload.fullName.split(' ')
          return { firstName: firstName || '', lastName: lastName || '', email: payload.email }
        }
      }

      registry.register(upcaster)

      const retrieved = registry.getUpcaster('UserCreated', 1)
      expect(retrieved).toBe(upcaster)
    })

    it('should return undefined for non-existent upcaster', () => {
      const upcaster = registry.getUpcaster('NonExistent', 1)
      expect(upcaster).toBeUndefined()
    })

    it('should get chain of upcasters', () => {
      const v1tov2: Upcaster<any, any> = {
        eventType: 'UserCreated',
        fromVersion: 1,
        toVersion: 2,
        upcast: (p) => ({ ...p, v2: true })
      }

      const v2tov3: Upcaster<any, any> = {
        eventType: 'UserCreated',
        fromVersion: 2,
        toVersion: 3,
        upcast: (p) => ({ ...p, v3: true })
      }

      registry.register(v1tov2)
      registry.register(v2tov3)

      const chain = registry.getUpcastChain('UserCreated', 1, 3)
      expect(chain).toHaveLength(2)
      expect(chain[0]).toBe(v1tov2)
      expect(chain[1]).toBe(v2tov3)
    })
  })

  describe('upcastEvent', () => {
    let registry: UpcasterRegistry

    beforeEach(() => {
      registry = new UpcasterRegistry()
      
      // Register v1 to v2
      registry.register({
        eventType: 'UserCreated',
        fromVersion: 1,
        toVersion: 2,
        upcast: (payload: UserCreatedV1) => {
          const [firstName, lastName] = payload.fullName.split(' ')
          return {
            firstName: firstName || '',
            lastName: lastName || '',
            email: payload.email
          }
        }
      })

      // Register v2 to v3
      registry.register({
        eventType: 'UserCreated',
        fromVersion: 2,
        toVersion: 3,
        upcast: (payload: UserCreatedV2) => ({
          ...payload,
          createdAt: new Date('2024-01-01')
        })
      })
    })

    it('should upcast event to latest version', () => {
      const v1Event = createEvent(
        { fullName: 'Jane Smith', email: 'jane@example.com' },
        1
      )

      const upcastedEvent = upcastEvent(v1Event, registry, 3)
      
      expect(upcastedEvent.payload.firstName).toBe('Jane')
      expect(upcastedEvent.payload.lastName).toBe('Smith')
      expect(upcastedEvent.payload.email).toBe('jane@example.com')
      expect(upcastedEvent.payload.createdAt).toEqual(new Date('2024-01-01'))
      expect(upcastedEvent.meta?.schemaVersion).toBe(3)
    })

    it('should not change event that is already at target version', () => {
      const v3Event = createEvent(
        { 
          firstName: 'Already',
          lastName: 'Updated',
          email: 'updated@example.com',
          createdAt: new Date('2024-02-01')
        },
        3
      )

      const upcastedEvent = upcastEvent(v3Event, registry, 3)
      
      expect(upcastedEvent).toEqual(v3Event)
    })
  })

  describe('createUpcasterChain', () => {
    it('should create a composite upcaster from chain', () => {
      const v1tov2: Upcaster<UserCreatedV1, UserCreatedV2> = {
        eventType: 'UserCreated',
        fromVersion: 1,
        toVersion: 2,
        upcast: (payload) => {
          const [firstName, lastName] = payload.fullName.split(' ')
          return {
            firstName: firstName || '',
            lastName: lastName || '',
            email: payload.email
          }
        }
      }

      const v2tov3: Upcaster<UserCreatedV2, UserCreatedV3> = {
        eventType: 'UserCreated',
        fromVersion: 2,
        toVersion: 3,
        upcast: (payload) => ({
          ...payload,
          createdAt: new Date('2024-01-01')
        })
      }

      const chain = createUpcasterChain([v1tov2, v2tov3])
      
      const v1Payload: UserCreatedV1 = {
        fullName: 'Chain Test',
        email: 'chain@test.com'
      }

      const result = chain(v1Payload)
      
      expect(result.firstName).toBe('Chain')
      expect(result.lastName).toBe('Test')
      expect(result.email).toBe('chain@test.com')
      expect(result.createdAt).toEqual(new Date('2024-01-01'))
    })
  })
})