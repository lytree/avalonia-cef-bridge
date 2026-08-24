import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { DEFAULT_INVOKE_TIMEOUT_MS, IpcCommandError, invoke, listen, reset } from '../ipc'

type PendingEnvelope =
  | { protocol: number; id: string; success: boolean; payload?: unknown; error?: { code: string; message: string } }
  | { type: 'event'; event: string; payload: unknown }

let capturedDispatch: ((envelope: PendingEnvelope) => void) | null = null

function captureAndReplace(): void {
  const previous = window.__tarui_dispatchBase64
  capturedDispatch = envelope => {
    if (!previous) {
      return
    }
    previous(btoa(JSON.stringify(envelope)))
  }
}

function sendResponse(response: PendingEnvelope): void {
  if (!capturedDispatch) {
    throw new Error('ipc dispatch handler missing; ensure happy-dom is configured')
  }
  capturedDispatch(response)
}

function rawBridge(): ((message: string) => void) | undefined {
  return window.invokeCSharpAction
}

beforeEach(() => {
  ;(window as unknown as { invokeCSharpAction?: (m: string) => void }).invokeCSharpAction = undefined
  window.__TARUI_INTERNALS__ = undefined
  captureAndReplace()
  reset()
})

afterEach(() => {
  reset()
})

describe('invoke', () => {
  it('surfaces a bridge unavailable error when no bridge is attached', async () => {
    await expect(invoke('plugin:demo|ping', { a: 1 })).rejects.toMatchObject({
      code: 'BRIDGE_UNAVAILABLE',
    })
  })

  it('normalizes synchronous bridge throws to BRIDGE_UNAVAILABLE', async () => {
    window.invokeCSharpAction = () => {
      throw new Error('native-crashed')
    }
    await expect(invoke('plugin:demo|ping')).rejects.toBeInstanceOf(IpcCommandError)
    await expect(invoke('plugin:demo|ping')).rejects.toMatchObject({ code: 'BRIDGE_UNAVAILABLE' })
  })

  it('forwards the success payload back to the caller', async () => {
    let receivedId = ''
    window.invokeCSharpAction = message => {
      const parsed = JSON.parse(message) as { id: string }
      receivedId = parsed.id
      sendResponse({ protocol: 1, id: parsed.id, success: true, payload: { value: 42 } })
    }

    const result = await invoke<{ value: number }>('plugin:demo|ping', { a: 1 })
    expect(result.value).toBe(42)
    expect(receivedId.startsWith('web-')).toBe(true)
  })

  it('rejects with the native error code on command failure', async () => {
    window.invokeCSharpAction = message => {
      const parsed = JSON.parse(message) as { id: string }
      sendResponse({
        protocol: 1,
        id: parsed.id,
        success: false,
        error: { code: 'COMMAND_FAILED', message: 'kaboom' },
      })
    }
    await expect(invoke('plugin:demo|ping')).rejects.toMatchObject({
      code: 'COMMAND_FAILED',
      message: 'kaboom',
    })
  })

  it('rejects with INVOKE_TIMEOUT when the bridge never answers', async () => {
    window.invokeCSharpAction = () => {
      // Intentionally do nothing so the invoke times out.
    }
    await expect(invoke('plugin:demo|never', {}, { timeoutMs: 25 })).rejects.toMatchObject({
      code: 'INVOKE_TIMEOUT',
    })
  })

  it('defaults to a 30 second timeout', () => {
    expect(DEFAULT_INVOKE_TIMEOUT_MS).toBe(30_000)
  })

  it('rejects with INVOKE_ABORTED when the caller signal is already aborted', async () => {
    window.invokeCSharpAction = vi.fn()
    const controller = new AbortController()
    controller.abort()
    await expect(
      invoke('plugin:demo|ping', {}, { signal: controller.signal }),
    ).rejects.toMatchObject({ code: 'INVOKE_ABORTED' })
    expect(rawBridge()).not.toHaveBeenCalled()
  })

  it('rejects with INVOKE_ABORTED when the caller aborts during the wait', async () => {
    window.invokeCSharpAction = () => {
      // No response; we abort below.
    }
    const controller = new AbortController()
    const pending = invoke('plugin:demo|slow', {}, { signal: controller.signal, timeoutMs: 5_000 })
    setTimeout(() => controller.abort(), 5)
    await expect(pending).rejects.toMatchObject({ code: 'INVOKE_ABORTED' })
  })
})

describe('reset', () => {
  it('rejects every outstanding invocation with BRIDGE_RESET', async () => {
    window.invokeCSharpAction = () => {
      // Never answer.
    }
    const a = invoke('plugin:demo|a', {}, { timeoutMs: 5_000 })
    const b = invoke('plugin:demo|b', {}, { timeoutMs: 5_000 })
    reset()
    await expect(a).rejects.toMatchObject({ code: 'BRIDGE_RESET' })
    await expect(b).rejects.toMatchObject({ code: 'BRIDGE_RESET' })
  })

  it('clears listeners so unregistered names no longer fire', () => {
    const offA = listen('user://a', vi.fn())
    const offB = listen('user://b', vi.fn())
    offA()
    offB()
    reset()
    const newCall = vi.fn()
    listen('user://a', newCall)
    sendResponse({ type: 'event', event: 'user://a', payload: 1 })
    expect(newCall).toHaveBeenCalledWith({ payload: 1 })
  })
})
