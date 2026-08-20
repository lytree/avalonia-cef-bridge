import { invoke } from './ipc'

/** Reads plain text from the system clipboard. */
export async function readText(): Promise<string> {
  const result = await invoke<{ text: string }>('core:clipboard|read-text', {})
  return result.text
}

/** Writes plain text to the system clipboard. */
export async function writeText(text: string): Promise<void> {
  await invoke('core:clipboard|write-text', { text })
}
