import { vi } from 'vitest';

export interface CloudEvent {
  id: string;
  type: string;
  source: string;
  specversion: string;
  time: string;
  data: any;
  datacontenttype?: string;
}

export interface PublishedMessage {
  topic: string;
  eventType: string;
  cloudEvent: CloudEvent;
}

export interface MockDaprClient {
  pubsub: {
    publish: ReturnType<typeof vi.fn>;
  };
  _simulateFailure?: boolean;
}

export function createMockDaprClient() {
  const publishedMessages: PublishedMessage[] = [];
  const publishErrors: Error[] = [];
  let shouldFailPublish = false;
  let publishFailureError: Error | null = null;

  const mockPublish = vi.fn().mockImplementation(async (topic: string, eventType: string, data: any) => {
    if (shouldFailPublish && publishFailureError) {
      publishErrors.push(publishFailureError);
      throw publishFailureError;
    }

    const cloudEvent: CloudEvent = {
      id: `${Date.now()}-${Math.random()}`,
      type: eventType,
      source: '/users',
      specversion: '1.0',
      time: new Date().toISOString(),
      data,
      datacontenttype: 'application/json'
    };

    publishedMessages.push({
      topic,
      eventType,
      cloudEvent
    });

    return { success: true };
  });

  const client: MockDaprClient = {
    pubsub: {
      publish: mockPublish
    },
    _simulateFailure: false
  };

  return {
    client,
    getPublishedMessages: () => [...publishedMessages],
    getPublishErrors: () => [...publishErrors],
    reset: () => {
      publishedMessages.length = 0;
      publishErrors.length = 0;
      shouldFailPublish = false;
      publishFailureError = null;
      client._simulateFailure = false;
      mockPublish.mockClear();
    },
    simulatePublishFailure: (error: Error) => {
      shouldFailPublish = true;
      publishFailureError = error;
      client._simulateFailure = true;
    }
  };
}