import { invoke } from './ipc'

export type ShellOpenResult = {
  opened: boolean
  error?: string
}

/** Opens a URL or file path with the OS default handler. */
export async function open(target: string): Promise<ShellOpenResult> {
  return invoke<ShellOpenResult>('core:shell|open', { target })
}
