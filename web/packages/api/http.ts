import { Channel, invoke } from './ipc'

export type HttpStreamEvent =
  | {
      kind: 'meta'
      status: number
      statusText: string | null
      headers: [name: string, value: string][]
    }
  | { kind: 'chunk'; data: Uint8Array }

export interface HttpHeaders {
  [name: string]: string
}

export interface HttpFetchOptions {
  method?: string
  headers?: HttpHeaders
  body?: string
  timeoutMs?: number
  /** Aborts the front-end side of the fetch. */
  signal?: AbortSignal
  /**
   * When provided, the response is streamed: a leading `meta` frame carries the status and headers, then any
   * number of `chunk` frames carry the body bytes. The resolved `body` is `null` in this mode.
   */
  onEvent?: (event: HttpStreamEvent) => void
}

export interface HttpResponse {
  status: number
  headers: HttpHeaders
  ok: boolean
  body: string | null
}

/**
 * Issues a capability-scoped HTTP request through the native shell (bypassing CORS / CSP network limits).
 * Every request and each redirect hop must be covered by the caller's URL scopes; permission is default-deny.
 * Passing `onEvent` switches to a streaming response so arbitrarily large bodies can be consumed frame by frame.
 */
export async function fetch(url: string, options: HttpFetchOptions = {}): Promise<HttpResponse> {
  const headers = Object.entries(options.headers ?? {}).map(([name, value]) => ({ name, value }))

  const streaming = options.onEvent !== undefined
  const channel = streaming ? new Channel<unknown>() : undefined
  if (channel && options.onEvent) {
    channel.onmessage = frame => options.onEvent!(toHttpEvent(frame))
  }

  const payload: Record<string, unknown> = {
    method: options.method ?? 'GET',
    url,
    headers: headers.length > 0 ? headers : undefined,
    body: options.body,
    timeoutMs: options.timeoutMs,
    channel,
  }

  const result = await invoke<{
    status: number
    headers: { name: string; value: string }[]
    body: string | null
  }>('plugin:http|fetch', payload, { timeoutMs: 0, signal: options.signal })

  const headerMap: HttpHeaders = {}
  for (const header of result.headers) {
    headerMap[header.name] = header.value
  }

  return {
    status: result.status,
    headers: headerMap,
    ok: result.status >= 200 && result.status < 300,
    body: result.body,
  }
}

function toHttpEvent(frame: unknown): HttpStreamEvent {
  const f = frame as {
    kind: 'meta' | 'chunk'
    meta?: { status?: number; statusText?: string | null; headers?: { name: string; value: string }[] } | null
    data?: string | null
  }
  if (f.kind === 'meta') {
    return {
      kind: 'meta',
      status: f.meta?.status ?? 0,
      statusText: f.meta?.statusText ?? null,
      headers: (f.meta?.headers ?? []).map(header => [header.name, header.value]),
    }
  }

  const b64 = f.data ?? ''
  const binary = atob(b64)
  const bytes = new Uint8Array(binary.length)
  for (let index = 0; index < binary.length; index++) {
    bytes[index] = binary.charCodeAt(index)
  }
  return { kind: 'chunk', data: bytes }
}

export const http = { fetch } as const