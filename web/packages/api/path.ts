import { invoke } from './ipc'

export type PathKind =
  | 'appData'
  | 'appLocalData'
  | 'appConfig'
  | 'appCache'
  | 'appLog'
  | 'temp'
  | 'home'
  | 'download'
  | 'document'
  | 'desktop'
  | 'pictures'
  | 'music'
  | 'video'
  | 'fonts'
  | 'resources'

/**
 * Resolves a well-known base directory, optionally joined with a
 * relative path. The shell rejects paths that escape the base.
 */
export async function resolve(kind: PathKind, path?: string): Promise<string> {
  const result = await invoke<{ path: string }>('core:path|resolve', { kind, path })
  return result.path
}
