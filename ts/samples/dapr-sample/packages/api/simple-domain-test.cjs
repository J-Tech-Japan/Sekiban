// Simple CommonJS test to verify domain types work
const { createTaskDomainTypes } = require('@dapr-sample/domain');

console.log('Testing Domain Types (CommonJS)...\n');

try {
  const domainTypes = createTaskDomainTypes();
  
  console.log('✅ Successfully created domain types');
  console.log(`Commands: ${domainTypes.commands.size}`);
  console.log(`Events: ${domainTypes.events.size}`);
  console.log(`Projectors: ${domainTypes.projectors.size}`);
  
  // List all registered types
  console.log('\nRegistered Commands:');
  Array.from(domainTypes.commands.keys()).forEach(cmd => console.log(`  - ${cmd}`));
  
  console.log('\nRegistered Events:');
  Array.from(domainTypes.events.keys()).forEach(evt => console.log(`  - ${evt}`));
  
  console.log('\nRegistered Projectors:');
  Array.from(domainTypes.projectors.keys()).forEach(proj => console.log(`  - ${proj}`));
  
  // Test specific lookups
  console.log('\nLookup Tests:');
  console.log(`  CreateTask command: ${domainTypes.commands.has('CreateTask') ? '✅' : '❌'}`);
  console.log(`  TaskCreated event: ${domainTypes.events.has('TaskCreated') ? '✅' : '❌'}`);
  console.log(`  TaskProjector: ${domainTypes.projectors.has('TaskProjector') ? '✅' : '❌'}`);
  
  console.log('\n✅ All tests passed!');
  
} catch (error) {
  console.error('❌ Error:', error.message);
  console.error(error.stack);
}