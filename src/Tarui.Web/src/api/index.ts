export type Unlisten = () => void

type InvokeEnvelope = {
  protocol: 1
  id: string
  command: string
  payload: unknown
  windowLabel: string
  webViewLabel: string
}

type InvokeResponse<T> = {
  protocol: number
  id: string
  success: boolean
  payload?: T
  error?: { code: string; message: string }
}

type EventEnvelope<T> = {
  type: 'event'
  event: string
  payload: T
}

type TaruiHost = {
  invokeCSharpAction?: (message: string) => void
}

declare global {
  interface Window {
    invokeCSharpAction?: (message: string) => void
    __TARUI_INTERNALS__?: TaruiHost
    __tarui_dispatchBase64?: (message: string) => void
  }
}

type Pending = {
  resolve: (value: unknown) => void
  reject: (reason: Error) => void
}

const pending = new Map<string, Pending>()
const eventListeners = new Map<string, Set<(payload: unknown) => void>>()
let sequence = 0

export class Channel<TPayload> {
  onmessage: ((payload: TPayload) => void) | undefined

  push(payload: TPayload): void {
    this.onmessage?.(payload)
  }
}

export async function invoke<TResult = unknown>(
  command: string,
  payload: unknown = {},
): Promise<TResult> {
  const bridge = window.invokeCSharpAction ?? window.__TARUI_INTERNALS__?.invokeCSharpAction
  if (!bridge) {
    throw new Error(`Native bridge unavailable for ${command}`)
  }

  sequence += 1
  const id = `web-${Date.now()}-${sequence}`
  const request: InvokeEnvelope = {
    protocol: 1,
    id,
    command,
    payload,
    windowLabel: 'main',
    webViewLabel: 'main',
  }

  const result = new Promise<unknown>((resolve, reject) => {
    pending.set(id, { resolve, reject })
  })
  bridge(JSON.stringify(request))
  return (await result) as TResult
}

export function listen<TPayload = unknown>(
  event: string,
  callback: (event: { payload: TPayload }) => void,
): Unlisten {
  const listeners = eventListeners.get(event) ?? new Set<(payload: unknown) => void>()
  const listener = (payload: unknown) => callback({ payload: payload as TPayload })
  listeners.add(listener)
  eventListeners.set(event, listeners)

  return () => {
    listeners.delete(listener)
    if (listeners.size === 0) {
      eventListeners.delete(event)
    }
  }
}

function decodeBase64(value: string): string {
  const bytes = Uint8Array.from(atob(value), character => character.charCodeAt(0))
  return new TextDecoder().decode(bytes)
}

function dispatch(message: InvokeResponse<unknown> | EventEnvelope<unknown>): void {
  if ('type' in message) {
    if (message.type === 'event') {
      for (const listener of eventListeners.get(message.event) ?? []) {
        listener(message.payload)
      }
    }
    return
  }

  const request = pending.get(message.id)
  if (!request) {
    return
  }

  pending.delete(message.id)
  if (message.success) {
    request.resolve(message.payload)
  } else {
    request.reject(new Error(message.error?.message ?? 'Native command failed'))
  }
}

window.__tarui_dispatchBase64 = message => {
  dispatch(JSON.parse(decodeBase64(message)) as InvokeResponse<unknown> | EventEnvelope<unknown>)
}
