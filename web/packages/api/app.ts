import { invoke } from './ipc'

export type AppInfo = {
  product: string
  shellVersion: string
  bridgeVersion: number
  platform: string
  capabilities: string[]
}

/** Handshake payload describing the shell build and granted capabilities. */
export async function getAppInfo(): Promise<AppInfo> {
  return invoke<AppInfo>('core:app|get-info', {})
}
