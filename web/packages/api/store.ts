import { invoke } from './ipc'

/**
 * Store base directory identifiers. Each maps to a well-known host directory that
 * `IFileAccessPolicy` authorizes before any disk access; `resources` is read-only.
 */
export const storeBaseIdentifiers = {
  AppData: 'appData',
  AppLocalData: 'appLocalData',
  AppConfig: 'appConfig',
  AppCache: 'appCache',
  AppLog: 'appLog',
  Temp: 'temp',
  Resource: 'resources',
} as const

export type StoreBaseId = (typeof storeBaseIdentifiers)[keyof typeof storeBaseIdentifiers]

/** Selects a JSON store file: a named base directory plus a relative store path. */
export interface StoreFileOptions {
  base?: StoreBaseId
  path?: string | undefined
}

/** A single key lookup or mutation within a JSON store file. */
export interface StoreKeyOptions extends StoreFileOptions {
  key: string
}

/** Writes `value` at `key`; a `null` value removes the key (Tauri erase semantics). */
export interface StoreSetOptions extends StoreKeyOptions {
  value?: string | null | undefined
}

export interface StoreGetResult {
  value?: string | null | undefined
}

export interface StoreHasResult {
  has: boolean
}

export interface StoreKeysResult {
  keys: string[]
}

/** Reads the value stored at `key` (null when missing). */
export async function get(options: StoreKeyOptions): Promise<StoreGetResult> {
  return invoke<StoreGetResult>('plugin:store|get', options)
}

/**
 * Writes `value` at `key`. Passing `null` (or omitting the value with no key) removes the key.
 * The store file defaults to `appData/settings.json`.
 */
export async function set(options: StoreSetOptions): Promise<void> {
  await invoke('plugin:store|set', options)
}

/** Reports whether `key` exists in the selected store file. */
export async function has(options: StoreKeyOptions): Promise<StoreHasResult> {
  return invoke<StoreHasResult>('plugin:store|has', options)
}

/** Removes `key` from the selected store file. */
export async function remove(options: StoreKeyOptions): Promise<void> {
  await invoke('plugin:store|delete', options)
}

/** Removes every key from the selected store file. */
export async function clear(options?: StoreFileOptions): Promise<void> {
  await invoke('plugin:store|clear', options ?? {})
}

/** Lists all keys currently present in the selected store file. */
export async function keys(options?: StoreFileOptions): Promise<StoreKeysResult> {
  return invoke<StoreKeysResult>('plugin:store|keys', options ?? {})
}

export const store = {
  get,
  set,
  has,
  remove,
  clear,
  keys,
  storeBaseIdentifiers,
} as const