import { InMemoryEventStore, StorageProviderType, Event, SortableUniqueId, PartitionKeys, EventRetrievalInfo, OptionalValue, SortableIdCondition } from '@sekiban/core';
import { MultiProjectorActor, initializeDaprContainer } from '@sekiban/dapr';
import { createTaskDomainTypes } from '@dapr-sample/domain';
import { ActorId } from '@dapr/dapr';

console.log('🧪 Testing MultiProjectorActor Projections Directly\n');

async function testProjections() {
  // Create in-memory event store
  const eventStore = new InMemoryEventStore({ 
    type: StorageProviderType.InMemory,
    enableLogging: false 
  });
  
  // Initialize domain types
  const domainTypes = createTaskDomainTypes();
  
  // Add test events to store
  console.log('📝 Creating test events...');
  const events = [];
  for (let i = 1; i <= 3; i++) {
    const taskId = `task-${i}`;
    const partitionKeys = PartitionKeys.existing(taskId, 'Task');
    const event = new Event(
      SortableUniqueId.create(),
      partitionKeys,
      'Task',
      'TaskCreated',
      1,
      {
        taskId,
        title: `Test Task ${i}`,
        description: `Description for task ${i}`,
        priority: i === 1 ? 'high' : 'medium',
        createdAt: new Date().toISOString()
      },
      { timestamp: new Date() }
    );
    events.push(event);
  }
  
  await (eventStore as any).saveEvents(events);
  console.log(`✅ Added ${events.length} events to store\n`);
  
  // Initialize Dapr container
  initializeDaprContainer({
    domainTypes,
    serviceProvider: {},
    actorProxyFactory: {} as any,
    serializationService: {},
    eventStore
  });
  
  // Test the projection logic manually
  console.log('🔍 Testing projection logic...\n');
  
  // Get events from store
  const retrievalInfo = new EventRetrievalInfo(
    OptionalValue.empty<string>(),
    OptionalValue.empty<any>(),
    OptionalValue.empty<string>(),
    SortableIdCondition.none(),
    OptionalValue.fromValue(100)
  );
  
  const eventsResult = await eventStore.getEvents(retrievalInfo);
  if (eventsResult.isOk()) {
    console.log(`📊 Retrieved ${eventsResult.value.length} events from store`);
    
    // Simulate what MultiProjectorActor does
    const projections: Record<string, any> = {};
    
    // Debug domain types structure
    console.log('Domain types structure:', {
      hasProjectorTypes: 'projectorTypes' in domainTypes,
      projectorTypesType: typeof domainTypes.projectorTypes,
      keys: Object.keys(domainTypes)
    });
    
    // Find the TaskProjector - check different possible structures
    let projectorType = null;
    
    // Check if it's a registry with getProjectorTypes
    if (domainTypes.projectorTypes && typeof domainTypes.projectorTypes.getProjectorTypes === 'function') {
      const projectorList = domainTypes.projectorTypes.getProjectorTypes();
      console.log('Projector list details:', projectorList);
      
      // The list contains objects with aggregateTypeName and projector
      const projectorWrapper = projectorList.find(
        (p: any) => {
          console.log('Checking projector:', { aggregateTypeName: p.aggregateTypeName, p });
          return p.aggregateTypeName === 'Task';
        }
      );
      
      if (projectorWrapper) {
        projectorType = projectorWrapper.projector;
      }
    }
    
    // If not found, check if TaskProjector is registered differently
    if (!projectorType && domainTypes.projectors) {
      console.log('Checking domainTypes.projectors:', Object.keys(domainTypes.projectors));
      projectorType = domainTypes.projectors.TaskProjector;
    }
    
    if (projectorType) {
      console.log(`✅ Found TaskProjector\n`);
      
      // The projectorType is already an instance with getInitialState and project methods
      const projector = projectorType;
      
      // Apply each event
      for (const event of eventsResult.value) {
        console.log(`  Applying event: ${event.type} for ${event.aggregateId}`);
        
        // Get or create projection
        let currentProjection = projections[event.aggregateId];
        
        if (!currentProjection) {
          const initialAggregate = projector.getInitialState(event.partitionKeys);
          currentProjection = {
            ...initialAggregate,
            payload: initialAggregate.payload
          };
        }
        
        // Project the event
        const result = projector.project(currentProjection, event);
        
        if (result.isOk()) {
          projections[event.aggregateId] = {
            id: event.aggregateId,
            aggregateType: result.value.aggregateType,
            version: result.value.version,
            payload: result.value.payload,
            partitionKeys: result.value.partitionKeys,
            createdAt: new Date().toISOString(),
            updatedAt: new Date().toISOString()
          };
          console.log(`    ✅ Projection updated`);
        } else {
          console.log(`    ❌ Projection failed:`, result.error);
        }
      }
      
      console.log(`\n📊 Final projections:`);
      console.log(JSON.stringify(projections, null, 2));
      
      // Test query logic
      console.log(`\n🔍 Testing query logic...`);
      const allProjections = Object.values(projections);
      console.log(`Total projections: ${allProjections.length}`);
      
      // Filter high priority tasks
      const highPriorityTasks = allProjections.filter((p: any) => 
        p.payload?.priority === 'high'
      );
      console.log(`High priority tasks: ${highPriorityTasks.length}`);
      
    } else {
      console.log('❌ TaskProjector not found in domain types');
    }
  } else {
    console.error('❌ Failed to retrieve events:', eventsResult.error);
  }
}

testProjections()
  .then(() => console.log('\n✨ Test completed'))
  .catch(console.error);