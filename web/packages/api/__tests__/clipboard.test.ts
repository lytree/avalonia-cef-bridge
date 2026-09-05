import { afterEach, describe, expect, it } from 'vitest'
import { readHtml, writeHtml, readImage, writeImage } from '../clipboard'
import { cleanupIpcMock, installIpcMock, lastInvoke, send } from './ipcMock'

afterEach(() => cleanupIpcMock())

describe('clipboard html', () => {
  it('writes html with a plain-text fallback', async () => {
    installIpcMock()
    const pending = writeHtml('<b>hi</b>', 'hi')
    const call = lastInvoke()!
    expect(call.command).toBe('core:clipboard|write-html')
    expect(call.payload.html).toBe('<b>hi</b>')
    expect(call.payload.plainText).toBe('hi')
    send({ protocol: 1, id: call.id, success: true, payload: {} })
    await pending
  })

  it('reads html and surfaces availability', async () => {
    installIpcMock()
    const pending = readHtml()
    const call = lastInvoke()!
    expect(call.command).toBe('core:clipboard|read-html')
    send({ protocol: 1, id: call.id, success: true, payload: { available: true, html: '<b>hi</b>' } })
    const result = await pending
    expect(result.available).toBe(true)
    expect(result.html).toBe('<b>hi</b>')
  })
})

describe('clipboard image', () => {
  it('encodes png to base64 on write and decodes on read', async () => {
    installIpcMock()
    const png = new Uint8Array([80, 78, 71, 13, 10]) // "PNG\r\n"
    const pendingWrite = writeImage(png)
    const writeCall = lastInvoke()!
    expect(writeCall.command).toBe('core:clipboard|write-image')
    expect(writeCall.payload.png).toBe(btoa('PNG\r\n'))
    send({ protocol: 1, id: writeCall.id, success: true, payload: {} })
    await pendingWrite

    const pendingRead = readImage()
    const readCall = lastInvoke()!
    expect(readCall.command).toBe('core:clipboard|read-image')
    send({ protocol: 1, id: readCall.id, success: true, payload: { available: true, png: btoa('PNG\r\n') } })
    const result = await pendingRead
    expect(result.available).toBe(true)
    expect(result.png).toEqual(png)
  })
})