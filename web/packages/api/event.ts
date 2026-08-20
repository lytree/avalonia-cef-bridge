import { invoke, listen } from './ipc'
import type { Unlisten } from './ipc'

/**
 * Emits an event through the shell's event router. Without a target
 * window the event is broadcast to every live webview, including the
 * sender; with one it is routed to that window only.
 */
export async function emit(
  event: string,
  payload?: unknown,
  targetWindow?: string,
): Promise<void> {
  await invoke('core:event|emit', {
    event,
    payload: payload ?? {},
    targetWindow,
  })
}

/** Subscribes to a routed event with the payload unwrapped. */
export function on<TPayload = unknown>(
  event: string,
  callback: (payload: TPayload) => void,
): Unlisten {
  return listen<TPayload>(event, received => callback(received.payload))
}
