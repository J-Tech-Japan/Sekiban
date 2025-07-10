import { describe, it, expect } from 'vitest'
import {
  IQuery,
  IQueryContext,
  IMultiProjectionQuery,
  IMultiProjectionListQuery,
  IQueryPagingParameter,
  QueryResult,
  ListQueryResult,
  createQueryResult,
  createListQueryResult
} from './query'
import { Result, ok, err } from 'neverthrow'
import { QueryExecutionError } from '../result/errors'
import { IMultiProjector, MultiProjectionState } from './multi-projection'
import { PartitionKeys } from '../documents/partition-keys'
import { Aggregate } from '../aggregates/aggregate'
import { IAggregatePayload } from '../aggregates/aggregate-payload'

// Test payload
class UserPayload implements IAggregatePayload {
  constructor(
    public readonly id: string,
    public readonly name: string,
    public readonly email: string,
    public readonly isActive: boolean = true
  ) {}
}

// Test multi-projection payload
class UserListPayload {
  constructor(
    public readonly aggregates: Map<string, Aggregate<UserPayload>> = new Map()
  ) {}
}

// Test multi-projector
class UserListProjector implements IMultiProjector<UserListPayload> {
  getTypeName(): string { return 'UserListProjector' }
  getVersion(): number { return 1 }
  
  getInitialState(): UserListPayload {
    return new UserListPayload()
  }
  
  project(state: UserListPayload, event: any): UserListPayload {
    // Simplified projection logic
    return state
  }
}

