import { invoke } from './ipc'

/** SQL storage class reported alongside every cell value. */
export type SqlValueType = 'null' | 'integer' | 'real' | 'text' | 'blob'

/**
 * A single SQL cell. `value` carries the payload as a string so the wire format stays JSON:
 * integers and reals are formatted invariantly, blobs are base64. `isNull` distinguishes SQL NULL
 * from an empty string and from zero, which a plain nullable string cannot express.
 */
export interface SqlValue {
  value?: string | null | undefined
  type: SqlValueType
  isNull: boolean
}

/** One result row; `cells` is positional and maps to the query's select list order. */
export interface SqlRow {
  cells: SqlValue[]
}

export interface SqlSelectResult {
  rows: SqlRow[]
}

export interface SqlExecuteResult {
  rowsAffected: number
  lastInsertId: number
}

export interface SqlCloseResult {
  closed: boolean
}

export interface SqlLoadOptions {
  /** Connection string whose scheme selects the driver, e.g. `sqlite://app.db`. */
  connection: string
  /**
   * Whether a missing database file is created. Defaults to true, matching Tauri sql 2.6.0.
   * Set to false to fail instead of silently creating an empty database.
   */
  autoCreate?: boolean | undefined
}

export interface SqlExecuteOptions {
  /** Handle returned by {@link load}. */
  db: string
  /** SQL text. Use `?` placeholders and pass the values separately. */
  query: string
  /** Positional bind values, in `?` order. */
  values?: SqlValue[] | undefined
}

export const NULL: SqlValue = { value: null, type: 'null', isNull: true }

/** Builds an integer cell. */
export function integer(value: number): SqlValue {
  return { value: String(value), type: 'integer', isNull: false }
}

/** Builds a real (floating point) cell. */
export function real(value: number): SqlValue {
  return { value: String(value), type: 'real', isNull: false }
}

/** Builds a text cell. */
export function text(value: string): SqlValue {
  return { value, type: 'text', isNull: false }
}

/** Builds a blob cell from raw bytes; they are carried as base64 over the wire. */
export function blob(value: Uint8Array): SqlValue {
  let binary = ''
  for (const byte of value) {
    binary += String.fromCharCode(byte)
  }
  return { value: btoa(binary), type: 'blob', isNull: false }
}

/**
 * Reads a text cell as a string. Returns `null` for SQL NULL.
 */
export function textValue(cell: SqlValue): string | null {
  return cell.isNull ? null : (cell.value ?? null)
}

/**
 * Reads an integer cell as a number. Returns `null` for SQL NULL.
 */
export function integerValue(cell: SqlValue): number | null {
  return cell.isNull ? null : Number(cell.value ?? 0)
}

/** Reads a real cell as a number. Returns `null` for SQL NULL. */
export function realValue(cell: SqlValue): number | null {
  return cell.isNull ? null : Number(cell.value ?? 0)
}

/** Reads a blob cell as bytes. Returns `null` for SQL NULL. */
export function blobValue(cell: SqlValue): Uint8Array | null {
  if (cell.isNull || !cell.value) {
    return null
  }
  const binary = atob(cell.value)
  const bytes = new Uint8Array(binary.length)
  for (let i = 0; i < binary.length; i += 1) {
    bytes[i] = binary.charCodeAt(i)
  }
  return bytes
}

/**
 * Opens a database and returns an opaque handle used by every later statement.
 *
 * `sqlite://name.db` opens a file inside the application data directory and requires the
 * capability to name that file in a `sql:allow-load` scope. `sqlite::memory:` opens a private
 * in-memory database that touches no disk.
 */
export async function load(options: SqlLoadOptions): Promise<string> {
  return invoke<string>('plugin:sql|load', options)
}

/** Runs a query and returns every row. */
export async function select(options: SqlExecuteOptions): Promise<SqlSelectResult> {
  return invoke<SqlSelectResult>('plugin:sql|select', options)
}

/** Runs a statement that returns no rows and reports the affected count and last insert id. */
export async function execute(options: SqlExecuteOptions): Promise<SqlExecuteResult> {
  return invoke<SqlExecuteResult>('plugin:sql|execute', options)
}

/**
 * Closes a handle. Calling it twice is safe: the second call reports `closed: false`, so a
 * `finally` block can always close without tracking state.
 */
export async function close(options: SqlExecuteOptions): Promise<SqlCloseResult> {
  return invoke<SqlCloseResult>('plugin:sql|close', options)
}

/**
 * Opens a database and returns a {@link SqlDatabaseHandle} bound to the new handle.
 *
 * ```ts
 * const db = await openDatabase('sqlite://notes.db')
 * try {
 *   await db.execute('CREATE TABLE IF NOT EXISTS notes (id INTEGER PRIMARY KEY, body TEXT)')
 *   await db.insert('INSERT INTO notes (body) VALUES (?)', [text('hello')])
 * } finally {
 *   await db.close()
 * }
 * ```
 *
 * Named `openDatabase` rather than a `Database.load` static so it does not shadow the module-level
 * {@link load} function, which would recurse.
 */
export async function openDatabase(connection: string): Promise<SqlDatabaseHandle> {
  const handle = await load({ connection })
  return new SqlDatabaseHandle(handle)
}

/**
 * An open database handle. Mirrors Tauri's `Database` object so existing call sites migrate with
 * a shape change rather than a rewrite.
 */
export class SqlDatabaseHandle {
  // A field rather than a parameter property: the package builds with `erasableSyntaxOnly`,
  // which rejects syntax that needs emit-time transformation.
  private readonly handle: string

  constructor(handle: string) {
    this.handle = handle
  }

  /** The underlying opaque handle id. */
  get id(): string {
    return this.handle
  }

  /** Runs a query and returns the rows. */
  async select(query: string, values?: SqlValue[]): Promise<SqlRow[]> {
    const result = await select({ db: this.handle, query, values })
    return result.rows
  }

  /** Runs a statement and returns the affected row count. */
  async execute(query: string, values?: SqlValue[]): Promise<number> {
    const result = await execute({ db: this.handle, query, values })
    return result.rowsAffected
  }

  /** Runs an insert and returns the new row id. */
  async insert(query: string, values?: SqlValue[]): Promise<number> {
    const result = await execute({ db: this.handle, query, values })
    return result.lastInsertId
  }

  /** Closes the handle. Safe to call more than once. */
  async close(): Promise<void> {
    await close({ db: this.handle, query: '' })
  }
}

export const sql = {
  load,
  select,
  execute,
  close,
  openDatabase,
  SqlDatabaseHandle,
  NULL,
  integer,
  real,
  text,
  blob,
  textValue,
  integerValue,
  realValue,
  blobValue,
} as const