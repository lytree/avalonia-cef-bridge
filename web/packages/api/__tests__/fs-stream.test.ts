import { afterEach, describe, expect, it, vi } from 'vitest'
import { readFileStream, writeFileChunked } from '../fs'
import { cleanupIpcMock, installIpcMock, lastInvoke, send } from './ipcMock'

afterEach(() => cleanupIpcMock())

describe('readFileStream', () => {
  it('sends a channel token, disables the timeout, and decodes frames', async () => {
    installIpcMock()
    const kinds: string[] = []
    const onEvent = vi.fn((event: { kind: string }) => kinds.push(event.kind))
    const pending = readFileStream({ base: 'temp', path: 'a.bin', chunkBytes: 1024, onEvent })

    // The first (and only) invoke carries the channel token as a string and no timeout.
    const call = lastInvoke()
    expect(call?.command).toBe('plugin:fs|read-file-stream')
    expect(call?.payload.base).toBe('temp')
    expect(call?.payload.chunkBytes).toBe(1024)
    const channelId = call?.payload.channel as string
    expect(channelId).toMatch(/^chan-\d+$/)

    send({ type: 'channel', channel: channelId, payload: { kind: 'meta', meta: { size: 8, modifiedAt: null } } })
    send({ type: 'channel', channel: channelId, payload: { kind: 'chunk', data: btoa('AB') } })
    send({ type: 'channel', channel: channelId, payload: { kind: 'chunk', data: btoa('CDEF') } })
    send({ protocol: 1, id: call!.id, success: true, payload: { size: 8 } })

    const result = await pending
    expect(result.size).toBe(8)
    expect(kinds).toEqual(['meta', 'chunk', 'chunk'])
    expect(onEvent).toHaveBeenCalledTimes(3)
  })
})

describe('writeFileChunked', () => {
  it('sequences begin, incremental chunks, and a final commit', async () => {
    installIpcMock()
    // Auto-respond so the await chain proceeds; record every command in sequence.
    const calls: { id: string; command: string; payload: Record<string, unknown> }[] = []
    window.invokeCSharpAction = message => {
      const parsed = JSON.parse(message) as { id: string; command: string; payload: Record<string, unknown> }
      calls.push(parsed)
      const payload = parsed.command === 'plugin:fs|write-begin' ? { writeId: 'w-1' } : {}
      send({ protocol: 1, id: parsed.id, success: true, payload })
    }

    const data = new Uint8Array([1, 2, 3, 4, 5])
    await writeFileChunked('temp', 'out.bin', data, 2)

    expect(calls.map(c => c.command)).toEqual(['plugin:fs|write-begin', 'plugin:fs|write-chunk', 'plugin:fs|write-chunk', 'plugin:fs|write-chunk', 'plugin:fs|write-commit'])
    expect(calls[0].payload).toMatchObject({ base: 'temp', path: 'out.bin', totalBytes: 5 })

    const chunks = calls.filter(c => c.command === 'plugin:fs|write-chunk')
    expect(chunks.map(c => c.payload.sequence)).toEqual([0, 1, 2])
    const decoded = chunks.map(c => Array.from(atob(c.payload.data as string), ch => ch.charCodeAt(0)))
    expect(decoded).toEqual([[1, 2], [3, 4], [5]])
    expect(calls.at(-1)!.command).toBe('plugin:fs|write-commit')
  })

  it('cancels the pending session when a chunk write fails', async () => {
    installIpcMock()
    const calls: { id: string; command: string; payload: Record<string, unknown> }[] = []
    window.invokeCSharpAction = message => {
      const parsed = JSON.parse(message) as { id: string; command: string; payload: Record<string, unknown> }
      calls.push(parsed)
      if (parsed.command === 'plugin:fs|write-begin') {
        send({ protocol: 1, id: parsed.id, success: true, payload: { writeId: 'w-9' } })
      } else if (parsed.command === 'plugin:fs|write-chunk' && parsed.payload.sequence === 1) {
        send({ protocol: 1, id: parsed.id, success: false, error: { code: 'PATH_DENIED', message: 'denied' } })
      } else {
        send({ protocol: 1, id: parsed.id, success: true, payload: {} })
      }
    }

    const data = new Uint8Array([1, 2, 3])
    await expect(writeFileChunked('temp', 'out.bin', data, 2)).rejects.toMatchObject({
      code: 'PATH_DENIED',
    })

    expect(calls.at(-1)!.command).toBe('plugin:fs|write-cancel')
  })
})