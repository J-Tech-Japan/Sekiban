import { describe, it, expect, beforeEach } from 'vitest'
import { InMemorySekibanExecutor } from './in-memory'
import { PartitionKeys } from '../documents/partition-keys'
import { Command, CommandHandler, CommandWithHandler } from '../commands/types'
import { Event } from '../events/types'
import { Query, QueryHandler } from '../queries/types'
import { Projector, AggregatePayload } from '../aggregates/types'
import { ok, err } from 'neverthrow'
import { ValidationError, BusinessRuleError } from '../result/errors'

// Test domain: Bank Account
interface AccountPayload extends AggregatePayload {
  accountId: string
  balance: number
  isActive: boolean
  owner: string
}

class AccountCreated implements Event {
  constructor(
    public readonly accountId: string,
    public readonly owner: string,
    public readonly initialBalance: number
  ) {}
}

class MoneyDeposited implements Event {
  constructor(
    public readonly accountId: string,
    public readonly amount: number
  ) {}
}

class MoneyWithdrawn implements Event {
  constructor(
    public readonly accountId: string,
    public readonly amount: number
  ) {}
}

class CreateAccountCommand implements CommandWithHandler<CreateAccountCommand, AccountProjector> {
  constructor(
    public readonly owner: string,
    public readonly initialDeposit: number
  ) {}
  
  getHandler(): CommandHandler<CreateAccountCommand, AccountProjector> {
    return {
      validate: async (command) => {
        if (!command.owner || command.owner.trim() === '') {
          return err(new ValidationError('Owner name is required', 'owner'))
        }
        if (command.initialDeposit < 0) {
          return err(new ValidationError('Initial deposit must be non-negative', 'initialDeposit'))
        }
        return ok(undefined)
      },
      handle: async (command, _) => {
        const accountId = `account-${Date.now()}`
        return ok([
          new AccountCreated(accountId, command.owner, command.initialDeposit)
        ])
      }
    }
  }
}

class DepositMoneyCommand implements CommandWithHandler<DepositMoneyCommand, AccountProjector, AccountPayload> {
  constructor(
    public readonly accountId: string,
    public readonly amount: number
  ) {}
  
  getHandler(): CommandHandler<DepositMoneyCommand, AccountProjector, AccountPayload> {
    return {
      validate: async (command) => {
        if (command.amount <= 0) {
          return err(new ValidationError('Deposit amount must be positive', 'amount'))
        }
        return ok(undefined)
      },
      handle: async (command, state) => {
        if (!state.isActive) {
          return err(new BusinessRuleError('Cannot deposit to inactive account', 'ACCOUNT_INACTIVE'))
        }
        return ok([new MoneyDeposited(command.accountId, command.amount)])
      }
    }
  }
}

class WithdrawMoneyCommand implements CommandWithHandler<WithdrawMoneyCommand, AccountProjector, AccountPayload> {
  constructor(
    public readonly accountId: string,
    public readonly amount: number
  ) {}
  
  getHandler(): CommandHandler<WithdrawMoneyCommand, AccountProjector, AccountPayload> {
    return {
      validate: async (command) => {
        if (command.amount <= 0) {
          return err(new ValidationError('Withdrawal amount must be positive', 'amount'))
        }
        return ok(undefined)
      },
      handle: async (command, state) => {
        if (!state.isActive) {
          return err(new BusinessRuleError('Cannot withdraw from inactive account', 'ACCOUNT_INACTIVE'))
        }
        if (state.balance < command.amount) {
          return err(new BusinessRuleError('Insufficient balance', 'INSUFFICIENT_BALANCE'))
        }
        return ok([new MoneyWithdrawn(command.accountId, command.amount)])
      }
    }
  }
}

class AccountProjector implements Projector<AccountPayload> {
  getInitialState(): AccountPayload {
    return {
      accountId: '',
      balance: 0,
      isActive: false,
      owner: ''
    }
  }
  
