import { invoke } from './ipc'

/** Exits the shell process with the given exit code. */
export async function exit(code = 0): Promise<void> {
  await invoke('core:process|exit', { code })
}

/** Restarts the shell process with a fresh executable. */
export async function relaunch(): Promise<void> {
  await invoke('core:process|relaunch', {})
}
