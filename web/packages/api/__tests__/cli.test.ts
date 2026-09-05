import { afterEach, describe, expect, it } from 'vitest'
import { cli, parseCli, matches } from '../cli'
import { cleanupIpcMock, installIpcMock, lastInvoke, send } from './ipcMock'

afterEach(() => cleanupIpcMock())

describe('cli.parse', () => {
  it('invokes parse with the declared schema and args', async () => {
    installIpcMock()
    const pending = parseCli({ options: [{ name: 'verbose', kind: 'flag' }], args: ['--verbose'] })

    const call = lastInvoke()!
    expect(call.command).toBe('core:cli|parse')
    expect(call.payload.options).toEqual([{ name: 'verbose', kind: 'flag' }])
    expect(call.payload.args).toEqual(['--verbose'])

    send({
      protocol: 1,
      id: call.id,
      success: true,
      payload: {
        success: true,
        error: null,
        values: [{ name: 'verbose', kind: 'flag', present: true }],
        positionalName: null,
        positionals: [],
      },
    })

    const result = await pending
    expect(result.success).toBe(true)
    expect(result.values[0]).toMatchObject({ name: 'verbose', present: true })
  })

  it('exposes matches as a shorthand', async () => {
    installIpcMock()
    const pending = matches({ options: [] })
    const call = lastInvoke()!
    expect(call.command).toBe('core:cli|parse')
    send({ protocol: 1, id: call.id, success: true, payload: { success: true, error: null, values: [], positionalName: null, positionals: [] } })
    const result = await pending
    expect(result.success).toBe(true)
  })
})

describe('cli module', () => {
  it('exposes parse and matches on the module namespace', () => {
    expect(cli.parse).toBe(parseCli)
    expect(cli.matches).toBe(matches)
  })
})