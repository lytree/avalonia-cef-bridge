import { invoke, listen } from './ipc'
import type { Unlisten } from './ipc'

/**
 * The launch deep-link URL: the registered-custom-protocol URL that started the running primary
 * instance. `url` is `null`/undefined when the instance was not activated through a registered
 * scheme (e.g. launched normally, or the URL was invalid).
 */
export interface DeepLinkCurrentResult {
  url?: string | null
}

/**
 * Simulation hook to feed a URL through the exact deep-link validation path so the example app can
 * demonstrate the received / rejected / not-applicable states without a real OS protocol activation.
 */
export interface DeepLinkFeedOptions {
  url?: string | null
}

/** Builds the reserved event name emitted for the given custom-protocol scheme. */
function deepLinkEvent(scheme: string): string {
  return `deeplink://${scheme}`
}

/** Returns the URL that started the current instance, or `null` when none is active. */
export async function getCurrent(): Promise<DeepLinkCurrentResult> {
  return invoke<DeepLinkCurrentResult>('plugin:deep-link|get-current')
}

/** Feeds a URL through deep-link validation (demonstration/testing only). */
export async function feed(options: DeepLinkFeedOptions): Promise<void> {
  await invoke('plugin:deep-link|feed', options)
}

/**
 * Subscribes to deep-link activations for a registered scheme. The callback receives the URL string
 * carried by the reserved `deeplink://<scheme>` event (requires the matching receive capability).
 *
 * @param scheme the registered custom protocol, e.g. `"tarui"`
 * @param callback receives the activation URL
 */
export function onDeepLink(scheme: string, callback: (url: string) => void): Unlisten {
  return listen<string>(deepLinkEvent(scheme), received => callback(received.payload))
}

export const deepLink = {
  getCurrent,
  feed,
  onDeepLink,
} as const