import { IEvent } from '@sekiban/core'

/**
 * Interface for upcasting events from one version to another
 */
export interface Upcaster<TIn, TOut> {
  eventType: string
  fromVersion: number
  toVersion: number
  upcast(payload: TIn): TOut
}

/**
 * Registry for managing event upcasters
 */
export class UpcasterRegistry {
  private upcasters = new Map<string, Upcaster<any, any>>()

  /**
   * Register an upcaster
   */
  register<TIn, TOut>(upcaster: Upcaster<TIn, TOut>): void {
    const key = this.getKey(upcaster.eventType, upcaster.fromVersion)
    this.upcasters.set(key, upcaster)
  }

  /**
   * Get an upcaster for a specific event type and version
   */
  getUpcaster(eventType: string, fromVersion: number): Upcaster<any, any> | undefined {
    const key = this.getKey(eventType, fromVersion)
    return this.upcasters.get(key)
  }

  /**
   * Get a chain of upcasters to transform from one version to another
   */
  getUpcastChain(eventType: string, fromVersion: number, toVersion: number): Upcaster<any, any>[] {
    const chain: Upcaster<any, any>[] = []
    let currentVersion = fromVersion

    while (currentVersion < toVersion) {
      const upcaster = this.getUpcaster(eventType, currentVersion)
      if (!upcaster) {
        throw new Error(
          `No upcaster found for ${eventType} from version ${currentVersion}`
        )
      }
      chain.push(upcaster)
      currentVersion = upcaster.toVersion
    }

    return chain
  }

  private getKey(eventType: string, fromVersion: number): string {
    return `${eventType}:${fromVersion}`
  }
}

/**
 * Upcast an event to a target version
 */
export function upcastEvent(
  event: IEvent,
  registry: UpcasterRegistry,
  targetVersion: number
): IEvent {
  const currentVersion = event.meta?.schemaVersion || 1

  if (currentVersion >= targetVersion) {
    return event
  }

  try {
    const chain = registry.getUpcastChain(event.eventType, currentVersion, targetVersion)
    
    let payload = event.payload
    for (const upcaster of chain) {
      payload = upcaster.upcast(payload)
    }

    return {
      ...event,
      payload,
      meta: {
        ...event.meta,
        schemaVersion: targetVersion
      }
    }
  } catch (error) {
    // If no upcaster chain exists, return the event as-is
    return event
  }
}

/**
 * Create a composite upcaster from a chain of upcasters
 */
export function createUpcasterChain<TIn, TOut>(
  chain: Upcaster<any, any>[]
): (payload: TIn) => TOut {
  return (payload: TIn) => {
    let result: any = payload
    for (const upcaster of chain) {
      result = upcaster.upcast(result)
    }
    return result as TOut
  }
}