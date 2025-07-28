import { z } from 'zod';
import { ok, err, Result } from 'neverthrow';
import { CreateTask, AssignTask } from '@dapr-sample/domain';
import type { SekibanError } from '@sekiban/core';

export const TaskAssignmentWorkflowContext = z.object({
  title: z.string(),
  description: z.string().optional(),
  assignedTo: z.string().email(),
  priority: z.enum(['low', 'medium', 'high']).default('medium'),
  dueDate: z.string().datetime().optional()
});

export type TaskAssignmentWorkflowContext = z.infer<typeof TaskAssignmentWorkflowContext>;

export interface TaskAssignmentWorkflowResult {
  taskId: string;
  assignedTo: string;
  createdAt: string;
}

/**
 * Workflow that creates a task and immediately assigns it to a user
 */
export class TaskAssignmentWorkflow {
  async execute(
    context: TaskAssignmentWorkflowContext,
    executor: { executeCommand: (cmd: any) => Promise<Result<any, SekibanError>> }
  ): Promise<Result<TaskAssignmentWorkflowResult, SekibanError>> {
    // Step 1: Create the task
    const createCommand = CreateTask.create({
      title: context.title,
      description: context.description,
      priority: context.priority,
      dueDate: context.dueDate,
      assignedTo: context.assignedTo
    });

    const createResult = await executor.executeCommand(createCommand);
    if (createResult.isErr()) {
      return err(createResult.error);
    }

    const taskId = createResult.value.aggregateId;

    // Step 2: Assign the task (even though it's already assigned during creation,
    // this demonstrates a multi-step workflow)
    const assignCommand = AssignTask.create({
      taskId,
      assignedTo: context.assignedTo
    });

    const assignResult = await executor.executeCommand(assignCommand);
    if (assignResult.isErr()) {
      // In a real workflow, we might want to compensate here
      return err(assignResult.error);
    }

    return ok({
      taskId,
      assignedTo: context.assignedTo,
      createdAt: new Date().toISOString()
    });
  }
}