# Immediate Testing Improvements for Sekiban.ts

Based on the issues encountered during the schema-based implementation, here are specific tests that should be added immediately:

## 1. Schema-Based Command Integration Tests

```typescript
// Test file: src/packages/core/src/schema-registry/__tests__/schema-command-integration.test.ts

describe('Schema-based command integration', () => {
  it('should create commands that implement ICommand interface', () => {
    const commandDef = defineCommand({
      type: 'TestCommand',
      schema: z.object({ value: z.string() }),
      aggregateType: 'Test',
      handlers: {
        specifyPartitionKeys: () => PartitionKeys.generate('Test'),
        validate: () => ok(undefined),
        handle: () => ok([])
      }
    });
    
    const command = commandDef.create({ value: 'test' });
    
    // Verify ICommand interface implementation
    expect(command.commandType).toBe('TestCommand');
    expect(typeof command.validate).toBe('function');
    expect(typeof command.specifyPartitionKeys).toBe('function');
    expect(typeof command.handle).toBe('function');
  });

  it('should validate schema on command creation', () => {
    const commandDef = defineCommand({
      type: 'TestCommand',
      schema: z.object({ value: z.string().min(5) }),
      aggregateType: 'Test',
      handlers: {
        specifyPartitionKeys: () => PartitionKeys.generate('Test'),
        validate: () => ok(undefined),
        handle: () => ok([])
      }
    });
    
    // Should throw on invalid data
    expect(() => commandDef.create({ value: '123' })).toThrow();
    
    // Should succeed on valid data
    expect(() => commandDef.create({ value: '12345' })).not.toThrow();
  });
});
```

## 2. Executor Compatibility Tests

```typescript
// Test file: src/packages/dapr/src/executor/__tests__/schema-command-executor.test.ts

describe('SekibanDaprExecutor with schema commands', () => {
  it('should execute schema-based commands', async () => {
    const mockDaprClient = createMockDaprClient();
    const domainTypes = createTestDomainTypes();
    
    const executor = new SekibanDaprExecutor(
      mockDaprClient,
      domainTypes,
      testConfig
    );
    
    const command = TestCommand.create({ value: 'test' });
    const result = await executor.executeCommandAsync(command);
    
    expect(result.isOk()).toBe(true);
    expect(mockDaprClient.invoker.invoke).toHaveBeenCalled();
  });

  it('should handle validation errors from schema commands', async () => {
    const command = TestCommand.create({ value: 'test' });
    
    // Mock validation failure
    command.validate = () => err(new CommandValidationError('Test', ['Validation failed']));
    
    const result = await executor.executeCommandAsync(command);
    
    expect(result.isErr()).toBe(true);
    expect(result.error.code).toBe('COMMAND_VALIDATION_ERROR');
  });
});
```

## 3. Type Inference Tests

```typescript
// Test file: src/packages/core/src/schema-registry/__tests__/type-inference.test.ts

describe('Schema type inference', () => {
  it('should infer command payload types correctly', () => {
    const CreateUser = defineCommand({
      type: 'CreateUser',
      schema: z.object({
        name: z.string(),
        age: z.number(),
        email: z.string().email()
      }),
      aggregateType: 'User',
      handlers: {
        specifyPartitionKeys: () => PartitionKeys.generate('User'),
        validate: () => ok(undefined),
        handle: (data, aggregate) => {
          // TypeScript should know the exact type of data here
          type _CheckName = typeof data.name extends string ? true : false;
          type _CheckAge = typeof data.age extends number ? true : false;
          type _CheckEmail = typeof data.email extends string ? true : false;
          
          return ok([]);
        }
      }
    });
    
    // Type checking at compile time
    const command = CreateUser.create({
      name: 'John',
      age: 30,
      email: 'john@example.com'
    });
    
    // @ts-expect-error - missing required field
    CreateUser.create({ name: 'John', age: 30 });
    
    // @ts-expect-error - wrong type
    CreateUser.create({ name: 'John', age: '30', email: 'john@example.com' });
  });
});
```

## 4. Dapr Actor Communication Tests

```typescript
// Test file: src/packages/dapr/src/actors/__tests__/actor-communication.test.ts

describe('Dapr actor communication', () => {
  it('should handle actor not found gracefully', async () => {
    const mockDaprClient = {
      invoker: {
        invoke: jest.fn().mockRejectedValue(new Error('Actor not found'))
      }
    };
    
    const executor = new SekibanDaprExecutor(
      mockDaprClient as any,
      domainTypes,
      config
    );
    
    const result = await executor.executeCommandAsync(command);
    
    expect(result.isErr()).toBe(true);
    expect(result.error.message).toContain('Actor not found');
  });

  it('should retry on transient failures', async () => {
    let attempts = 0;
    const mockDaprClient = {
      invoker: {
        invoke: jest.fn().mockImplementation(() => {
          attempts++;
          if (attempts < 3) {
            throw new Error('Temporary failure');
          }
          return { success: true };
        })
      }
    };
    
    const executor = new SekibanDaprExecutor(
      mockDaprClient as any,
      domainTypes,
      { ...config, retryAttempts: 3 }
    );
    
    const result = await executor.executeCommandAsync(command);
    
    expect(result.isOk()).toBe(true);
    expect(attempts).toBe(3);
  });
});
```

## 5. Event Sourcing Consistency Tests

