import { afterEach, describe, expect, it } from 'vitest'
import { Webview } from '../webview'
import { cleanupIpcMock, installIpcMock, lastInvoke, send } from './ipcMock'

afterEach(() => cleanupIpcMock())

describe('webview devtools', () => {
  it('opens devtools on the current webview with open:true', async () => {
    installIpcMock()
    const pending = Webview.getCurrent().openDevtools()
    const call = lastInvoke()!
    expect(call.command).toBe('plugin:webview|devtools')
    expect(call.payload.open).toBe(true)
    expect(call.payload.label).toBeUndefined()
    send({ protocol: 1, id: call.id, success: true, payload: {} })
    await pending
  })

  it('closes devtools on a labeled webview with open:false', async () => {
    installIpcMock()
    const pending = Webview.getByLabel('editor').closeDevtools()
    const call = lastInvoke()!
    expect(call.command).toBe('plugin:webview|devtools')
    expect(call.payload.open).toBe(false)
    expect(call.payload.label).toBe('editor')
    send({ protocol: 1, id: call.id, success: true, payload: {} })
    await pending
  })
})