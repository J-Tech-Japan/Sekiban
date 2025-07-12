import { globalRegistry } from '@sekiban/core';
import { taskDomainTypes } from './packages/domain/src/index.js';

// Initialize domain types
taskDomainTypes.register();

// Get the CreateTask command definition
const commandDef = globalRegistry.getCommand('CreateTask');

console.log('Command definition:', commandDef);
console.log('Command def keys:', commandDef ? Object.keys(commandDef) : 'null');

// Create a command instance
const commandData = {
  title: 'Debug Test',
  description: 'Testing command handler',
  priority: 'high' as const
};

if (commandDef && commandDef.create) {
  const commandInstance = commandDef.create(commandData);
  console.log('Command instance:', commandInstance);
  console.log('Command instance type:', typeof commandInstance);
  console.log('Has handle?', typeof commandInstance.handle === 'function');
  
  // Create a mock context
  const events: any[] = [];
  const context = {
    aggregateId: 'test-id',
    aggregate: null,
    getPartitionKeys: () => ({ aggregateId: 'test-id', group: 'Task' }),
    getAggregate: () => ({ isOk: () => true, value: null }),
    appendEvent: (event: any) => {
      console.log('appendEvent called with:', event);
      events.push(event);
      return { isOk: () => true, value: event };
    },
    getService: () => ({ isErr: () => true, error: { message: 'No service' } })
  };
  
  // Call handle
  console.log('Calling handle...');
  try {
    const result = commandInstance.handle(commandData, context);
    console.log('Handle result:', result);
    console.log('Is Result type?', result && typeof result === 'object' && 'isOk' in result);
    if (result && result.isOk) {
      console.log('Result is OK:', result.isOk());
      console.log('Result value:', result.value);
    }
  } catch (error) {
    console.error('Handle error:', error);
  }
  
  console.log('Events collected:', events);
}