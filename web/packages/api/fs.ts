import { Channel, invoke, listen } from './ipc'
import type { Unlisten } from './ipc'

// File-system base identifiers exposed to the web layer. These are the exact strings the desktop
// IFileAccessPolicy resolves via TryGetBaseDirectory; any value listed here MUST have a matching
// resolver branch in src/core/Tarui.Ipc/FileAccessPolicy.cs and the legacy PascalCase aliases have
// been removed so the API does not drift from the host.
export const fsBaseIdentifiers = {
  AppData: 'appData',
  AppConfig: 'appConfig',
  AppCache: 'appCache',
  AppLocalData: 'appLocalData',
  AppLog: 'appLog',
  Desktop: 'desktop',
  Document: 'document',
  Download: 'download',
  Font: 'fonts',
  Home: 'home',
  Picture: 'pictures',
  Music: 'music',
  Video: 'video',
  Resource: 'resources',
  Temp: 'temp',
} as const

export type FsBaseId = (typeof fsBaseIdentifiers)[keyof typeof fsBaseIdentifiers]

export interface FsPathOptions {
  base: FsBaseId
  path?: string | undefined
}

export interface FsReadDirOptions extends FsPathOptions {
  recursive?: boolean
}

export interface FsWriteTextOptions extends FsPathOptions {
  contents?: string
}

export interface FsCopyOptions {
  fromBase: FsBaseId
  fromPath?: string
  toBase: FsBaseId
  toPath?: string
}

export interface FsRenameOptions {
  fromBase: FsBaseId
  fromPath?: string
  toBase: FsBaseId
  toPath?: string
  overwrite?: boolean
}

export interface FsMkdirOptions extends FsPathOptions {
  recursive?: boolean
}

export interface FsRemoveOptions extends FsPathOptions {
  recursive?: boolean
}

export interface FsDirEntry {
  name: string
  path: string
  isDirectory: boolean
  isFile: boolean
  size: number
  children?: FsDirEntry[] | undefined
}

export interface FsStatResult {
  isDirectory: boolean
  isFile: boolean
  size: number
  accessedAtMs?: number | undefined
  modifiedAtMs?: number | undefined
  createdAtMs?: number | undefined
}

export interface FsReadTextResult {
  contents: string
}

export type FsStreamEvent =
  | { kind: 'meta'; meta: { size: number; modifiedAtMs: number | null } }
  | { kind: 'chunk'; data: Uint8Array }

export interface FsReadStreamOptions extends FsPathOptions {
  chunkBytes?: number
  onEvent?: (event: FsStreamEvent) => void
}

export interface FsReadStreamResult {
  size: number
}

export interface FsWriteBeginResult {
  writeId: string
}

export const fsWatchEventKinds = {
  created: 'created',
  changed: 'changed',
  renamed: 'renamed',
  deleted: 'deleted',
  error: 'error',
} as const

export type FsWatchEventKind = (typeof fsWatchEventKinds)[keyof typeof fsWatchEventKinds]

/** Payload of the `fs://watch-change` event produced by `watch`. */
export interface FsWatchEvent {
  watchId: string
  eventKind: FsWatchEventKind
  /** Affected path(s) relative to the watched root; source then destination for `renamed`. */
  outputPaths: string[]
}

export interface FsWatchOptions extends FsPathOptions {
  recursive?: boolean
}

export interface FsWatchResult {
  watchId: string
}

/** Event carrying directory-change notifications to windows that declare receive authorization. */
export const FS_WATCH_EVENT = 'fs://watch-change'

export async function readTextFile(options: FsPathOptions): Promise<FsReadTextResult> {
  return invoke<FsReadTextResult>('plugin:fs|read-text-file', options)
}

export async function writeTextFile(options: FsWriteTextOptions): Promise<void> {
  await invoke('plugin:fs|write-text-file', options)
}

/**
 * Streams a (potentially large) file back as frames: a leading `meta` frame with the total size, then any
 * number of `chunk` frames. `onEvent` is invoked for each frame; the promise resolves when the stream ends.
 * Because the host streams at its own pace, the invocation timeout is disabled here.
 */
export async function readFileStream(options: FsReadStreamOptions): Promise<FsReadStreamResult> {
  const channel = new Channel<FsStreamEvent>()
  channel.onmessage = toFsEvent(options.onEvent)
  const { onEvent: _ignored, ...request } = options
  return invoke<FsReadStreamResult>('plugin:fs|read-file-stream', { ...request, channel }, { timeoutMs: 0 })
}

