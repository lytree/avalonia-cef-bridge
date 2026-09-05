import { afterEach, describe, expect, it } from 'vitest'
import { Window } from '../window'
import { cleanupIpcMock, installIpcMock, lastInvoke, send } from './ipcMock'

afterEach(() => cleanupIpcMock())

describe('window.setIcon', () => {
  it('base64-encodes png and targets the current window', async () => {
    installIpcMock()
    const png = new Uint8Array([80, 78, 71]) // "PNG"
    const pending = Window.getCurrent().setIcon(png)
    const call = lastInvoke()!
    expect(call.command).toBe('core:window|set-icon')
    expect(call.payload.png).toBe(btoa('PNG'))
    expect(call.payload.label).toBeUndefined()
    send({ protocol: 1, id: call.id, success: true, payload: {} })
    await pending
  })

  it('omits png to clear the icon and carries the label', async () => {
    installIpcMock()
    const pending = Window.getByLabel('editor').setIcon(null)
    const call = lastInvoke()!
    expect(call.command).toBe('core:window|set-icon')
    expect(call.payload.png).toBeUndefined()
    expect(call.payload.label).toBe('editor')
    send({ protocol: 1, id: call.id, success: true, payload: {} })
    await pending
  })
})

describe('window.setTheme', () => {
  it('invokes set-theme with dark and the current window', async () => {
    installIpcMock()
    const pending = Window.getCurrent().setTheme('dark')
    const call = lastInvoke()!
    expect(call.command).toBe('core:window|set-theme')
    expect(call.payload.theme).toBe('dark')
    send({ protocol: 1, id: call.id, success: true, payload: {} })
    await pending
  })
})