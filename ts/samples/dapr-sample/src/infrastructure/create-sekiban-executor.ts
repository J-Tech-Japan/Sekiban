import type { SekibanConfig } from './simple-sekiban-executor';
import { SimpleSekibanExecutor } from './simple-sekiban-executor';
import type { AppDependencies } from '../app';

export async function createSekibanExecutor(
  config: SekibanConfig, 
  dependencies: AppDependencies = {}
): Promise<SimpleSekibanExecutor> {
  const executor = new SimpleSekibanExecutor(config, dependencies);
  return executor;
}