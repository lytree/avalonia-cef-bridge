import { reset } from '../ipc'

export type MockEnvelope =
  | { protocol: number; id: string; success: boolean; payload?: unknown; error?: { code: string; message: string } }
  | { type: 'event'; event: string; payload: unknown }
  | { type: 'channel'; channel: string; payload: unknown }

export interface RecordedInvoke {
  id: string
  command: string
  payload: Record<string, unknown>
}

let dispatch: ((envelope: MockEnvelope) => void) | null = null

/**
 * Installs the native bridge hooks on the test window so `invoke`/`listen`/`Channel` operate against a mock.
 * Recorded invocations are returned by {@link lastInvoke}; pushed dispatch envelopes are routed via {@link send}.
 */
export function installIpcMock(): void {
  const previous = window.__tarui_dispatchBase64
  dispatch = envelope => {
    if (previous) {
      previous(btoa(JSON.stringify(envelope)))
    }
  }
  window.invokeCSharpAction = message => {
    invokeLog.push(JSON.parse(message) as RecordedInvoke)
  }
  reset()
}

export function cleanupIpcMock(): void {
  reset()
  dispatch = null
  invokeLog.splice(0)
}

export function send(envelope: MockEnvelope): void {
  if (dispatch) {
    dispatch(envelope)
  }
}

export const invokeLog: RecordedInvoke[] = []

export function lastInvoke(): RecordedInvoke | undefined {
  return invokeLog[invokeLog.length - 1]
}