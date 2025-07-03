import { z } from 'zod';
import type { ICommand } from '../../../infrastructure/simple-sekiban-executor';
import { v4 as uuidv4 } from 'uuid';

const CreateUserCommandSchema = z.object({
  name: z.string().min(1).max(100),
  email: z.string().email().max(255)
});

export class CreateUserCommand implements ICommand {
  public readonly type = 'CreateUser';
  public readonly aggregateId: string;
  public readonly name: string;
  public readonly email: string;
  
  constructor(name: string, email: string) {
    // Validate inputs
    const validation = CreateUserCommandSchema.safeParse({ name, email });
    if (!validation.success) {
      throw new Error(`Invalid CreateUser command: ${validation.error.message}`);
    }
    
    this.aggregateId = uuidv4();
    this.name = name.trim();
    this.email = email.toLowerCase().trim();
  }
  
  toJSON() {
    return {
      type: this.type,
      aggregateId: this.aggregateId,
      name: this.name,
      email: this.email
    };
  }
}