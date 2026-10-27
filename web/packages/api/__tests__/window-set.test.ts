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

describe('window effects', () => {
  it('invokes set-always-on-bottom with the target label', async () => {
    installIpcMock()
    const pending = Window.getByLabel('editor').setAlwaysOnBottom(true)
    const call = lastInvoke()!
    expect(call.command).toBe('core:window|set-always-on-bottom')
    expect(call.payload.value).toBe(true)
    expect(call.payload.label).toBe('editor')
    send({ protocol: 1, id: call.id, success: true, payload: {} })
    await pending
  })

  it('invokes set-shadow for the current window', async () => {
    installIpcMock()
    const pending = Window.getCurrent().setShadow(false)
    const call = lastInvoke()!
    expect(call.command).toBe('core:window|set-shadow')
    expect(call.payload.value).toBe(false)
    expect(call.payload.label).toBeUndefined()
    send({ protocol: 1, id: call.id, success: true, payload: {} })
    await pending
  })

  it('invokes set-badge-count and forwards null to clear the badge', async () => {
    installIpcMock()
    const pending = Window.getCurrent().setBadgeCount(7)
    const setCall = lastInvoke()!
    expect(setCall.command).toBe('core:window|set-badge-count')
    expect(setCall.payload.count).toBe(7)
    send({ protocol: 1, id: setCall.id, success: true, payload: {} })
    await pending

    const clearing = Window.getCurrent().setBadgeCount(null)
    const clearCall = lastInvoke()!
    expect(clearCall.payload.count).toBeNull()
    send({ protocol: 1, id: clearCall.id, success: true, payload: {} })
    await clearing
  })

  it('invokes set-progress-bar with status and percentage', async () => {
    installIpcMock()
    const pending = Window.getCurrent().setProgressBar({ status: 'paused', progress: 42.5 })
    const call = lastInvoke()!
    expect(call.command).toBe('core:window|set-progress-bar')
    expect(call.payload.status).toBe('paused')
    expect(call.payload.progress).toBe(42.5)
    send({ protocol: 1, id: call.id, success: true, payload: {} })
    await pending
  })

  it('maps a null progress bar to the none status', async () => {
    installIpcMock()
    const pending = Window.getCurrent().setProgressBar(null)
    const call = lastInvoke()!
    expect(call.payload.status).toBe('none')
    expect(call.payload.progress).toBeUndefined()
    send({ protocol: 1, id: call.id, success: true, payload: {} })
    await pending
  })
})

describe('window.setFullscreenOnMonitor', () => {
  it('sends the physical point together with the target label', async () => {
    installIpcMock()
    const pending = Window.getByLabel('editor').setFullscreenOnMonitor(2500, 300)
    const call = lastInvoke()!
    expect(call.command).toBe('core:window|set-fullscreen-on-monitor')
    expect(call.payload.x).toBe(2500)
    expect(call.payload.y).toBe(300)
    expect(call.payload.label).toBe('editor')
    send({ protocol: 1, id: call.id, success: true, payload: {} })
    await pending
  })

  it('omits the label for the current window', async () => {
    installIpcMock()
    const pending = Window.getCurrent().setFullscreenOnMonitor(0, 0)
    const call = lastInvoke()!
    expect(call.payload.label).toBeUndefined()
    send({ protocol: 1, id: call.id, success: true, payload: {} })
    await pending
  })
})