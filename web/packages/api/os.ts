import { invoke } from './ipc'

export type OsInfo = {
  platform: string
  arch: string
  version: string
  family: string
  locale: string
}

/** Reports the host operating system details. */
export async function info(): Promise<OsInfo> {
  return invoke<OsInfo>('core:os|info', {})
}
