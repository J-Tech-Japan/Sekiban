import { DaprClient, HttpMethod } from '@dapr/dapr';
import { v4 as uuidv4 } from 'uuid';

async function createTestData() {
  console.log('🌱 Creating test data...\n');

  const daprClient = new DaprClient({
    daprHost: "127.0.0.1",
    daprPort: "3500" // Main API port
  });

  try {
    // Create tasks through the main API
    const tasks = [
      {
        title: "Implement user authentication",
        description: "Add JWT-based authentication to the API",
        assigneeEmail: "developer@example.com"
      },
      {
        title: "Write unit tests",
        description: "Add comprehensive unit tests for all services",
        assigneeEmail: "qa@example.com"
      },
      {
        title: "Deploy to production",
        description: "Set up CI/CD pipeline and deploy to production",
        assigneeEmail: "devops@example.com"
      }
    ];

    console.log('📝 Creating tasks...');
    
    for (const task of tasks) {
      const taskId = uuidv4();
      
      // Create task
      const createResponse = await daprClient.invoker.invoke(
        'dapr-sample-api',
        'api/v1/tasks',
        HttpMethod.POST,
        {
          taskId,
          ...task
        }
      );
      
      console.log(`✅ Created task: ${task.title} (ID: ${taskId})`);
      
      // Assign task
      await daprClient.invoker.invoke(
        'dapr-sample-api',
        `api/v1/tasks/${taskId}/assign`,
        HttpMethod.POST,
        {
          assigneeEmail: task.assigneeEmail
        }
      );
      
      console.log(`✅ Assigned task to: ${task.assigneeEmail}`);
      
      // Complete the first task
      if (tasks.indexOf(task) === 0) {
        await daprClient.invoker.invoke(
          'dapr-sample-api',
          `api/v1/tasks/${taskId}/complete`,
          HttpMethod.POST,
          {}
        );
        console.log(`✅ Completed task: ${task.title}`);
      }
    }
    
    console.log('\n🎉 Test data created successfully!');
    
    // Wait a bit for events to propagate
    console.log('\n⏳ Waiting for events to propagate...');
    await new Promise(resolve => setTimeout(resolve, 3000));
    
    // Now test the multi-projector query
    console.log('\n🔍 Testing MultiProjectorActor queries...');
    
    const actorId = 'aggregatelistprojector-taskprojector';
    
    const queryResponse = await daprClient.invoker.invoke(
      'dapr-sample-api-multi-projector',
      `actors/MultiProjectorActor/${actorId}/method/queryListAsync`,
      HttpMethod.PUT,
      [{
        queryType: 'TaskProjector',
        payload: {},
        skip: 0,
        take: 10
      }]
    );
    
    console.log('\n📊 Query results:', JSON.stringify(queryResponse, null, 2));
    
  } catch (error) {
    console.error('❌ Error:', error);
  }
}

// Run the script
createTestData().catch(console.error);