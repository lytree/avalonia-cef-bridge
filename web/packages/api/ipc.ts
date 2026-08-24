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

type Resolver = {
  resolve: (value: unknown) => void
  reject: (reason: Error) => void
  timeoutHandle?: ReturnType<typeof setTimeout>
  abortHandler?: () => void
}

declare global {
  interface Window {
    invokeCSharpAction?: (message: string) => void
    __TARUI_INTERNALS__?: TaruiHost
    __tarui_dispatchBase64?: (message: string) => void
  }
}

/** Default timeout for invocations that do not specify one. */
export const DEFAULT_INVOKE_TIMEOUT_MS = 30_000

/** Native command lifecycle options. */
export type InvokeOptions = {
  /** Maximum time to wait for a response before rejecting with INVOKE_TIMEOUT. */
  timeoutMs?: number
  /** Abort the request as soon as the signal aborts. */
  signal?: AbortSignal
}

/** Error thrown when a native command resolves with a failure payload. */
export class IpcCommandError extends Error {
  readonly code: string

  constructor(code: string, message: string) {
    super(message)
    this.name = 'IpcCommandError'
    this.code = code
  }
}

/** Streaming channel mirrored from the native TaruiChannel contract. */
export class Channel<TPayload> {
  onmessage: ((payload: TPayload) => void) | undefined

  push(payload: TPayload): void {
    this.onmessage?.(payload)
  }
}

const pending = new Map<string, Resolver>()
const eventListeners = new Map<string, Set<(payload: unknown) => void>>()
let sequence = 0
let resetInstalled = false

/**
 * Invokes a native command registered on the shell's command router.
 * Commands outside the calling window's capability set are rejected.
 *
 * The promise resolves when the native bridge answers, rejects on
 * IpcCommandError, the supplied signal aborts, or the optional
 * timeoutMs elapses. Pending bookkeeping is reclaimed on every exit
 * path so the map cannot leak across page unloads or bridge resets.
 */
export async function invoke<TResult = unknown>(
  command: string,
  payload: unknown = {},
  options: InvokeOptions = {},
): Promise<TResult> {
  installResetOnce()

  const bridge = window.invokeCSharpAction ?? window.__TARUI_INTERNALS__?.invokeCSharpAction
  if (!bridge) {
    throw new IpcCommandError('BRIDGE_UNAVAILABLE', `Native bridge unavailable for ${command}`)
  }

  if (options.signal?.aborted) {
    throw new IpcCommandError('INVOKE_ABORTED', `Invocation aborted before dispatch: ${command}`)
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

  const timeoutMs = options.timeoutMs ?? DEFAULT_INVOKE_TIMEOUT_MS
  const result = new Promise<unknown>((resolve, reject) => {
    const resolver: Resolver = { resolve, reject }
    pending.set(id, resolver)

    if (timeoutMs > 0) {
      resolver.timeoutHandle = setTimeout(() => {
        if (!pending.has(id)) {
          return
        }
        pending.delete(id)
        reject(new IpcCommandError('INVOKE_TIMEOUT', `Invocation exceeded ${timeoutMs}ms: ${command}`))
      }, timeoutMs)
    }

    if (options.signal) {
      resolver.abortHandler = () => {
        if (!pending.has(id)) {
          return
        }
        pending.delete(id)
        reject(new IpcCommandError('INVOKE_ABORTED', `Invocation aborted: ${command}`))
      }
      options.signal.addEventListener('abort', resolver.abortHandler, { once: true })
    }
  })

  try {
    dispatchThroughBridge(bridge, command, request)
    return (await result) as TResult
  } catch (error) {
    if (error instanceof IpcCommandError) {
      throw error
    }
    throw new IpcCommandError(
      'BRIDGE_UNAVAILABLE',
      `Bridge threw while dispatching ${command}: ${(error as Error).message}`,
    )
  } finally {
    const resolver = pending.get(id)
    if (resolver) {
      pending.delete(id)
      clearResolver(resolver)
    }
  }
}

/**
 * Calls the native bridge for the supplied request, converting a synchronous
 * exception into an {@link IpcCommandError} so callers see a consistent
 * rejection shape across sync and async failures.
 */
function dispatchThroughBridge(
  bridge: (message: string) => void,
  command: string,
  request: InvokeEnvelope,
): void {
  try {
    bridge(JSON.stringify(request))
  } catch (error) {
    throw new IpcCommandError(
      'BRIDGE_UNAVAILABLE',
      `Bridge threw while dispatching ${command}: ${(error as Error).message}`,
    )
  }
}

function clearResolver(resolver: Resolver): void {
  if (resolver.timeoutHandle) {
    clearTimeout(resolver.timeoutHandle)
    resolver.timeoutHandle = undefined
  }
}

/** Subscribes to events routed from the shell to this webview. */
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
  clearResolver(request)
  if (message.success) {
    request.resolve(message.payload)
  } else {
    request.reject(
      new IpcCommandError(
        message.error?.code ?? 'COMMAND_FAILED',
        message.error?.message ?? 'Native command failed',
      ),
    )
  }
}

/**
 * Rejects every outstanding invoke and clears event listeners. Exposed
 * for tests, hot-reload harnesses, and explicit teardown. The page
 * unload hook installs an automatic call so a navigating webview never
 * leaks pending requests back to a stale DOM context.
 */
export function reset(): void {
  for (const [id, resolver] of pending) {
    clearResolver(resolver)
    resolver.reject(new IpcCommandError('BRIDGE_RESET', `Pending invocation rejected: ${id}`))
  }
  pending.clear()
  eventListeners.clear()
}

/**
 * Installs the beforeunload listener exactly once. Subsequent
 * invoke calls and explicit reset() invocations reuse the existing
 * hook. The listener rejects any in-flight requests so a navigating
 * webview never resolves them against a stale DOM context.
 */
function installResetOnce(): void {
  if (resetInstalled) {
    return
  }
  if (typeof window === 'undefined') {
    return
  }

  resetInstalled = true
  window.addEventListener('beforeunload', () => {
    reset()
  })
}

if (typeof window !== 'undefined') {
  window.__tarui_dispatchBase64 = message => {
    dispatch(JSON.parse(decodeBase64(message)) as InvokeResponse<unknown> | EventEnvelope<unknown>)
  }
}