/**
 * Writes a large payload in chunks. The write is buffered to a temporary native file and only becomes visible
 * on the final commit; on any error the pending session is cancelled so no partial file is left behind.
 */
export async function writeFileChunked(
  base: FsBaseId,
  path: string,
  data: Uint8Array | Blob,
  chunkBytes = 256 * 1024,
): Promise<void> {
  const bytes = data instanceof Blob ? new Uint8Array(await data.arrayBuffer()) : data
  const begin = await invoke<FsWriteBeginResult>('plugin:fs|write-begin', {
    base,
    path,
    totalBytes: bytes.length,
  })
  const { writeId } = begin
  try {
    for (let offset = 0; offset < bytes.length; offset += chunkBytes) {
      const slice = bytes.subarray(offset, Math.min(offset + chunkBytes, bytes.length))
      await invoke('plugin:fs|write-chunk', {
        writeId,
        data: base64Encode(slice),
        sequence: offset / chunkBytes,
      })
    }
    await invoke('plugin:fs|write-commit', { writeId })
  } catch (error) {
    await invoke('plugin:fs|write-cancel', { writeId }).catch(() => undefined)
    throw error
  }
}

function toFsEvent(onEvent?: (event: FsStreamEvent) => void): ((frame: unknown) => void) | undefined {
  if (!onEvent) {
    return undefined
  }

  return frame => {
    const f = frame as { kind: 'meta' | 'chunk'; meta?: { size: number; modifiedAt?: number | null } | null; data?: string | null }
    if (f.kind === 'meta') {
      onEvent({ kind: 'meta', meta: { size: f.meta?.size ?? 0, modifiedAtMs: f.meta?.modifiedAt ?? null } })
    } else {
      onEvent({ kind: 'chunk', data: f.data ? base64ToBytes(f.data) : new Uint8Array(0) })
    }
  }
}

function base64Encode(bytes: Uint8Array): string {
  let binary = ''
  for (const value of bytes) {
    binary += String.fromCharCode(value)
  }
  return btoa(binary)
}

function base64ToBytes(b64: string): Uint8Array {
  const binary = atob(b64)
  const bytes = new Uint8Array(binary.length)
  for (let index = 0; index < binary.length; index++) {
    bytes[index] = binary.charCodeAt(index)
  }
  return bytes
}

export async function readDir(options: FsReadDirOptions): Promise<FsDirEntry[]> {
  return invoke<FsDirEntry[]>('plugin:fs|read-dir', options)
}

export async function stat(options: FsPathOptions): Promise<FsStatResult | null> {
  return invoke<FsStatResult | null>('plugin:fs|stat', options)
}

export async function exists(options: FsPathOptions): Promise<boolean> {
  return invoke<boolean>('plugin:fs|exists', options)
}

export async function mkdir(options: FsMkdirOptions): Promise<void> {
  await invoke('plugin:fs|mkdir', options)
}

export async function copyFile(options: FsCopyOptions): Promise<void> {
  await invoke('plugin:fs|copy-file', options)
}

export async function rename(options: FsRenameOptions): Promise<void> {
  await invoke('plugin:fs|rename', options)
}

export async function remove(options: FsRemoveOptions): Promise<void> {
  await invoke('plugin:fs|remove', options)
}

/**
 * Watches a directory and delivers `fs://watch-change` events to this window. The target must be covered by the
 * caller's read scopes. Requires `plugin:fs|watch`; consuming events requires the `fs://watch-change` receive
 * capability. Pass the returned `watchId` to `unwatch` to stop.
 */
export async function watch(options: FsWatchOptions): Promise<FsWatchResult> {
  return invoke<FsWatchResult>('plugin:fs|watch', options)
}

/** Stops the watch identified by `watchId`. Requires `plugin:fs|unwatch`. */
export async function unwatch(watchId: string): Promise<void> {
  await invoke('plugin:fs|unwatch', { watchId })
}

/** Subscribes to directory-change events for windows that declare `fs://watch-change` receive authorization. */
export function onWatchEvent(callback: (event: FsWatchEvent) => void): Unlisten {
  return listen<FsWatchEvent>(FS_WATCH_EVENT, received => callback(received.payload))
}

export const fs = {
  readTextFile,
  writeTextFile,
  readFileStream,
  writeFileChunked,
  readDir,
  stat,
  exists,
  mkdir,
  copyFile,
  rename,
  remove,
  watch,
  unwatch,
  onWatchEvent,
} as const
