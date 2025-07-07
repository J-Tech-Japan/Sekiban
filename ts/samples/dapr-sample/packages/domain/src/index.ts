// Export domain types
export { createTaskDomainTypes } from './domain-types.js';

// Export commands
export { 
  CreateTask, 
  AssignTask, 
  CompleteTask, 
  UpdateTask, 
  DeleteTask 
} from './aggregates/task/commands/task-commands.js';

// Export events
export {
  TaskCreated,
  TaskAssigned,
  TaskCompleted,
  TaskUpdated,
  TaskDeleted
} from './aggregates/task/events/task-events.js';

// Export projector
export { TaskProjector } from './aggregates/task/projectors/task-projector.js';
export type { TaskState } from './aggregates/task/projectors/task-projector.js';

// Export queries
export { GetTaskById } from './aggregates/task/queries/task-queries.js';