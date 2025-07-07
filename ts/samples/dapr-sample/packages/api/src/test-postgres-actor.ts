import { createPostgresActor } from './actors/aggregate-actor-postgres.js';
import { PartitionKeys } from '@sekiban/core';
import { Pool } from 'pg';

// Create connection to verify data
const pool = new Pool({
  host: 'localhost',
  port: 5432,
  database: 'sekiban_events',
  user: 'sekiban',
  password: 'sekiban_password'
});

async function verifyEventsInDatabase(aggregateId: string) {
  const query = `
    SELECT 
      id, 
      sortable_unique_id,
      payload,
      payload_type_name as event_type,
      version,
      aggregate_id,
      root_partition_key,
      timestamp,
      causation_id,
      correlation_id,
      executed_user
    FROM events 
    WHERE aggregate_id = $1
    ORDER BY version ASC
  `;
  
  const result = await pool.query(query, [aggregateId]);
  
  console.log('\nEvents in database:');
  console.log('===================');
  result.rows.forEach((row, index) => {
    console.log(`\nEvent ${index + 1}:`);
    console.log('  ID:', row.id);
    console.log('  Sortable Unique ID:', row.sortable_unique_id);
    console.log('  Event Type:', row.event_type);
    console.log('  Version:', row.version);
    console.log('  Aggregate ID:', row.aggregate_id);
    console.log('  Root Partition Key:', row.root_partition_key);
    console.log('  Timestamp:', row.timestamp);
    console.log('  Payload:', JSON.stringify(row.payload, null, 2));
    console.log('  Causation ID:', row.causation_id);
    console.log('  Correlation ID:', row.correlation_id);
    console.log('  Executed User:', row.executed_user);
  });
  
  return result.rows;
}

async function cleanupDatabase(aggregateId: string) {
  await pool.query('DELETE FROM events WHERE aggregate_id = $1', [aggregateId]);
  console.log(`Cleaned up events for aggregate ${aggregateId}`);
}

async function testPostgresActor() {
  console.log('Testing PostgreSQL actor implementation...\n');
  
  // Generate unique aggregate ID for this test (must be UUID)
  const testAggregateId = crypto.randomUUID();
  const rootPartition = 'test-partition';
  const actorId = `${rootPartition}@${testAggregateId}@Task`;
  
  console.log('Test configuration:');
  console.log('  Actor ID:', actorId);
  console.log('  Aggregate ID:', testAggregateId);
  console.log('  Root Partition:', rootPartition);
  
  try {
    // Create an actor instance
    const actor = await createPostgresActor(actorId);
    
    // Test 1: Create Task
    console.log('\n1. Testing CreateTask command...');
    const partitionKeys = {
      aggregateId: testAggregateId,
      group: 'Task',
      rootPartitionKey: rootPartition,
      partitionKey: `${rootPartition}@${testAggregateId}@Task`
    };
    
    const createResult = await actor.executeCommandAsync({
      commandType: 'CreateTask',
      commandData: {
        taskId: testAggregateId,
        title: 'PostgreSQL Test Task',
        description: 'This task is saved to PostgreSQL',
        priority: 'high'
      },
      partitionKeys,
      metadata: { 
        userId: 'test-user',
        causationId: 'cmd-create-1',
        correlationId: 'flow-1'
      }
    });
    
    console.log('Create result:', createResult);
    
    // Verify in database
    await new Promise(resolve => setTimeout(resolve, 500)); // Small delay to ensure write completes
    const eventsAfterCreate = await verifyEventsInDatabase(testAggregateId);
    console.log(`\nTotal events after create: ${eventsAfterCreate.length}`);
    
    // Test 2: Query after creation
    console.log('\n2. Testing query after creation...');
    const queryResult1 = await actor.queryAsync({
      queryType: 'GetTask',
      taskId: testAggregateId
    });
    
    console.log('Query result:', queryResult1);
    
    // Test 3: Assign Task
    console.log('\n3. Testing AssignTask command...');
    const assignResult = await actor.executeCommandAsync({
      commandType: 'AssignTask',
      commandData: {
        taskId: testAggregateId,
        assignedTo: 'postgres.user@example.com'
      },
      partitionKeys,
      metadata: { 
        userId: 'test-user',
        causationId: 'cmd-assign-1',
        correlationId: 'flow-1'
      }
    });
    
    console.log('Assign result:', assignResult);
    
    // Verify in database
    await new Promise(resolve => setTimeout(resolve, 500));
    const eventsAfterAssign = await verifyEventsInDatabase(testAggregateId);
    console.log(`\nTotal events after assign: ${eventsAfterAssign.length}`);
    
    // Test 4: Complete Task
    console.log('\n4. Testing CompleteTask command...');
    const completeResult = await actor.executeCommandAsync({
      commandType: 'CompleteTask',
      commandData: {
        taskId: testAggregateId
      },
      partitionKeys,
      metadata: { 
        userId: 'admin-user',
        causationId: 'cmd-complete-1',
        correlationId: 'flow-1'
      }
    });
    
    console.log('Complete result:', completeResult);
    
    // Final verification
    await new Promise(resolve => setTimeout(resolve, 500));
    const finalEvents = await verifyEventsInDatabase(testAggregateId);
    console.log(`\nTotal events after complete: ${finalEvents.length}`);
    
    // Test 5: Final query
    console.log('\n5. Testing final query...');
    const finalQueryResult = await actor.queryAsync({
      queryType: 'GetTask',
      taskId: testAggregateId
    });
    
    console.log('Final query result:', finalQueryResult);
    
    // Test 6: Get all events through actor
    console.log('\n6. Testing getAllEventsAsync...');
    const allEvents = await actor.getAllEventsAsync();
    console.log('Events retrieved through actor:', allEvents.length);
    
    // Test 7: Test concurrency control
    console.log('\n7. Testing concurrency control...');
    const duplicateResult = await actor.executeCommandAsync({
      commandType: 'CreateTask',
      commandData: {
        taskId: testAggregateId,
        title: 'Duplicate Task',
        description: 'This should fail'
      },
      partitionKeys,
      metadata: { userId: 'test-user' }
    });
    
    console.log('Duplicate create result:', duplicateResult);
    
    console.log('\nPostgreSQL actor test completed successfully!');
    
    // Cleanup
    console.log('\nCleaning up test data...');
    await cleanupDatabase(testAggregateId);
    
  } catch (error) {
    console.error('Test failed:', error);
    // Try to cleanup even on error
    try {
      await cleanupDatabase(testAggregateId);
    } catch (cleanupError) {
      console.error('Cleanup failed:', cleanupError);
    }
  } finally {
    await pool.end();
  }
}

testPostgresActor().catch(console.error);