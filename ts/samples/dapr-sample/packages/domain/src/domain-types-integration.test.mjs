import assert from 'node:assert';
import { createTaskDomainTypes } from '../dist/domain-types.js';

console.log('Testing Domain Types Registration...');

try {
  const domainTypes = createTaskDomainTypes();
  
  // Test Commands Registration
  console.log('\n=== Testing Commands ===');
  const expectedCommands = [
    'CreateTask',
    'AssignTask',
    'CompleteTask',
    'UpdateTask',
    'DeleteTask',
    'RevertTaskCompletion'
  ];
  
  console.log(`Total commands registered: ${domainTypes.commands.size}`);
  assert.strictEqual(domainTypes.commands.size, 6, 'Should have 6 commands registered');
  
  expectedCommands.forEach(commandName => {
    const hasCommand = domainTypes.commands.has(commandName);
    console.log(`- ${commandName}: ${hasCommand ? '✓' : '✗'}`);
    assert.strictEqual(hasCommand, true, `Command '${commandName}' should be registered`);
  });
  
  // Test Events Registration
  console.log('\n=== Testing Events ===');
  const expectedEvents = [
    'TaskCreated',
    'TaskAssigned',
    'TaskCompleted',
    'TaskUpdated',
    'TaskDeleted',
    'TaskCompletionReverted'
  ];
  
  console.log(`Total events registered: ${domainTypes.events.size}`);
  assert.strictEqual(domainTypes.events.size, 6, 'Should have 6 events registered');
  
  expectedEvents.forEach(eventName => {
    const hasEvent = domainTypes.events.has(eventName);
    console.log(`- ${eventName}: ${hasEvent ? '✓' : '✗'}`);
    assert.strictEqual(hasEvent, true, `Event '${eventName}' should be registered`);
  });
  
  // Test Projectors Registration
  console.log('\n=== Testing Projectors ===');
  console.log(`Total projectors registered: ${domainTypes.projectors.size}`);
  assert.strictEqual(domainTypes.projectors.size, 1, 'Should have 1 projector registered');
  
  const hasTaskProjector = domainTypes.projectors.has('TaskProjector');
  console.log(`- TaskProjector: ${hasTaskProjector ? '✓' : '✗'}`);
  assert.strictEqual(hasTaskProjector, true, 'TaskProjector should be registered');
  
  // Test Lookup Functions
  console.log('\n=== Testing Lookup Functions ===');
  
  // Test command lookup
  const createTaskDef = domainTypes.findCommandDefinition('CreateTask');
  console.log(`- findCommandDefinition('CreateTask'): ${createTaskDef ? '✓' : '✗'}`);
  assert.ok(createTaskDef, 'Should find CreateTask command definition');
  assert.strictEqual(createTaskDef.name, 'CreateTask', 'Command name should match');
  
  // Test event lookup
  const taskCreatedDef = domainTypes.findEventDefinition('TaskCreated');
  console.log(`- findEventDefinition('TaskCreated'): ${taskCreatedDef ? '✓' : '✗'}`);
  assert.ok(taskCreatedDef, 'Should find TaskCreated event definition');
  assert.strictEqual(taskCreatedDef.name, 'TaskCreated', 'Event name should match');
  
  // Test projector lookup
  const taskProjectorDef = domainTypes.findProjectorDefinition('TaskProjector');
  console.log(`- findProjectorDefinition('TaskProjector'): ${taskProjectorDef ? '✓' : '✗'}`);
  assert.ok(taskProjectorDef, 'Should find TaskProjector definition');
  assert.strictEqual(taskProjectorDef.name, 'TaskProjector', 'Projector name should match');
  
  // Test non-existent lookups
  const nonExistentCommand = domainTypes.findCommandDefinition('NonExistentCommand');
  console.log(`- findCommandDefinition('NonExistentCommand'): ${!nonExistentCommand ? '✓' : '✗'}`);
  assert.strictEqual(nonExistentCommand, undefined, 'Should return undefined for non-existent command');
  
  const nonExistentEvent = domainTypes.findEventDefinition('NonExistentEvent');
  console.log(`- findEventDefinition('NonExistentEvent'): ${!nonExistentEvent ? '✓' : '✗'}`);
  assert.strictEqual(nonExistentEvent, undefined, 'Should return undefined for non-existent event');
  
  const nonExistentProjector = domainTypes.findProjectorDefinition('NonExistentProjector');
  console.log(`- findProjectorDefinition('NonExistentProjector'): ${!nonExistentProjector ? '✓' : '✗'}`);
  assert.strictEqual(nonExistentProjector, undefined, 'Should return undefined for non-existent projector');
  
  console.log('\n✅ All tests passed!');
  
} catch (error) {
  console.error('\n❌ Test failed:', error.message);
  console.error(error.stack);
  process.exit(1);
}