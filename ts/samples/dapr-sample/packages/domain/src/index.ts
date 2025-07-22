// Import domain types to ensure registration happens
import './domain-types.js';

// Export domain types
export { createTaskDomainTypes } from './domain-types.js';

// Export commands
export { 
  CreateTask, 
  AssignTask, 
  CompleteTask, 
  UpdateTask, 
  DeleteTask,
  RevertTaskCompletion
} from './aggregates/task/commands/task-commands.js';

// Export events
export {
  TaskCreated,
  TaskAssigned,
  TaskCompleted,
  TaskUpdated,
  TaskDeleted,
  TaskCompletionReverted
} from './aggregates/task/events/task-events.js';

// Export projector and states
export { TaskProjector } from './aggregates/task/projectors/task-projector.js';
export type { TaskState, CompletedTaskState, TaskPayloadUnion } from './aggregates/task/projectors/task-projector.js';

// Export queries
export { GetTaskById } from './aggregates/task/queries/task-queries.js';

// Export User aggregate
export * from './aggregates/user/index.js';