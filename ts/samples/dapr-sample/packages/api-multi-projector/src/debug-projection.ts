import { InMemoryEventStore, StorageProviderType, Event, SortableUniqueId, PartitionKeys } from '@sekiban/core';
import { createTaskDomainTypes } from '@dapr-sample/domain';

console.log('🔍 Debugging Projection Issue\n');

async function debugProjection() {
  // Create event
  const taskId = 'debug-task-1';
  const partitionKeys = PartitionKeys.existing(taskId, 'Task');
  const event = new Event(
    SortableUniqueId.create(),
    partitionKeys,
    'Task',
    'TaskCreated',
    1,
    {
      taskId,
      title: 'Debug Task',
      description: 'Testing projection',
      priority: 'high',
      createdAt: new Date().toISOString()
    },
    { timestamp: new Date() }
  );
  
  console.log('📝 Created event:', {
    type: event.type,
    payload: event.payload,
    aggregateId: event.aggregateId
  });
  
  // Get domain types
  const domainTypes = createTaskDomainTypes();
  
  // Find TaskProjector
  console.log('\n🔍 Looking for TaskProjector...');
  if (domainTypes.projectorTypes && typeof domainTypes.projectorTypes.getProjectorTypes === 'function') {
    const projectorList = domainTypes.projectorTypes.getProjectorTypes();
    console.log('Available projectors:', projectorList.map((p: any) => ({
      aggregateTypeName: p.aggregateTypeName,
      projectorType: typeof p.projector,
      methods: p.projector ? Object.getOwnPropertyNames(p.projector) : []
    })));
    
    const taskProjectorWrapper = projectorList.find((p: any) => p.aggregateTypeName === 'Task');
    
    if (taskProjectorWrapper) {
      const projector = taskProjectorWrapper.projector;
      console.log('\n✅ Found TaskProjector');
      console.log('Projector methods:', Object.getOwnPropertyNames(projector));
      console.log('Projector type:', projector.aggregateType);
      
      // Get initial state
      const initialState = projector.getInitialState(partitionKeys);
      console.log('\n📊 Initial state:', {
        aggregateType: initialState.aggregateType,
        version: initialState.version,
        payload: initialState.payload,
        payloadType: initialState.payload?.aggregateType || 'unknown'
      });
      
      // Try to project the event
      console.log('\n🎯 Projecting event...');
      console.log('Event to project:', {
        type: event.type,
        payload: event.payload
      });
      
      const projectionResult = projector.project(initialState, event);
      
      if (projectionResult.isOk()) {
        const projected = projectionResult.value;
        console.log('\n✅ Projection successful!');
        console.log('Result:', {
          aggregateType: projected.aggregateType,
          version: projected.version,
          payload: projected.payload,
          payloadType: projected.payload?.aggregateType || 'unknown'
        });
        
        // Check if it's still empty
        if (projected.payload?._empty) {
          console.log('\n❌ Payload is still empty!');
          
          // Let's check the projector's projection functions
          console.log('\n🔍 Checking projector projections...');
          if (projector.projections) {
            console.log('Available projections:', Object.keys(projector.projections));
          }
          
          // Try to see if we can access the defineProjector result directly
          console.log('\n🔍 Checking projector internals...');
          console.log('Projector structure:', Object.keys(projector));
          
          // Let's also check if the event type matches
          console.log('\n🔍 Event type matching...');
          console.log('Event type:', event.type);
          console.log('Event eventType:', (event as any).eventType);
          
        } else {
          console.log('\n✅ Payload has data:', projected.payload);
        }
      } else {
        console.log('\n❌ Projection failed:', projectionResult.error);
      }
    } else {
      console.log('❌ TaskProjector not found');
    }
  }
}

debugProjection().catch(console.error);