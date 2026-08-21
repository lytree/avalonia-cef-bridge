import { invoke, listen } from './ipc'
import type { Unlisten } from './ipc'

/**
 * Log levels mirrored from `Microsoft.Extensions.Logging.LogLevel`. Unknown renderer levels
 * degrade to `Information` on the desktop side.
 */
export const logLevels = {
  Trace: 'Trace',
  Debug: 'Debug',
  Information: 'Information',
  Warning: 'Warning',
  Error: 'Error',
  Critical: 'Critical',
  None: 'None',
} as const

export type LogLevel = (typeof logLevels)[keyof typeof logLevels]

/** A renderer-originated log record forwarded into the host logging pipeline. */
export interface LogRecordOptions {
  level: LogLevel
  message: string
  /** Optional logger category; defaults to the caller window. */
  target?: string | undefined
  /** Optional client timestamp (epoch milliseconds) surfaced for correlation. */
  timestampMs?: number | undefined
}

/** A log entry streamed to authorized windows on the `log://entry` event. */
export interface LogEntry {
  level: LogLevel
  message: string
  target?: string | undefined
  timestampMs?: number | undefined
}

/** Event carrying desktop log entries to windows that declare receive authorization. */
export const LOG_ENTRY_EVENT = 'log://entry'

/**
 * Forwards a renderer log record into the desktop logging pipeline where it joins the host
 * sink chain (file, console, subsystem providers). Requires `plugin:log|record`.
 */
export async function record(options: LogRecordOptions): Promise<void> {
  await invoke('plugin:log|record', options)
}

/** Convenience helpers mirroring `Microsoft.Extensions.Logging.LogLevel`. */
export async function trace(message: string, target?: string): Promise<void> {
  await record({ level: 'Trace', message, target })
}

export async function debug(message: string, target?: string): Promise<void> {
  await record({ level: 'Debug', message, target })
}

export async function info(message: string, target?: string): Promise<void> {
  await record({ level: 'Information', message, target })
}

export async function warn(message: string, target?: string): Promise<void> {
  await record({ level: 'Warning', message, target })
}

export async function error(message: string, target?: string): Promise<void> {
  await record({ level: 'Error', message, target })
}

export async function critical(message: string, target?: string): Promise<void> {
  await record({ level: 'Critical', message, target })
}

/** Subscribes to desktop log entries (requires `log://entry` receive capability). */
export function onLogEntry(callback: (entry: LogEntry) => void): Unlisten {
  return listen<LogEntry>(LOG_ENTRY_EVENT, received => callback(received.payload))
}

export const log = {
  record,
  trace,
  debug,
  info,
  warn,
  error,
  critical,
  onLogEntry,
  logLevels,
} as const