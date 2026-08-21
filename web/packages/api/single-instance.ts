import { listen } from './ipc'
import type { Unlisten } from './ipc'

/**
 * Event name delivered to the primary instance's authorized windows when a
 * second instance of the application is launched. Subscribe with `onSecondInstance`
 * (requires the `app://second-instance` event capability).
 */
export const SECOND_INSTANCE_EVENT = 'app://second-instance'

/** Command line forwarded by a second instance before it exits. */
export interface SecondInstanceArgs {
  /** Raw command-line arguments passed to the second instance. */
  arguments: string[]
  /** Working directory of the second instance. */
  workingDirectory: string
  /** ISO-8601 activation time (UTC) of the second instance. */
  timestamp: string
}

/** Subscribes to second-instance activations delivered to this window. */
export function onSecondInstance(callback: (args: SecondInstanceArgs) => void): Unlisten {
  return listen<SecondInstanceArgs>(SECOND_INSTANCE_EVENT, received =>
    callback(received.payload),
  )
}

export const singleInstance = {
  onSecondInstance,
} as const