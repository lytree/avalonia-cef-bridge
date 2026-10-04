import { invoke, listen } from './ipc'
import type { Unlisten } from './ipc'

/** A single path scope entry matching the native `PathScope` contract. */
export interface PersistedPathScope {
  /** Well-known directory base (for example `appData`, `temp`); omit for URL-style scopes. */
  base?: string | undefined
  /** Glob-relative path below the base (for example `documents/**`). */
  path?: string | undefined
}

/** Payload for `scopeAllow` / `scopeDeny`. */
export interface PersistedScopeOptions {
  permission: string
  paths: PersistedPathScope[]
}

/** Payload of `scope://changed`. `action` is `allow`, `deny`, or `reset`. */
export interface PersistedScopeChangedEvent {
  permission: string
  action: 'allow' | 'deny' | 'reset'
}

/** Event delivered when the persisted scope overlay changes. */
export const PERSISTED_SCOPE_CHANGED_EVENT = 'scope://changed'

/**
 * Adds runtime allow entries for a structured permission. Only fs and http permissions accept
 * runtime entries; a matching deny entry is lifted so the allow takes effect.
 */
export async function scopeAllow(permission: string, paths: PersistedPathScope[]): Promise<void> {
  await invoke('plugin:persisted-scope|scope-allow', { permission, paths })
}

/**
 * Adds runtime deny entries for a structured permission (deny always wins). A matching allow entry
 * is removed so the deny takes effect.
 */
export async function scopeDeny(permission: string, paths: PersistedPathScope[]): Promise<void> {
  await invoke('plugin:persisted-scope|scope-deny', { permission, paths })
}

/** Clears the overlay for one permission, or every permission when omitted. */
export async function scopeReset(permission?: string): Promise<void> {
  await invoke('plugin:persisted-scope|scope-reset', { permission })
}

/** Subscribes to persisted scope changes (requires the `scope://changed` capability). */
export function onScopeChanged(callback: (event: PersistedScopeChangedEvent) => void): Unlisten {
  return listen<PersistedScopeChangedEvent>(PERSISTED_SCOPE_CHANGED_EVENT, received => callback(received.payload))
}

export const persistedScope = {
  allow: scopeAllow,
  deny: scopeDeny,
  reset: scopeReset,
  onScopeChanged,
} as const
