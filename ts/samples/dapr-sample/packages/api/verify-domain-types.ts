// Verification script for domain types registration
// This script mimics how server.ts uses createTaskDomainTypes

import { createTaskDomainTypes } from '@dapr-sample/domain';

console.log('=== Domain Types Registration Verification ===\n');

try {
  // Initialize domain types same way as server.ts line 123
  const domainTypes = createTaskDomainTypes();
  
  console.log('✅ createTaskDomainTypes() executed successfully');
  
  // Verify the structure
  console.log('\n📊 Domain Types Structure:');
  console.log(`- commands: ${typeof domainTypes.commands} (size: ${domainTypes.commands?.size || 'N/A'})`);
  console.log(`- events: ${typeof domainTypes.events} (size: ${domainTypes.events?.size || 'N/A'})`);
  console.log(`- projectors: ${typeof domainTypes.projectors} (size: ${domainTypes.projectors?.size || 'N/A'})`);
  
  // Test command registration
  console.log('\n📝 Commands Registered:');
  const expectedCommands = ['CreateTask', 'AssignTask', 'CompleteTask', 'UpdateTask', 'DeleteTask', 'RevertTaskCompletion'];
  let commandCount = 0;
  
  for (const cmd of expectedCommands) {
    const found = domainTypes.commands.has(cmd);
    console.log(`  ${cmd}: ${found ? '✅' : '❌'}`);
    if (found) commandCount++;
  }
  console.log(`  Total: ${commandCount}/${expectedCommands.length}`);
  
  // Test event registration
  console.log('\n📢 Events Registered:');
  const expectedEvents = ['TaskCreated', 'TaskAssigned', 'TaskCompleted', 'TaskUpdated', 'TaskDeleted', 'TaskCompletionReverted'];
  let eventCount = 0;
  
  for (const evt of expectedEvents) {
    const found = domainTypes.events.has(evt);
    console.log(`  ${evt}: ${found ? '✅' : '❌'}`);
    if (found) eventCount++;
  }
  console.log(`  Total: ${eventCount}/${expectedEvents.length}`);
  
  // Test projector registration
  console.log('\n🔧 Projectors Registered:');
  const hasTaskProjector = domainTypes.projectors.has('TaskProjector');
  console.log(`  TaskProjector: ${hasTaskProjector ? '✅' : '❌'}`);
  
  // Test lookup functions
  console.log('\n🔍 Lookup Functions:');
  const createTaskCmd = domainTypes.findCommandDefinition('CreateTask');
  console.log(`  findCommandDefinition('CreateTask'): ${createTaskCmd ? '✅' : '❌'}`);
  
  const taskCreatedEvt = domainTypes.findEventDefinition('TaskCreated');
  console.log(`  findEventDefinition('TaskCreated'): ${taskCreatedEvt ? '✅' : '❌'}`);
  
  const taskProjector = domainTypes.findProjectorDefinition('TaskProjector');
  console.log(`  findProjectorDefinition('TaskProjector'): ${taskProjector ? '✅' : '❌'}`);
  
  // Summary
  const allCommandsOk = commandCount === expectedCommands.length;
  const allEventsOk = eventCount === expectedEvents.length;
  const projectorOk = hasTaskProjector;
  const lookupOk = createTaskCmd && taskCreatedEvt && taskProjector;
  
  console.log('\n📊 Summary:');
  console.log(`  All commands registered: ${allCommandsOk ? '✅' : '❌'}`);
  console.log(`  All events registered: ${allEventsOk ? '✅' : '❌'}`);
  console.log(`  Projector registered: ${projectorOk ? '✅' : '❌'}`);
  console.log(`  Lookup functions work: ${lookupOk ? '✅' : '❌'}`);
  
  const allOk = allCommandsOk && allEventsOk && projectorOk && lookupOk;
  console.log(`\n${allOk ? '✅ All tests passed!' : '❌ Some tests failed!'}`);
  
  process.exit(allOk ? 0 : 1);
  
} catch (error) {
  console.error('\n❌ Error:', error);
  process.exit(1);
}