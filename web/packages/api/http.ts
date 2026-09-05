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

export interface HttpField {
  name: string
  value: string
}

export interface HttpFilePart {
  /** Form field name for the file part. */
  name: string
  /** Client filename sent as the part's filename. */
  fileName: string
  /** Raw file bytes. */
  data: Uint8Array
  /** Optional MIME type; defaults to octet-stream. */
  contentType?: string
}

export interface HttpUploadOptions {
  headers?: HttpHeaders
  fields?: HttpField[]
  files?: HttpFilePart[]
  timeoutMs?: number
  signal?: AbortSignal
}

export interface HttpUploadResult {
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

/**
 * Sends a capability-scoped multipart/form-data POST. File bytes and text fields are carried as parts and the
 * URL (plus every redirect hop) must be covered by the caller's URL scopes; permission is default-deny. Returns
 * the inline response body, capped at the native inline limit like `fetch`.
 */
export async function upload(url: string, options: HttpUploadOptions = {}): Promise<HttpUploadResult> {
  const headers = Object.entries(options.headers ?? {}).map(([name, value]) => ({ name, value }))
  const files = (options.files ?? []).map(file => ({
    name: file.name,
    fileName: file.fileName,
    data: base64Encode(file.data),
    contentType: file.contentType,
  }))

  const result = await invoke<{
    status: number
    headers: { name: string; value: string }[]
    body: string | null
  }>('plugin:http|upload', {
    url,
    headers: headers.length > 0 ? headers : undefined,
    fields: options.fields && options.fields.length > 0 ? options.fields : undefined,
    files: files.length > 0 ? files : undefined,
    timeoutMs: options.timeoutMs,
  }, { signal: options.signal })

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

function base64Encode(bytes: Uint8Array): string {
  let binary = ''
  for (const value of bytes) {
    binary += String.fromCharCode(value)
  }
  return btoa(binary)
}

export const http = { fetch, upload } as const