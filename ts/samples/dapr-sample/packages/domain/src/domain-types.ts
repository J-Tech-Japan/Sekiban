import { createSchemaDomainTypes, globalRegistry } from '@sekiban/core';
import { 
  CreateTask, 
  AssignTask, 
  CompleteTask, 
  UpdateTask, 
  DeleteTask 
} from './aggregates/task/commands/task-commands.js';
import {
  TaskCreated,
  TaskAssigned,
  TaskCompleted,
  TaskUpdated,
  TaskDeleted
} from './aggregates/task/events/task-events.js';
import { taskProjectorDefinition } from './aggregates/task/projectors/task-projector.js';
// import { GetTaskById } from './aggregates/task/queries/task-queries.js';

// Register all domain types with the global registry
// Events
globalRegistry.registerEvent(TaskCreated);
globalRegistry.registerEvent(TaskAssigned);
globalRegistry.registerEvent(TaskCompleted);
globalRegistry.registerEvent(TaskUpdated);
globalRegistry.registerEvent(TaskDeleted);

// Commands
globalRegistry.registerCommand(CreateTask);
globalRegistry.registerCommand(AssignTask);
globalRegistry.registerCommand(CompleteTask);
globalRegistry.registerCommand(UpdateTask);
globalRegistry.registerCommand(DeleteTask);

// Projectors
globalRegistry.registerProjector(taskProjectorDefinition);

// TODO: Register queries when supported
// globalRegistry.registerQuery(GetTaskById);

export function createTaskDomainTypes() {
  return createSchemaDomainTypes(globalRegistry);
}