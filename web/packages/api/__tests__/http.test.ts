import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetch, http } from '../http'
import type { HttpStreamEvent } from '../http'
import { cleanupIpcMock, installIpcMock, lastInvoke, send } from './ipcMock'

afterEach(() => cleanupIpcMock())

describe('http.fetch', () => {
  it('returns status, ok, headers, and inline body', async () => {
    installIpcMock()
    const pending = fetch('https://api.example.com/greet', { headers: { 'X-Api': 'key' } })

    const call = lastInvoke()
    expect(call?.command).toBe('plugin:http|fetch')
    expect(call?.payload.url).toBe('https://api.example.com/greet')
    expect(call?.payload.headers).toEqual([{ name: 'X-Api', value: 'key' }])
    expect(call?.payload.channel).toBeUndefined()

    send({
      protocol: 1,
      id: call!.id,
      success: true,
      payload: { status: 200, headers: [{ name: 'Content-Type', value: 'text/plain' }], body: 'hello' },
    })

    const res = await pending
    expect(res.status).toBe(200)
    expect(res.ok).toBe(true)
    expect(res.body).toBe('hello')
    expect(res.headers['Content-Type']).toBe('text/plain')
  })

  it('flags non-2xx as not ok and still surfaces the body', async () => {
    installIpcMock()
    const pending = fetch('https://api.example.com/missing')
    const call = lastInvoke()!
    send({ protocol: 1, id: call.id, success: true, payload: { status: 404, headers: [], body: 'nope' } })
    const res = await pending
    expect(res.status).toBe(404)
    expect(res.ok).toBe(false)
    expect(res.body).toBe('nope')
  })

  it('streams meta and chunk frames, decoding base64 chunk data', async () => {
    installIpcMock()
    const frames: HttpStreamEvent[] = []
    const onEvent = vi.fn((event: HttpStreamEvent) => frames.push(event))
    const pending = fetch('https://api.example.com/big', { onEvent })

    const call = lastInvoke()!
    const channelId = call.payload.channel as string
    expect(channelId).toMatch(/^chan-\d+$/)

    send({ type: 'channel', channel: channelId, payload: { kind: 'meta', meta: { status: 200, statusText: 'OK', headers: [{ name: 'x-a', value: '1' }] } } })
    send({ type: 'channel', channel: channelId, payload: { kind: 'chunk', data: btoa('hi') } })
    send({ type: 'channel', channel: channelId, payload: { kind: 'chunk', data: btoa('!') } })
    send({ protocol: 1, id: call.id, success: true, payload: { status: 200, headers: [{ name: 'x-a', value: '1' }], body: null } })

    const res = await pending
    expect(res.body).toBeNull()
    expect(onEvent).toHaveBeenCalledTimes(3)
    expect(frames[0]).toMatchObject({ kind: 'meta', status: 200 })
    const chunks = frames.slice(1) as { kind: 'chunk'; data: Uint8Array }[]
    expect(new TextDecoder().decode(chunks[0].data)).toBe('hi')
    expect(new TextDecoder().decode(chunks[1].data)).toBe('!')
  })
})

describe('http', () => {
  it('exposes a fetch binding on the module namespace', () => {
    expect(http.fetch).toBe(fetch)
  })
})