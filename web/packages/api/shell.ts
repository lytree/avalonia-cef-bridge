import { Channel, invoke } from './ipc'

export type ShellOpenResult = {
  opened: boolean
  error?: string
}

/** Opens a URL or file path with the OS default handler. */
export async function open(target: string): Promise<ShellOpenResult> {
  return invoke<ShellOpenResult>('core:shell|open', { target })
}

export type ShellStreamEvent =
  | { kind: 'stdout'; data: Uint8Array }
  | { kind: 'stderr'; data: Uint8Array }
  | { kind: 'terminated'; code: number | null }

export interface ShellSpawnOptions {
  args?: string[] | undefined
  workingDir?: string | undefined
  captureStdout?: boolean | undefined
  captureStderr?: boolean | undefined
  /** When provided, stdout/stderr are streamed here frame-by-frame and a `terminated` frame closes the stream. */
  onEvent?: (event: ShellStreamEvent) => void
}

export interface ShellSpawnResult {
  id: string
}

/**
 * Spawns a subprocess. The program must be explicitly granted by the caller's capability program scopes
 * (deny-by-default); a bare permission never opens arbitrary execution. Passing `onEvent` wires the child's
 * redirected stdio to a channel so stdout/stderr arrive incrementally and a final `terminated` frame reports
 * the exit code.
 */
export async function spawn(program: string, options: ShellSpawnOptions = {}): Promise<ShellSpawnResult> {
  const channel = options.onEvent ? new Channel<unknown>() : undefined
  if (channel && options.onEvent) {
    channel.onmessage = frame => options.onEvent!(toShellEvent(frame))
  }
  return invoke<ShellSpawnResult>('plugin:shell|spawn', {
    program,
    args: options.args && options.args.length > 0 ? options.args : undefined,
    workingDir: options.workingDir,
    captureStdout: options.captureStdout,
    captureStderr: options.captureStderr,
    channel,
  })
}

/** Writes raw bytes to a spawned child's stdin. */
export async function writeStdin(id: string, data: Uint8Array): Promise<void> {
  await invoke('plugin:shell|stdin', { id, data: base64Encode(data) })
}

/** Terminates a spawned child; defaults to killing its process tree too. */
export async function kill(id: string, killTree = true): Promise<void> {
  await invoke('plugin:shell|kill', { id, killTree })
}

function toShellEvent(frame: unknown): ShellStreamEvent {
  const f = frame as { kind: 'stdout' | 'stderr' | 'terminated'; data?: string | null; code?: number | null }
  if (f.kind === 'terminated') {
    return { kind: 'terminated', code: f.code ?? null }
  }
  return { kind: f.kind, data: f.data ? base64ToBytes(f.data) : new Uint8Array(0) }
}

function base64Encode(bytes: Uint8Array): string {
  let binary = ''
  for (const value of bytes) {
    binary += String.fromCharCode(value)
  }
  return btoa(binary)
}

function base64ToBytes(b64: string): Uint8Array {
  const binary = atob(b64)
  const bytes = new Uint8Array(binary.length)
  for (let index = 0; index < binary.length; index++) {
    bytes[index] = binary.charCodeAt(index)
  }
  return bytes
}

export const shell = { open, spawn, writeStdin, kill } as const