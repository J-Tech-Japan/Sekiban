import { createSchemaDomainTypes } from '@sekiban/core';
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
import { TaskProjector } from './aggregates/task/projectors/task-projector.js';
import { GetTaskById } from './aggregates/task/queries/task-queries.js';

export function createTaskDomainTypes() {
  return createSchemaDomainTypes({
    commands: [
      CreateTask,
      AssignTask,
      CompleteTask,
      UpdateTask,
      DeleteTask
    ],
    events: [
      TaskCreated,
      TaskAssigned,
      TaskCompleted,
      TaskUpdated,
      TaskDeleted
    ],
    projectors: [
      TaskProjector
    ],
    queries: [
      GetTaskById
    ]
  });
}