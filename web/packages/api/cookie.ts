import { invoke } from './ipc'

/** One browser cookie. `expires` is milliseconds since the Unix epoch; null means a session cookie with no expiry. */
export interface Cookie {
  name: string
  value: string
  domain?: string | null
  path?: string | null
  secure?: boolean
  httpOnly?: boolean
  expires?: number | null
  sameSite?: 'unspecified' | 'lax' | 'strict' | 'none' | null
}

export interface CookieListOptions {
  /** Only cookies for this URL are returned. */
  url: string
  /** Include HttpOnly cookies. */
  includeHttpOnly?: boolean
}

export interface CookieListResult {
  /** False (with `error` set) when the host has no browser cookie store — an honest degrade, never a fake list. */
  supported: boolean
  cookies: Cookie[]
  error?: string | null
}

export interface CookieSetOptions {
  url: string
  cookie: Cookie
}

export interface CookieSetResult {
  succeeded: boolean
  error?: string | null
}

/** Deletes a named cookie for a URL, or all cookies for a URL when `name` is omitted, or every cookie when `url` is also omitted. */
export interface CookieDeleteOptions {
  url?: string | null
  name?: string | null
}

export interface CookieDeleteResult {
  succeeded: boolean
  error?: string | null
}

/**
 * Lists the cookies that apply to `url`. The browser's global cookie store backs this call; when no store is
 * available the result reports `supported: false` instead of an empty list.
 */
export async function list(url: string, options: Pick<CookieListOptions, 'includeHttpOnly'> = {}): Promise<CookieListResult> {
  return invoke<CookieListResult>('plugin:cookie|list', {
    url,
    includeHttpOnly: options.includeHttpOnly ?? true,
  })
}

/** Sets a cookie for `url`. */
export async function set(url: string, cookie: Cookie): Promise<CookieSetResult> {
  return invoke<CookieSetResult>('plugin:cookie|set', { url, cookie })
}

/** Removes cookies. With no `url`, every cookie is removed. */
export async function remove(url: string | null = null, name: string | null = null): Promise<CookieDeleteResult> {
  return invoke<CookieDeleteResult>('plugin:cookie|remove', {
    url: url ?? undefined,
    name: name ?? undefined,
  })
}

/** Flushes any buffered cookie writes to persistent storage. */
export async function flush(): Promise<void> {
  await invoke('plugin:cookie|flush')
}

export const cookie = { list, set, remove, flush } as const