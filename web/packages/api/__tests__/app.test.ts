import { afterEach, describe, expect, it } from 'vitest'
import { exit } from '../app'
import { cleanupIpcMock, installIpcMock, lastInvoke, send } from './ipcMock'

afterEach(() => cleanupIpcMock())

describe('app.exit', () => {
  it('defaults the exit code to zero', async () => {
    installIpcMock()
    const pending = exit()
    const call = lastInvoke()!
    expect(call.command).toBe('core:app|exit')
    expect(call.payload.code).toBe(0)
    send({ protocol: 1, id: call.id, success: true, payload: {} })
    await pending
  })

  it('forwards an explicit exit code', async () => {
    installIpcMock()
    const pending = exit(3)
    const call = lastInvoke()!
    expect(call.command).toBe('core:app|exit')
    expect(call.payload.code).toBe(3)
    send({ protocol: 1, id: call.id, success: true, payload: {} })
    await pending
  })
})
