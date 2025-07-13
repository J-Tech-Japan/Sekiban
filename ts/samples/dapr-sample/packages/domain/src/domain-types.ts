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
// import { GetTaskById } from './aggregates/task/queries/task-queries.js';
import { UserCreated, UserNameChanged, UserEmailChanged } from './aggregates/user/events/index.js';
import { UserProjector } from './aggregates/user/projectors/user-projector.js';

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
globalRegistry.registerProjector(UserProjector);

// TODO: Register queries when supported
// globalRegistry.registerQuery(GetTaskById);

export function createTaskDomainTypes() {
  const domainTypes = createSchemaDomainTypes(globalRegistry);
  
  // Add convenience properties for backward compatibility
  return {
    ...domainTypes,
    // Expose the registry's collections directly
    commands: globalRegistry.commandDefinitions,
    events: globalRegistry.eventDefinitions,
    projectors: globalRegistry.projectorDefinitions,
    // Add helper methods
    findCommandDefinition: (name: string) => globalRegistry.commandDefinitions.get(name),
    findEventDefinition: (name: string) => globalRegistry.eventDefinitions.get(name),
    findProjectorDefinition: (name: string) => globalRegistry.projectorDefinitions.get(name)
  };
}