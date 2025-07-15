import { createTaskDomainTypes } from '@dapr-sample/domain';

async function testSetup() {
  console.log('Testing domain types setup...');
  
  try {
    const domainTypes = createTaskDomainTypes();
    console.log('Domain types created successfully');
    
    console.log('Event types:', domainTypes.eventTypes.getEventTypes());
    console.log('Command types:', domainTypes.commandTypes.getCommandTypes());
    console.log('Projector types:', domainTypes.projectorTypes.getProjectorTypes());
    
    const projectorTypes = domainTypes.projectorTypes.getProjectorTypes();
    console.log(`Found ${projectorTypes.length} projector(s)`);
    
    if (projectorTypes.length === 0) {
      console.error('ERROR: No projectors found!');
    } else {
      console.log('SUCCESS: Projectors are registered correctly');
    }
    
  } catch (error) {
    console.error('Error creating domain types:', error);
  }
}

testSetup();