import { invoke, listen } from './ipc'
import type { Unlisten } from './ipc'

/** OS notification permission states. */
export const notificationPermissionState = {
  Granted: 'granted',
  Denied: 'denied',
  Prompt: 'prompt',
} as const

export type NotificationPermission = (typeof notificationPermissionState)[keyof typeof notificationPermissionState]

/** Options for `plugin:notification|show`. */
export interface NotificationOptions {
  /** App-defined id used to cancel the notification and correlate events. */
  id: string
  title: string
  body: string
  /** Optional icon path resolved against a well-known `base:` identifier. */
  icon?: string | undefined
  /** Requests an audible alert when the platform honours it. */
  sound?: boolean
}

/** Result of a permission query/request. */
export interface NotificationPermissionStateResult {
  permission: NotificationPermission
  /** False on platforms with no notification facility. */
  supported: boolean
  reason?: string | undefined
}

/** Payload of `notification://activated` / `notification://dismissed`. */
export interface NotificationEvent {
  id: string
  title?: string | undefined
  body?: string | undefined
  action?: string | undefined
}

/** Event delivered when a notification is activated by the user. */
export const NOTIFICATION_ACTIVATED_EVENT = 'notification://activated'

/** Event delivered when a notification is dismissed. */
export const NOTIFICATION_DISMISSED_EVENT = 'notification://dismissed'

/** Queries the current notification permission state. */
export async function permissionState(): Promise<NotificationPermissionStateResult> {
  return invoke('plugin:notification|permission-state')
}

/** Requests notification permission from the OS/user. */
export async function requestPermission(): Promise<NotificationPermissionStateResult> {
  return invoke('plugin:notification|request-permission')
}

/** Shows a system notification. */
export async function show(options: NotificationOptions): Promise<void> {
  await invoke('plugin:notification|show', options)
}

/** Cancels a showing notification by its id. */
export async function cancel(id: string): Promise<void> {
  await invoke('plugin:notification|cancel', { id })
}

/** Subscribes to notification activation events (requires `notification://activated` capability). */
export function onNotificationActivated(callback: (notification: NotificationEvent) => void): Unlisten {
  return listen<NotificationEvent>(NOTIFICATION_ACTIVATED_EVENT, received => callback(received.payload))
}

/** Subscribes to notification dismissal events (requires `notification://dismissed` capability). */
export function onNotificationDismissed(callback: (notification: NotificationEvent) => void): Unlisten {
  return listen<NotificationEvent>(NOTIFICATION_DISMISSED_EVENT, received => callback(received.payload))
}

export const notification = {
  permissionState,
  requestPermission,
  show,
  cancel,
  onNotificationActivated,
  onNotificationDismissed,
} as const