describe('Query Interfaces', () => {
  describe('IQuery', () => {
    it('should define basic query interface', () => {
      // Arrange
      class GetUserByIdQuery implements IQuery {
        constructor(public readonly userId: string) {}
      }
      
      // Act
      const query = new GetUserByIdQuery('user-123')
      
      // Assert
      expect(query).toBeDefined()
      expect(query.userId).toBe('user-123')
    })
  })
  
  describe('IMultiProjectionQuery', () => {
    it('should handle single result query', () => {
      // Arrange
      interface UserDto {
        id: string
        name: string
        email: string
      }
      
      class GetUserQuery implements IMultiProjectionQuery<
        UserListProjector,
        GetUserQuery,
        UserDto
      > {
        constructor(public readonly userId: string) {}
        
        static handleQuery(
          projection: MultiProjectionState<UserListProjector>,
          query: GetUserQuery,
          context: IQueryContext
        ): Result<UserDto, QueryExecutionError> {
          const aggregate = projection.payload.aggregates.get(query.userId)
          
          if (!aggregate) {
            return err(new QueryExecutionError(
              'GetUserQuery',
              `User ${query.userId} not found`
            ))
          }
          
          const payload = aggregate.payload
          return ok({
            id: payload.id,
            name: payload.name,
            email: payload.email
          })
        }
      }
      
      // Setup test data
      const userAggregate = new Aggregate(
        PartitionKeys.create('user-123'),
        'User',
        1,
        new UserPayload('user-123', 'John Doe', 'john@example.com'),
        null,
        'UserProjector',
        1
      )
      
      const projectionState = new MultiProjectionState(
        new UserListProjector(),
        new UserListPayload(new Map([['user-123', userAggregate]])),
        1
      )
      
      const context: IQueryContext = {
        getService: <T>(key: string) => undefined as any
      }
      
      // Act
      const result = GetUserQuery.handleQuery(
        projectionState,
        new GetUserQuery('user-123'),
        context
      )
      
      // Assert
      expect(result.isOk()).toBe(true)
      if (result.isOk()) {
        expect(result.value.id).toBe('user-123')
        expect(result.value.name).toBe('John Doe')
        expect(result.value.email).toBe('john@example.com')
      }
    })
    
    it('should handle query with not found result', () => {
      // Arrange
      class GetUserQuery implements IMultiProjectionQuery<
        UserListProjector,
        GetUserQuery,
        UserPayload
      > {
        constructor(public readonly userId: string) {}
        
        static handleQuery(
          projection: MultiProjectionState<UserListProjector>,
          query: GetUserQuery,
          context: IQueryContext
        ): Result<UserPayload, QueryExecutionError> {
          const aggregate = projection.payload.aggregates.get(query.userId)
          
          if (!aggregate) {
            return err(new QueryExecutionError(
              'GetUserQuery',
              `User ${query.userId} not found`
            ))
          }
          
          return ok(aggregate.payload)
        }
      }
      
      const projectionState = new MultiProjectionState(
        new UserListProjector(),
        new UserListPayload(),
        1
      )
      
      const context: IQueryContext = {
        getService: <T>(key: string) => undefined as any
      }
      
      // Act
      const result = GetUserQuery.handleQuery(
        projectionState,
        new GetUserQuery('non-existent'),
        context
      )
      
      // Assert
      expect(result.isErr()).toBe(true)
      if (result.isErr()) {
        expect(result.error.queryType).toBe('GetUserQuery')
        expect(result.error.reason).toContain('not found')
      }
    })
  })
  
  describe('IMultiProjectionListQuery', () => {
    it('should handle list query with filtering and sorting', () => {
      // Arrange
      interface UserListItem {
        id: string
        name: string
        isActive: boolean
      }
      
      class GetActiveUsersQuery implements IMultiProjectionListQuery<
        UserListProjector,
        GetActiveUsersQuery,
        UserListItem
      > {
        constructor(
          public readonly nameFilter?: string,
          public readonly pageSize: number = 10,
          public readonly pageNumber: number = 1
        ) {}
        
        static handleFilter(
          projection: MultiProjectionState<UserListProjector>,
          query: GetActiveUsersQuery,
          context: IQueryContext
        ): Result<UserListItem[], QueryExecutionError> {
          const users = Array.from(projection.payload.aggregates.values())
            .map(agg => agg.payload)
            .filter(user => user.isActive)
            .filter(user => 
              !query.nameFilter || 
              user.name.toLowerCase().includes(query.nameFilter.toLowerCase())
            )
            .map(user => ({
              id: user.id,
              name: user.name,
              isActive: user.isActive
            }))
          
          return ok(users)
        }
        
        static handleSort(
          filteredList: UserListItem[],
          query: GetActiveUsersQuery,
          context: IQueryContext
        ): Result<UserListItem[], QueryExecutionError> {
          // Sort by name
          return ok(filteredList.sort((a, b) => a.name.localeCompare(b.name)))
        }
      }
      
      // Setup test data
      const users = [
        new Aggregate(
          PartitionKeys.create('user-1'),
          'User',
          1,
          new UserPayload('user-1', 'Alice', 'alice@test.com', true),
          null,
          'UserProjector',
          1
        ),
        new Aggregate(
          PartitionKeys.create('user-2'),
          'User',
          1,
          new UserPayload('user-2', 'Bob', 'bob@test.com', false),
          null,
          'UserProjector',
          1
        ),
        new Aggregate(
          PartitionKeys.create('user-3'),
          'User',
          1,
          new UserPayload('user-3', 'Charlie', 'charlie@test.com', true),
          null,
          'UserProjector',
          1
        )
      ]
      
      const aggregateMap = new Map(
        users.map(u => [u.payload.id, u])
      )
      
      const projectionState = new MultiProjectionState(
        new UserListProjector(),
        new UserListPayload(aggregateMap),
        1
      )
      
      const context: IQueryContext = {
        getService: <T>(key: string) => undefined as any
      }
      
      // Act - filter
      const filterResult = GetActiveUsersQuery.handleFilter(
        projectionState,
        new GetActiveUsersQuery(),
        context
      )
      
      // Assert filter
      expect(filterResult.isOk()).toBe(true)
      if (filterResult.isOk()) {
        expect(filterResult.value).toHaveLength(2) // Only active users
        
        // Act - sort
        const sortResult = GetActiveUsersQuery.handleSort(
          filterResult.value,
          new GetActiveUsersQuery(),
          context
        )
        
        // Assert sort
        expect(sortResult.isOk()).toBe(true)
        if (sortResult.isOk()) {
          expect(sortResult.value[0]!.name).toBe('Alice')
          expect(sortResult.value[1]!.name).toBe('Charlie')
        }
      }
    })
  })
  
  describe('QueryResult', () => {
    it('should create single query result', () => {
      // Arrange & Act
      const result = createQueryResult({
        value: { id: '123', name: 'Test' },
        query: 'GetById',
        projectionVersion: 5
      })
      
      // Assert
      expect(result.value).toEqual({ id: '123', name: 'Test' })
      expect(result.query).toBe('GetById')
      expect(result.projectionVersion).toBe(5)
    })
  })
  
  describe('ListQueryResult', () => {
    it('should create list query result with paging', () => {
      // Arrange
      const items = [
        { id: '1', name: 'Item 1' },
        { id: '2', name: 'Item 2' },
        { id: '3', name: 'Item 3' }
      ]
      
      // Act
      const result = createListQueryResult({
        items,
        totalCount: 50,
        pageSize: 3,
        pageNumber: 1,
        query: 'GetList'
      })
      
      // Assert
      expect(result.items).toEqual(items)
      expect(result.totalCount).toBe(50)
      expect(result.pageSize).toBe(3)
      expect(result.pageNumber).toBe(1)
      expect(result.totalPages).toBe(17) // Math.ceil(50/3)
      expect(result.hasNextPage).toBe(true)
      expect(result.hasPreviousPage).toBe(false)
    })
    
    it('should calculate paging correctly', () => {
      // Arrange & Act
      const result = createListQueryResult({
        items: [],
        totalCount: 25,
        pageSize: 10,
        pageNumber: 3
      })
      
      // Assert
      expect(result.totalPages).toBe(3)
      expect(result.hasNextPage).toBe(false)
      expect(result.hasPreviousPage).toBe(true)
    })
  })
  
  describe('IQueryPagingParameter', () => {
    it('should support paging parameters', () => {
      // Arrange
      class PagedQuery implements IQueryPagingParameter {
        constructor(
          public readonly pageSize: number = 20,
          public readonly pageNumber: number = 1
        ) {}
      }
      
      // Act
      const query = new PagedQuery(10, 2)
      
      // Assert
      expect(query.pageSize).toBe(10)
      expect(query.pageNumber).toBe(2)
    })
  })
  
  describe('Query consistency', () => {
    it('should support wait for consistency', () => {
      // Arrange
      class ConsistentQuery implements IQuery {
        constructor(
          public readonly id: string,
          public readonly waitForEventId?: string
        ) {}
      }
      
      // Act
      const query = new ConsistentQuery('123', 'event-456')
      
      // Assert
      expect(query.waitForEventId).toBe('event-456')
    })
  })
})