  apply(state: AccountPayload, event: Event): AccountPayload {
    if (event instanceof AccountCreated) {
      return {
        accountId: event.accountId,
        balance: event.initialBalance,
        isActive: true,
        owner: event.owner
      }
    }
    
    if (event instanceof MoneyDeposited) {
      return {
        ...state,
        balance: state.balance + event.amount
      }
    }
    
    if (event instanceof MoneyWithdrawn) {
      return {
        ...state,
        balance: state.balance - event.amount
      }
    }
    
    return state
  }
}

class GetAccountQuery implements Query<AccountPayload> {
  constructor(public readonly accountId: string) {}
}

class GetAccountQueryHandler implements QueryHandler<GetAccountQuery, AccountPayload> {
  async handle(query: GetAccountQuery, executor: InMemorySekibanExecutor) {
    const partitionKeys = PartitionKeys.existing(query.accountId)
    const result = await executor.getAggregate(new AccountProjector(), partitionKeys)
    
    if (result.isErr()) {
      return err(result.error)
    }
    
    const aggregate = result.value
    if (!aggregate.payload.isActive) {
      return err(new BusinessRuleError('Account not found', 'ACCOUNT_NOT_FOUND'))
    }
    
    return ok(aggregate.payload)
  }
}

describe('InMemorySekibanExecutor', () => {
  let executor: InMemorySekibanExecutor
  
  beforeEach(() => {
    executor = new InMemorySekibanExecutor()
    executor.registerCommandHandler(CreateAccountCommand, new CreateAccountCommand('', 0).getHandler())
    executor.registerCommandHandler(DepositMoneyCommand, new DepositMoneyCommand('', 0).getHandler())
    executor.registerCommandHandler(WithdrawMoneyCommand, new WithdrawMoneyCommand('', 0).getHandler())
    executor.registerQueryHandler(GetAccountQuery, new GetAccountQueryHandler())
    executor.registerProjector('AccountProjector', new AccountProjector())
  })
  
  describe('Command Execution', () => {
    it('should create a new account successfully', async () => {
      // Arrange
      const command = new CreateAccountCommand('John Doe', 1000)
      
      // Act
      const result = await executor.execute(command)
      
      // Assert
      expect(result.isOk()).toBe(true)
      const response = result._unsafeUnwrap()
      expect(response.events).toHaveLength(1)
      expect(response.events[0]).toBeInstanceOf(AccountCreated)
      expect(response.version).toBe(1)
    })
    
    it('should fail to create account with invalid data', async () => {
      // Arrange
      const command = new CreateAccountCommand('', 1000)
      
      // Act
      const result = await executor.execute(command)
      
      // Assert
      expect(result.isErr()).toBe(true)
      const error = result._unsafeUnwrapErr()
      expect(error.code).toBe('VALIDATION_ERROR')
      expect((error as ValidationError).field).toBe('owner')
    })
    
    it('should deposit money to existing account', async () => {
      // Arrange
      const createResult = await executor.execute(new CreateAccountCommand('John Doe', 1000))
      const accountId = (createResult._unsafeUnwrap().events[0] as AccountCreated).accountId
      const partitionKeys = PartitionKeys.existing(accountId)
      
      const depositCommand = new DepositMoneyCommand(accountId, 500)
      
      // Act
      const result = await executor.execute(depositCommand, partitionKeys)
      
      // Assert
      expect(result.isOk()).toBe(true)
      const response = result._unsafeUnwrap()
      expect(response.events[0]).toBeInstanceOf(MoneyDeposited)
      expect(response.version).toBe(2)
    })
    
    it('should enforce business rules for withdrawal', async () => {
      // Arrange
      const createResult = await executor.execute(new CreateAccountCommand('John Doe', 1000))
      const accountId = (createResult._unsafeUnwrap().events[0] as AccountCreated).accountId
      const partitionKeys = PartitionKeys.existing(accountId)
      
      // Try to withdraw more than balance
      const withdrawCommand = new WithdrawMoneyCommand(accountId, 1500)
      
      // Act
      const result = await executor.execute(withdrawCommand, partitionKeys)
      
      // Assert
      expect(result.isErr()).toBe(true)
      const error = result._unsafeUnwrapErr()
      expect(error.code).toBe('BUSINESS_RULE_ERROR')
      expect((error as BusinessRuleError).rule).toBe('INSUFFICIENT_BALANCE')
    })
  })
  
  describe('Query Execution', () => {
    it('should retrieve account state', async () => {
      // Arrange
      const createResult = await executor.execute(new CreateAccountCommand('John Doe', 1000))
      const accountId = (createResult._unsafeUnwrap().events[0] as AccountCreated).accountId
      const partitionKeys = PartitionKeys.existing(accountId)
      
      // Make some transactions
      await executor.execute(new DepositMoneyCommand(accountId, 500), partitionKeys)
      await executor.execute(new WithdrawMoneyCommand(accountId, 200), partitionKeys)
      
      // Act
      const query = new GetAccountQuery(accountId)
      const result = await executor.query(query)
      
      // Assert
      expect(result.isOk()).toBe(true)
      const account = result._unsafeUnwrap()
      expect(account.balance).toBe(1300) // 1000 + 500 - 200
      expect(account.owner).toBe('John Doe')
      expect(account.isActive).toBe(true)
    })
  })
  
  describe('Event Sourcing Flow', () => {
    it('should maintain event history and rebuild state', async () => {
      // Arrange & Act
      const createResult = await executor.execute(new CreateAccountCommand('Jane Doe', 500))
      const accountId = (createResult._unsafeUnwrap().events[0] as AccountCreated).accountId
      const partitionKeys = PartitionKeys.existing(accountId)
      
      // Perform multiple operations
      await executor.execute(new DepositMoneyCommand(accountId, 300), partitionKeys)
      await executor.execute(new WithdrawMoneyCommand(accountId, 100), partitionKeys)
      await executor.execute(new DepositMoneyCommand(accountId, 200), partitionKeys)
      
      // Assert - check aggregate state
      const aggregateResult = await executor.getAggregate(new AccountProjector(), partitionKeys)
      expect(aggregateResult.isOk()).toBe(true)
      
      const aggregate = aggregateResult._unsafeUnwrap()
      expect(aggregate.version).toBe(4)
      expect(aggregate.payload.balance).toBe(900) // 500 + 300 - 100 + 200
      
      // Assert - check event history
      const events = await executor.getEvents(partitionKeys)
      expect(events).toHaveLength(4)
      expect(events[0].eventType).toBe('AccountCreated')
      expect(events[1].eventType).toBe('MoneyDeposited')
      expect(events[2].eventType).toBe('MoneyWithdrawn')
      expect(events[3].eventType).toBe('MoneyDeposited')
    })
  })
  
  describe('Snapshot functionality', () => {
    it('should handle snapshots correctly', async () => {
      // Arrange
      const createResult = await executor.execute(new CreateAccountCommand('Snapshot Test', 1000))
      const accountId = (createResult._unsafeUnwrap().events[0] as AccountCreated).accountId
      const partitionKeys = PartitionKeys.existing(accountId)
      
      // Generate many events
      for (let i = 0; i < 15; i++) {
        await executor.execute(new DepositMoneyCommand(accountId, 10), partitionKeys)
      }
      
      // Act - retrieve with snapshot
      const aggregateResult = await executor.getAggregate(new AccountProjector(), partitionKeys)
      
      // Assert
      expect(aggregateResult.isOk()).toBe(true)
      const aggregate = aggregateResult._unsafeUnwrap()
      expect(aggregate.version).toBe(16) // 1 create + 15 deposits
      expect(aggregate.payload.balance).toBe(1150) // 1000 + 15*10
      
      // Verify snapshot was created (version 10)
      const snapshot = await executor.getSnapshot(partitionKeys)
      expect(snapshot).toBeDefined()
      expect(snapshot?.version).toBe(10)
    })
  })
})