```typescript
// Test file: src/packages/core/src/__tests__/event-sourcing-consistency.test.ts

describe('Event sourcing consistency', () => {
  it('should maintain consistency across command-event-projection cycle', async () => {
    const executor = createInMemoryExecutor();
    
    // Create task
    const createResult = await executor.executeCommandAsync(
      CreateTask.create({ title: 'Test Task', priority: 'high' })
    );
    expect(createResult.isOk()).toBe(true);
    
    const taskId = createResult.value.aggregateId;
    
    // Update task
    const updateResult = await executor.executeCommandAsync(
      UpdateTask.create({ taskId, title: 'Updated Task' })
    );
    expect(updateResult.isOk()).toBe(true);
    
    // Query updated state
    const query = GetTaskById.create({ taskId });
    const queryResult = await executor.queryAsync(query);
    
    expect(queryResult.isOk()).toBe(true);
    expect(queryResult.value.payload.title).toBe('Updated Task');
    expect(queryResult.value.payload.priority).toBe('high'); // Original value retained
  });

  it('should handle concurrent updates correctly', async () => {
    const executor = createInMemoryExecutor();
    
    // Create task
    const createResult = await executor.executeCommandAsync(
      CreateTask.create({ title: 'Concurrent Test' })
    );
    const taskId = createResult.value.aggregateId;
    
    // Simulate concurrent updates
    const updates = Array.from({ length: 5 }, (_, i) => 
      executor.executeCommandAsync(
        UpdateTask.create({ 
          taskId, 
          title: `Update ${i}`,
          priority: i % 2 === 0 ? 'high' : 'low'
        })
      )
    );
    
    const results = await Promise.all(updates);
    
    // All should succeed (no optimistic locking in this case)
    expect(results.every(r => r.isOk())).toBe(true);
    
    // Final state should reflect last update
    const finalState = await executor.queryAsync(GetTaskById.create({ taskId }));
    expect(finalState.value.payload.title).toMatch(/Update \d/);
  });
});
```

## 6. Error Boundary Tests

```typescript
// Test file: src/packages/core/src/__tests__/error-boundaries.test.ts

describe('Error boundaries', () => {
  it('should not corrupt state on handler errors', async () => {
    const FailingCommand = defineCommand({
      type: 'FailingCommand',
      schema: z.object({ shouldFail: z.boolean() }),
      aggregateType: 'Test',
      handlers: {
        specifyPartitionKeys: () => PartitionKeys.generate('Test'),
        validate: () => ok(undefined),
        handle: (data) => {
          if (data.shouldFail) {
            throw new Error('Intentional failure');
          }
          return ok([]);
        }
      }
    });
    
    const executor = createInMemoryExecutor();
    
    // Execute failing command
    const result = await executor.executeCommandAsync(
      FailingCommand.create({ shouldFail: true })
    );
    
    expect(result.isErr()).toBe(true);
    
    // Verify system is still functional
    const healthCheck = await executor.executeCommandAsync(
      FailingCommand.create({ shouldFail: false })
    );
    
    expect(healthCheck.isOk()).toBe(true);
  });
});
```

## 7. Performance Regression Tests

```typescript
// Test file: src/packages/core/src/__tests__/performance.test.ts

describe('Performance regression tests', () => {
  it('should handle 1000 commands within acceptable time', async () => {
    const executor = createInMemoryExecutor();
    const startTime = performance.now();
    
    const commands = Array.from({ length: 1000 }, (_, i) => 
      CreateTask.create({ 
        title: `Task ${i}`,
        priority: ['low', 'medium', 'high'][i % 3] as any
      })
    );
    
    await Promise.all(commands.map(cmd => executor.executeCommandAsync(cmd)));
    
    const duration = performance.now() - startTime;
    
    // Should complete within 5 seconds
    expect(duration).toBeLessThan(5000);
    
    // Log for tracking
    console.log(`Created 1000 tasks in ${duration.toFixed(2)}ms`);
  });

  it('should efficiently project large event streams', async () => {
    const executor = createInMemoryExecutor();
    
    // Create aggregate with many events
    const createResult = await executor.executeCommandAsync(
      CreateTask.create({ title: 'Large Event Stream' })
    );
    const taskId = createResult.value.aggregateId;
    
    // Generate 100 update events
    for (let i = 0; i < 100; i++) {
      await executor.executeCommandAsync(
        UpdateTask.create({ 
          taskId, 
          title: `Update ${i}` 
        })
      );
    }
    
    // Measure projection time
    const startTime = performance.now();
    const result = await executor.queryAsync(GetTaskById.create({ taskId }));
    const projectionTime = performance.now() - startTime;
    
    expect(result.isOk()).toBe(true);
    expect(projectionTime).toBeLessThan(100); // Should project in < 100ms
    
    console.log(`Projected 101 events in ${projectionTime.toFixed(2)}ms`);
  });
});
```

## Test Execution Strategy

1. **Unit Tests First**: Focus on schema-command integration and type inference
2. **Integration Tests**: Test Dapr executor with mock clients
3. **End-to-End Tests**: Full stack tests with actual Dapr runtime
4. **Performance Tests**: Run separately with baseline tracking
5. **Regression Tests**: Add for each bug found during development

## Continuous Testing Improvements

1. **Test Coverage Targets**
   - Core package: >90%
   - Dapr package: >80%
   - Sample projects: >70%

2. **Automated Test Generation**
   - Generate tests from command/event schemas
   - Property-based testing for invariants
   - Snapshot testing for projections

3. **Test Environment Setup**
   - Docker compose for Dapr testing
   - In-memory implementations for fast tests
   - Parallel test execution configuration