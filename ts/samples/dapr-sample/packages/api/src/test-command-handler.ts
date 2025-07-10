import { globalRegistry, ok, err } from '@sekiban/core';
import { createTaskDomainTypes } from '@dapr-sample/domain';

// Initialize domain types to register commands
console.log('[TEST] Initializing domain types...');
const domainTypes = createTaskDomainTypes();

// Get the command definition from global registry
console.log('[TEST] Getting CreateTask command from registry...');
const commandDef = globalRegistry.getCommand('CreateTask');

console.log('[TEST] Command definition:', commandDef ? Object.keys(commandDef) : 'not found');

if (!commandDef || !commandDef.create) {
  console.error('[TEST] Command definition or create function not found!');
  process.exit(1);
}

// Create a command instance
console.log('[TEST] Creating command instance...');
const commandInstance = commandDef.create({
  title: "Test Task with PostgreSQL - Direct Handler Test",
  description: "Testing if command handler works properly"
});

console.log('[TEST] Command instance created:', typeof commandInstance);
console.log('[TEST] Command instance has handle?', commandInstance && 'handle' in commandInstance);

if (!commandInstance || typeof commandInstance.handle !== 'function') {
  console.error('[TEST] Command instance does not have handle method!');
  if (commandInstance) {
    console.error('[TEST] Available methods:', Object.getOwnPropertyNames(Object.getPrototypeOf(commandInstance)));
  }
  process.exit(1);
}

// Create a simple test context
const testContext = {
  originalSortableUniqueId: '',
  events: [] as any[],
  partitionKeys: {
    aggregateId: 'test-' + Date.now(),
    group: 'Task',
    rootPartitionKey: 'default'
  },
  metadata: {
    commandId: 'test-cmd-' + Date.now(),
    timestamp: new Date()
  },
  getPartitionKeys: function() { return this.partitionKeys; },
  getNextVersion: function() { return 1; },
  getCurrentVersion: function() { return 0; },
  appendEvent: function(eventPayload: any) {
    console.log('[TEST] Event appended:', eventPayload);
    this.events.push({ payload: eventPayload });
    return ok({ id: 'test-event-' + Date.now() });
  },
  getService: () => err({ message: 'Service resolution not implemented' } as any),
  getAggregate: () => ok({
    partitionKeys: testContext.partitionKeys,
    aggregateType: 'Task',
    version: 0,
    payload: { aggregateType: 'EmptyAggregate' },
    lastSortableUniqueId: null
  })
};

console.log('[TEST] Calling command handler...');

// Call the handle method on the command instance
// The first parameter is the command data (already in the instance)
// The second parameter is the context
const result = commandInstance.handle({
  title: "Test Task with PostgreSQL - Direct Handler Test",
  description: "Testing if command handler works properly"
}, testContext);

console.log('[TEST] Handle result:', result);

if (result && result.isOk && !result.isOk()) {
  console.error('[TEST] Command handler failed:', result.error);
  process.exit(1);
}

console.log('[TEST] Success! Events generated:', testContext.events);
console.log('[TEST] Command handler is working properly!');