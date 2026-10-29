import { invoke, listen } from './ipc'
import { relaunch as relaunchApp } from './process'
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
  /** The verified staging directory to pass to `apply`, present on success. */
  stagingPath?: string | null
}

/** Outcome of `plugin:updater|apply`. */
export interface UpdateApplyResult {
  succeeded: boolean
  /** A Web-facing reason when apply failed or was unsupported; not present on success. */
  error?: string | null
  /** Whether a restart was requested (and should now be performed on success). */
  restart: boolean
}

/** Outcome of `plugin:updater|restore`. */
export interface UpdateRestoreResult {
  succeeded: boolean
  /** The restored (already verified) target version, present on success. */
  version?: string | null
  /** A Web-facing reason when parsing, signature or version policy failed. */
  error?: string | null
}

/** Outcome of `plugin:updater|verify`. */
export interface UpdateVerifyResult {
  succeeded: boolean
  /** A Web-facing reason naming the offending file when a staged blob is missing or mismatched. */
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
  ApplyStart: 'apply-start',
  ApplySuccess: 'apply-success',
  ApplyFailed: 'apply-failed',
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
 * Applies a previously staged, verified bundle (pass the `stagingPath` from `download`) using the platform
 * install strategy. On success with `restart` set, relaunches the app so the new version takes effect. Requires
 * `plugin:updater|apply`.
 */
export async function apply(stagingPath: string, restart = true): Promise<UpdateApplyResult> {
  const result = await invoke<UpdateApplyResult>('plugin:updater|apply', { stagingPath, restart })
  // Relaunch the host through the existing process command so the danger zone (replacing the running
  // installation) never races the host teardown inside the updater service.
  if (result.succeeded && result.restart) {
    await relaunchApp()
  }
  return result
}

/**
 * Subscribes to updater progress/completion events (requires `updater://status` receive capability).
 */
export function onStatus(callback: (status: UpdaterStatus) => void): Unlisten {
  return listen<UpdaterStatus>(UPDATER_STATUS_EVENT, received => callback(received.payload))
}

/**
 * Restores a previously fetched signed manifest (persisted by the caller after `check`) after
 * re-verifying its signature and the configured version policy. Mirrors Tauri's
 * `Updater.restoreUpdate`: a cached manifest is only trusted when its signature still verifies.
 * Requires `plugin:updater|restore`.
 */
export async function restore(manifestJson: string): Promise<UpdateRestoreResult> {
  return invoke<UpdateRestoreResult>('plugin:updater|restore', { manifestJson })
}

/**
 * Verifies every staged blob against the signed manifest's SHA-256 table without touching the
 * network or the running installation. Mirrors Tauri's `Update.verify`. Requires
 * `plugin:updater|verify`.
 */
export async function verify(manifestJson: string, stagingPath: string): Promise<UpdateVerifyResult> {
  return invoke<UpdateVerifyResult>('plugin:updater|verify', { manifestJson, stagingPath })
}

export const updater = {
  check,
  download,
  apply,
  restore,
  verify,
  // Aliases kept for parity with the root `@lytree/api` namespace re-exports.
  checkUpdate: check,
  downloadUpdate: download,
  applyUpdate: apply,
  restoreUpdate: restore,
  verifyUpdate: verify,
  onStatus,
  updaterStatusPhases,
  UPDATER_STATUS_EVENT,
} as const

// Named aliases for direct `import { checkUpdate } from '@lytree/api/updater'` style usage.
export const checkUpdate = check
export const downloadUpdate = download
export const applyUpdate = apply
export const restoreUpdate = restore
export const verifyUpdate = verify