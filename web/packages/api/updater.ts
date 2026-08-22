import { invoke, listen } from './ipc'
import type { Unlisten } from './ipc'

/** Outcome of `plugin:updater|check`. */
export interface UpdateCheckResult {
  updateAvailable: boolean
  /** The target version when an update is available. */
  version?: string | null
  /** A Web-facing reason when verification/fetch failed; not present on success or no-update. */
  error?: string | null
}

/** Outcome of `plugin:updater|download`. */
export interface UpdateDownloadResult {
  succeeded: boolean
  /** A Web-facing reason when staging failed; not present on success. */
  error?: string | null
}

/** Machine-readable progress/completion phases delivered on the `updater://status` event. */
export const updaterStatusPhases = {
  CheckSuccess: 'check-success',
  DownloadStart: 'download-start',
  DownloadProgress: 'download-progress',
  DownloadSuccess: 'download-success',
  VerificationFailed: 'verification-failed',
  CheckFailed: 'check-failed',
  DownloadFailed: 'download-failed',
} as const

export type UpdaterStatusPhase = (typeof updaterStatusPhases)[keyof typeof updaterStatusPhases]

/** Payload of the reserved `updater://status` event. */
export interface UpdaterStatus {
  phase: UpdaterStatusPhase
  version?: string | null
  file?: string | null
  error?: string | null
}

/** Event carrying updater progress/completion to windows that declare receive authorization. */
export const UPDATER_STATUS_EVENT = 'updater://status'

/**
 * Fetches and verifies the signed update manifest, reporting whether a strictly newer version is
 * available. Verification failures surface as `error`; they are never reported as "no update".
 * Requires `plugin:updater|check`.
 */
export async function check(): Promise<UpdateCheckResult> {
  return invoke<UpdateCheckResult>('plugin:updater|check')
}

/**
 * Fetches and verifies the update manifest, then stages every file to the app data staging
 * directory after revalidating each blob's SHA-256. The running installation is never touched.
 * Requires `plugin:updater|download`.
 */
export async function download(): Promise<UpdateDownloadResult> {
  return invoke<UpdateDownloadResult>('plugin:updater|download')
}

/**
 * Subscribes to updater progress/completion events (requires `updater://status` receive capability).
 */
export function onStatus(callback: (status: UpdaterStatus) => void): Unlisten {
  return listen<UpdaterStatus>(UPDATER_STATUS_EVENT, received => callback(received.payload))
}

export const updater = {
  check,
  download,
  onStatus,
  updaterStatusPhases,
  UPDATER_STATUS_EVENT,
} as const