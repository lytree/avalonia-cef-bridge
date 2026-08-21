import { invoke } from './ipc'

export const fsBaseIdentifiers = {
  AppData: 'appData',
  AppConfig: 'appConfig',
  AppCache: 'appCache',
  AppLocalData: 'appLocalData',
  AppLog: 'appLog',
  Audio: 'audio',
  Cache: 'cache',
  Config: 'config',
  Data: 'data',
  Desktop: 'desktop',
  Document: 'document',
  Download: 'download',
  Executable: 'executable',
  Font: 'font',
  Home: 'home',
  LocalData: 'localData',
  Log: 'log',
  Music: 'music',
  Picture: 'picture',
  Public: 'public',
  Resource: 'resource',
  Runtime: 'runtime',
  Temp: 'temp',
  Template: 'template',
  Video: 'video',
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

export async function readTextFile(options: FsPathOptions): Promise<FsReadTextResult> {
  return invoke<FsReadTextResult>('plugin:fs|read-text-file', options)
}

export async function writeTextFile(options: FsWriteTextOptions): Promise<void> {
  await invoke('plugin:fs|write-text-file', options)
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

export const fs = {
  readTextFile,
  writeTextFile,
  readDir,
  stat,
  exists,
  mkdir,
  copyFile,
  rename,
  remove,
} as const
