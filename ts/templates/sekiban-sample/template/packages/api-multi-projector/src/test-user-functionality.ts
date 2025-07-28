/**
 * Simple test script to verify user creation, retrieval, and list queries
 * This bypasses complex TypeScript compilation issues by using simpler direct testing
 */

import { InMemoryEventStore, StorageProviderType, SortableUniqueId, PartitionKeys, createEvent, createEventMetadata } from '@sekiban/core';
import { createTaskDomainTypes } from '@dapr-sample/domain';

console.log('🧪 Testing User Functionality (Direct Domain Logic)\n');

async function testUserFunctionality() {
  console.log('1️⃣ Testing User Creation...');
  
  // Create event store
  const eventStore = new InMemoryEventStore({ type: StorageProviderType.InMemory });
  await eventStore.initialize();
  
  // Get domain types
  const domainTypes = createTaskDomainTypes();
  
  // Create a user creation event
  const userId = SortableUniqueId.create().value;
  const partitionKeys = PartitionKeys.existing(userId, 'User');
  
  const userCreatedEvent = createEvent({
    id: SortableUniqueId.create(),
    partitionKeys,
    aggregateType: 'User',
    eventType: 'UserCreated',
    version: 1,
    payload: {
      userId: userId,
      name: 'Test User',
      email: 'test@example.com'
    },
    metadata: createEventMetadata({ timestamp: new Date() })
  });
  
  console.log('📝 Created UserCreated event:', {
    aggregateId: userCreatedEvent.aggregateId,
    eventType: userCreatedEvent.eventType,
    payload: userCreatedEvent.payload
  });
  
  // Save event to store
  await eventStore.saveEvents([userCreatedEvent]);
  console.log('✅ Event saved to store\n');
  
  console.log('2️⃣ Testing User Aggregate Retrieval...');
  
  // Try to get the projector for User
  if (domainTypes.projectorTypes && typeof domainTypes.projectorTypes.getProjectorTypes === 'function') {
    const projectorList = domainTypes.projectorTypes.getProjectorTypes();
    console.log('Available projectors:', projectorList.map((p) => p.aggregateTypeName));
    console.log('Projector details:', projectorList.map((p) => ({
      name: p.aggregateTypeName,
      projector: !!p.projector,
      projectorType: typeof p.projector,
      projections: p.projector ? Object.keys((p.projector as any).projections || {}) : []
    })));
    
    const userProjectorWrapper = projectorList.find((p) => p.aggregateTypeName === 'User');
    
    if (userProjectorWrapper) {
      const userProjector = userProjectorWrapper.projector;
      console.log('✅ Found UserProjector');
      
      // Get initial state
      const initialState = userProjector.getInitialState(partitionKeys);
      console.log('📊 Initial state type:', initialState.payload?.aggregateType || 'empty');
      
      // Debug the event and projector
      console.log('🔍 Debug info:');
      console.log('  - Event type:', userCreatedEvent.eventType);
      console.log('  - Event payload:', userCreatedEvent.payload);
      console.log('  - UserProjector structure:', Object.keys(userProjector));
      console.log('  - UserProjector projections:', undefined); // defineProjector doesn't expose projections directly
      console.log('  - Available projections:', []); // projections are internal to the projector
      
      // Project the event
      const projectionResult = userProjector.project(initialState, userCreatedEvent);
      
      if (projectionResult.isOk()) {
        const userAggregate = projectionResult.value;
        console.log('✅ User aggregate projected successfully:');
        const userPayload = userAggregate.payload as any;
        console.log('  - User ID:', userPayload?.userId || 'N/A');
        console.log('  - Name:', userPayload?.name || 'N/A');
        console.log('  - Email:', userPayload?.email || 'N/A');
        console.log('  - Version:', userAggregate.version);
      } else {
        console.log('❌ Failed to project user aggregate:', projectionResult.error);
      }
    } else {
      console.log('❌ UserProjector not found');
    }
  } else {
    console.log('❌ No projector types found');
  }
  
  console.log('\n3️⃣ Testing Task List Queries...');
  
  // Create some task events for list testing
  const tasks = [
    { id: 'task-1', title: 'First Task', priority: 'high' },
    { id: 'task-2', title: 'Second Task', priority: 'medium' },
    { id: 'task-3', title: 'Third Task', priority: 'low' }
  ];
  
  const taskEvents = [];
  for (const task of tasks) {
    const taskPartitionKeys = PartitionKeys.existing(task.id, 'Task');
    const taskEvent = createEvent({
      id: SortableUniqueId.create(),
      partitionKeys: taskPartitionKeys,
      aggregateType: 'Task',
      eventType: 'TaskCreated',
      version: 1,
      payload: {
        taskId: task.id,
        title: task.title,
        description: `Description for ${task.title}`,
        priority: task.priority,
        createdAt: new Date().toISOString()
      },
      metadata: createEventMetadata({ timestamp: new Date() })
    });
    taskEvents.push(taskEvent);
  }
  
  // Save task events
  await eventStore.saveEvents(taskEvents);
  console.log(`📝 Created ${taskEvents.length} task events`);
  
  // Test task projection
  if (domainTypes.projectorTypes && typeof domainTypes.projectorTypes.getProjectorTypes === 'function') {
    const projectorList = domainTypes.projectorTypes.getProjectorTypes();
    const taskProjectorWrapper = projectorList.find((p) => p.aggregateTypeName === 'Task');
    
    if (taskProjectorWrapper) {
      const taskProjector = taskProjectorWrapper.projector;
      console.log('✅ Found TaskProjector');
      console.log('TaskProjector structure:', Object.keys(taskProjector));
      console.log('TaskProjector projections:', undefined); // defineProjector doesn't expose projections directly
      
      const taskProjections: any[] = [];
      
      // Project each task event
      for (const taskEvent of taskEvents) {
        const initialState = taskProjector.getInitialState(taskEvent.partitionKeys);
        const projectionResult = taskProjector.project(initialState, taskEvent);
        
        if (projectionResult.isOk()) {
          taskProjections.push({
            id: taskEvent.aggregateId,
            ...projectionResult.value.payload
          });
        }
      }
      
      console.log(`✅ Successfully projected ${taskProjections.length} tasks:`);
      taskProjections.forEach((task, index) => {
        console.log(`  ${index + 1}. ${task.title} (${task.priority})`);
      });
      
      // Test filtering (simulate queries)
      const highPriorityTasks = taskProjections.filter(task => task.priority === 'high');
      console.log(`\n🔍 High priority tasks: ${highPriorityTasks.length}`);
      
    } else {
      console.log('❌ TaskProjector not found');
    }
  }
  
  console.log('\n✨ User functionality test completed!');
}

testUserFunctionality().catch(console.error);