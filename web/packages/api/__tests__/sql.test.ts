import { afterEach, describe, expect, it } from 'vitest'
import {
  NULL,
  blob,
  blobValue,
  close,
  execute,
  integer,
  integerValue,
  load,
  openDatabase,
  real,
  realValue,
  select,
  text,
  textValue,
} from '../sql'
import { cleanupIpcMock, installIpcMock, lastInvoke, send } from './ipcMock'

afterEach(() => cleanupIpcMock())

describe('cell constructors', () => {
  it('encodes each storage class with its type tag', () => {
    expect(integer(42)).toEqual({ value: '42', type: 'integer', isNull: false })
    expect(real(1.5)).toEqual({ value: '1.5', type: 'real', isNull: false })
    expect(text('hi')).toEqual({ value: 'hi', type: 'text', isNull: false })
  })

  it('marks SQL NULL distinctly from an empty string', () => {
    expect(NULL.isNull).toBe(true)
    expect(text('').isNull).toBe(false)
  })

  it('round-trips blob bytes through base64', () => {
    const cell = blob(new Uint8Array([1, 2, 3]))
    expect(cell.type).toBe('blob')
    expect(Array.from(blobValue(cell)!)).toEqual([1, 2, 3])
  })
})

describe('cell readers', () => {
  it('returns null for a SQL NULL cell and the payload otherwise', () => {
    expect(textValue(NULL)).toBeNull()
    expect(textValue(text('hello'))).toBe('hello')
    expect(integerValue(integer(7))).toBe(7)
    expect(realValue(real(0.5))).toBe(0.5)
    expect(blobValue(NULL)).toBeNull()
  })
})

describe('plugin:sql commands', () => {
  it('load sends the connection string', async () => {
    installIpcMock()
    const pending = load({ connection: 'sqlite://app.db' })
    const call = lastInvoke()!
    expect(call.command).toBe('plugin:sql|load')
    expect(call.payload.connection).toBe('sqlite://app.db')
    send({ protocol: 1, id: call.id, success: true, payload: 'db1' })
    expect(await pending).toBe('db1')
  })

  it('forwards autoCreate so a missing database is not created', async () => {
    installIpcMock()
    const pending = load({ connection: 'sqlite://app.db', autoCreate: false })
    const call = lastInvoke()!
    expect(call.command).toBe('plugin:sql|load')
    expect(call.payload.autoCreate).toBe(false)
    send({ protocol: 1, id: call.id, success: true, payload: 'db1' })
    expect(await pending).toBe('db1')
  })

  it('select forwards the handle, query and bound values', async () => {
    installIpcMock()
    const pending = select({ db: 'db1', query: 'SELECT * FROM t WHERE id = ?', values: [integer(1)] })
    const call = lastInvoke()!
    expect(call.command).toBe('plugin:sql|select')
    expect(call.payload.db).toBe('db1')
    expect(call.payload.query).toBe('SELECT * FROM t WHERE id = ?')
    expect(call.payload.values).toEqual([{ value: '1', type: 'integer', isNull: false }])
    send({ protocol: 1, id: call.id, success: true, payload: { rows: [] } })
    expect((await pending).rows).toEqual([])
  })

  it('execute returns rows affected and the last insert id', async () => {
    installIpcMock()
    const pending = execute({ db: 'db1', query: 'INSERT INTO t VALUES (?)', values: [text('a')] })
    const call = lastInvoke()!
    expect(call.command).toBe('plugin:sql|execute')
    send({ protocol: 1, id: call.id, success: true, payload: { rowsAffected: 1, lastInsertId: 5 } })
    expect(await pending).toEqual({ rowsAffected: 1, lastInsertId: 5 })
  })

  it('close reports whether the handle was still open', async () => {
    installIpcMock()
    const pending = close({ db: 'db1', query: '' })
    const call = lastInvoke()!
    expect(call.command).toBe('plugin:sql|close')
    send({ protocol: 1, id: call.id, success: true, payload: { closed: false } })
    expect((await pending).closed).toBe(false)
  })
})

describe('database handle', () => {
  it('wraps a handle and addresses every statement to it', async () => {
    installIpcMock()
    const opening = openDatabase('sqlite::memory:')
    const openCall = lastInvoke()!
    send({ protocol: 1, id: openCall.id, success: true, payload: 'db9' })
    const db = await opening
    expect(db.id).toBe('db9')

    const selecting = db.select('SELECT 1')
    const selectCall = lastInvoke()!
    expect(selectCall.command).toBe('plugin:sql|select')
    expect(selectCall.payload.db).toBe('db9')
    send({
      protocol: 1,
      id: selectCall.id,
      success: true,
      payload: { rows: [{ cells: [{ value: '1', type: 'integer', isNull: false }] }] },
    })
    const rows = await selecting
    expect(integerValue(rows[0].cells[0])).toBe(1)

    const inserting = db.insert('INSERT INTO t VALUES (?)', [text('x')])
    const insertCall = lastInvoke()!
    expect(insertCall.command).toBe('plugin:sql|execute')
    send({ protocol: 1, id: insertCall.id, success: true, payload: { rowsAffected: 1, lastInsertId: 3 } })
    expect(await inserting).toBe(3)

    const closing = db.close()
    const closeCall = lastInvoke()!
    expect(closeCall.command).toBe('plugin:sql|close')
    send({ protocol: 1, id: closeCall.id, success: true, payload: { closed: true } })
    await closing
  })
})