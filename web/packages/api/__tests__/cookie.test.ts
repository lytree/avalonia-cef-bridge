import { afterEach, describe, expect, it } from 'vitest'
import { cookie, list, set, remove, flush } from '../cookie'
import { cleanupIpcMock, installIpcMock, lastInvoke, send } from './ipcMock'

afterEach(() => cleanupIpcMock())

describe('cookie.list', () => {
  it('invokes list with the URL and includeHttpOnly default', async () => {
    installIpcMock()
    const pending = list('https://example.com/')
    const call = lastInvoke()!
    expect(call.command).toBe('plugin:cookie|list')
    expect(call.payload.url).toBe('https://example.com/')
    expect(call.payload.includeHttpOnly).toBe(true)
    send({
      protocol: 1,
      id: call.id,
      success: true,
      payload: { supported: true, cookies: [{ name: 'sid', value: 'v1', httpOnly: true }], error: null },
    })
    const res = await pending
    expect(res.supported).toBe(true)
    expect(res.cookies[0]).toMatchObject({ name: 'sid', httpOnly: true })
  })

  it('surfaces an unsupported host honestly', async () => {
    installIpcMock()
    const pending = list('https://example.com/')
    const call = lastInvoke()!
    send({
      protocol: 1,
      id: call.id,
      success: true,
      payload: { supported: false, cookies: [], error: 'no store' },
    })
    const res = await pending
    expect(res.supported).toBe(false)
    expect(res.error).toBe('no store')
  })
})

describe('cookie.set', () => {
  it('invokes set with the url and cookie', async () => {
    installIpcMock()
    const pending = set('https://example.com/', { name: 'theme', value: 'dark', path: '/' })
    const call = lastInvoke()!
    expect(call.command).toBe('plugin:cookie|set')
    expect(call.payload.url).toBe('https://example.com/')
    expect(call.payload.cookie).toEqual({ name: 'theme', value: 'dark', path: '/' })
    send({ protocol: 1, id: call.id, success: true, payload: { succeeded: true, error: null } })
    await expect(pending).resolves.toMatchObject({ succeeded: true })
  })
})

describe('cookie.remove', () => {
  it('invokes remove with url and name', async () => {
    installIpcMock()
    const pending = remove('https://example.com/', 'sid')
    const call = lastInvoke()!
    expect(call.command).toBe('plugin:cookie|remove')
    expect(call.payload.url).toBe('https://example.com/')
    expect(call.payload.name).toBe('sid')
    send({ protocol: 1, id: call.id, success: true, payload: { succeeded: true } })
    await expect(pending).resolves.toMatchObject({ succeeded: true })
  })

  it('omits url/name for a global clear', async () => {
    installIpcMock()
    const pending = remove()
    const call = lastInvoke()!
    expect(call.command).toBe('plugin:cookie|remove')
    expect(call.payload.url).toBeUndefined()
    expect(call.payload.name).toBeUndefined()
    send({ protocol: 1, id: call.id, success: true, payload: { succeeded: true } })
    await pending
  })
})

describe('cookie.flush', () => {
  it('invokes flush', async () => {
    installIpcMock()
    const pending = flush()
    const call = lastInvoke()!
    expect(call.command).toBe('plugin:cookie|flush')
    send({ protocol: 1, id: call.id, success: true, payload: {} })
    await pending
  })
})

describe('cookie module', () => {
  it('exposes the four operations on the module namespace', () => {
    expect(cookie.list).toBe(list)
    expect(cookie.set).toBe(set)
    expect(cookie.remove).toBe(remove)
    expect(cookie.flush).toBe(flush)
  })
})