import { createSchemaDomainTypes, globalRegistry } from '@sekiban/core';
import { 
  CreateTask, 
  AssignTask, 
  CompleteTask, 
  UpdateTask, 
  DeleteTask,
  RevertTaskCompletion
} from './aggregates/task/commands/task-commands.js';
import {
  TaskCreated,
  TaskAssigned,
  TaskCompleted,
  TaskUpdated,
  TaskDeleted,
  TaskCompletionReverted
} from './aggregates/task/events/task-events.js';
import { taskProjectorDefinition } from './aggregates/task/projectors/task-projector.js';
import { TaskMultiProjector } from './aggregates/task/projectors/task-multi-projector.js';
import { GetTaskById, GetAllTasks } from './aggregates/task/queries/index.js';
import { UserCreated, UserNameChanged, UserEmailChanged } from './aggregates/user/events/index.js';
import { userProjectorDefinition } from './aggregates/user/projectors/user-projector.js';

// Register all domain types with the global registry
// Events
globalRegistry.registerEvent(TaskCreated);
globalRegistry.registerEvent(TaskAssigned);
globalRegistry.registerEvent(TaskCompleted);
globalRegistry.registerEvent(TaskUpdated);
globalRegistry.registerEvent(TaskDeleted);
globalRegistry.registerEvent(TaskCompletionReverted);

// Commands
globalRegistry.registerCommand(CreateTask);
globalRegistry.registerCommand(AssignTask);
globalRegistry.registerCommand(CompleteTask);
globalRegistry.registerCommand(UpdateTask);
globalRegistry.registerCommand(DeleteTask);
globalRegistry.registerCommand(RevertTaskCompletion);

// User Events
globalRegistry.registerEvent(UserCreated);
globalRegistry.registerEvent(UserNameChanged);
globalRegistry.registerEvent(UserEmailChanged);

// Projectors
globalRegistry.registerProjector(taskProjectorDefinition);
globalRegistry.registerProjector(userProjectorDefinition);

// Multi-projectors
globalRegistry.registerMultiProjector(TaskMultiProjector);

// Queries
globalRegistry.registerQuery(GetTaskById);
globalRegistry.registerQuery(GetAllTasks);

export function createTaskDomainTypes() {
  const domainTypes = createSchemaDomainTypes(globalRegistry);
  
  // Add convenience methods using the public API
  return {
    ...domainTypes,
    // Add helper methods using public API
    findCommandDefinition: (name: string) => globalRegistry.getCommand(name),
    findEventDefinition: (name: string) => globalRegistry.getEventDefinition(name),
    findProjectorDefinition: (name: string) => globalRegistry.getProjector(name)
  };
}