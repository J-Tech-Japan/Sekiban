import { CosmosClient } from '@azure/cosmos';
import { CosmosEventStore } from './src/cosmos-event-store';
import { 
  PartitionKeys, 
  SortableUniqueId, 
  createEvent,
  createEventMetadata,
  defineEvent
} from '@sekiban/core';
import { z } from 'zod';

// Define a test event
const WeatherForecastInputted = defineEvent({
  type: 'WeatherForecastInputted',
  schema: z.object({
    location: z.string(),
    date: z.string(),
    temperatureC: z.object({
      value: z.number()
    }),
    summary: z.string()
  })
});

async function testCosmosFormat() {
  // You'll need to set these environment variables
  const connectionString = process.env.COSMOS_CONNECTION_STRING;
  const databaseName = process.env.COSMOS_DATABASE_NAME || 'sekiban_test';
  
  if (!connectionString) {
    console.error('Please set COSMOS_CONNECTION_STRING environment variable');
    process.exit(1);
  }

  // Extract endpoint and key from connection string
  const endpoint = connectionString.match(/AccountEndpoint=([^;]+);/)?.[1];
  const key = connectionString.match(/AccountKey=([^;]+);/)?.[1];
  
  if (!endpoint || !key) {
    console.error('Invalid connection string format');
    process.exit(1);
  }

  // Create CosmosDB client
  const client = new CosmosClient({ endpoint, key });
  
  // Create or get database
  const { database } = await client.databases.createIfNotExists({
    id: databaseName
  });
  
  // Create event store
  const eventStore = new CosmosEventStore(database);
  
  // Initialize
  const initResult = await eventStore.initialize();
  if (initResult.isErr()) {
    console.error('Failed to initialize:', initResult.error);
    process.exit(1);
  }
  
  console.log('✅ Cosmos DB initialized');
  
  // Create a test event
  const event = createEvent({
    id: SortableUniqueId.generate(),
    partitionKeys: PartitionKeys.create(
      "01981ca8-31fc-7841-b82c-f4c6cabb8fba",
      "WeatherForecastProjector",
      "default"
    ),
    aggregateType: "WeatherForecast",
    eventType: "WeatherForecastInputted",
    version: 1,
    payload: {
      location: "aaaaa",
      date: "2025-07-18",
      temperatureC: {
        value: 20
      },
      summary: "Chilly"
    },
    metadata: createEventMetadata({
      timestamp: new Date("2025-07-18T08:30:42.0647355Z"),
      correlationId: "b5841b69-42ab-473e-be98-183af0cba0f7",
      causationId: "b5841b69-42ab-473e-be98-183af0cba0f7",
      userId: "system"
    })
  });
  
  console.log('📝 Saving event...');
  
  // Save the event
  await eventStore.saveEvents([event]);
  
  console.log('✅ Event saved!');
  console.log('Event ID (aggregateId):', event.partitionKeys.aggregateId);
  console.log('SortableUniqueId:', event.id.value);
  
  // Query the event back
  console.log('\n📖 Querying events...');
  
  const eventRetrievalInfo = {
    aggregateId: { 
      hasValueProperty: true, 
      getValue: () => "01981ca8-31fc-7841-b82c-f4c6cabb8fba" 
    },
    rootPartitionKey: { 
      hasValueProperty: true, 
      getValue: () => "default" 
    },
    aggregateStream: { hasValueProperty: false },
    sortableIdCondition: null,
    maxCount: { hasValueProperty: false }
  };
  
  const result = await eventStore.getEvents(eventRetrievalInfo);
  
  if (result.isErr()) {
    console.error('Failed to query:', result.error);
    process.exit(1);
  }
  
  console.log('✅ Found', result.value.length, 'events');
  
  if (result.value.length > 0) {
    const queriedEvent = result.value[0];
    console.log('\n📄 Event structure in Cosmos DB:');
    console.log('- id:', event.partitionKeys.aggregateId);
    console.log('- sortableUniqueId:', queriedEvent.id.value);
    console.log('- partitionKey:', `default@WeatherForecastProjector@${event.partitionKeys.aggregateId}`);
    console.log('- payloadTypeName:', queriedEvent.eventType);
    console.log('- timeStamp:', queriedEvent.metadata.timestamp?.toISOString());
    console.log('- payload:', JSON.stringify(queriedEvent.payload, null, 2));
  }
  
  // Close the event store
  await eventStore.close();
  
  console.log('\n✅ Test completed!');
}

// Run the test
testCosmosFormat().catch(console.error);