import { invoke } from './ipc'

export const cliArgKinds = ['flag', 'text', 'text-list', 'number', 'number-list'] as const
export type CliArgKind = (typeof cliArgKinds)[number]

/** A declaration for one long (`--name`) and/or short (`-x`) CLI option the parser recognizes. */
export interface CliArgSpec {
  name: string
  shortName?: string
  kind?: CliArgKind
  multiple?: boolean
  required?: boolean
  description?: string
}

export interface CliParseOptions {
  options: CliArgSpec[]
  positionalName?: string
  positionalRequired?: boolean
  /** Process arguments; defaults to the current process args (executable name stripped). */
  args?: string[]
}

/** One parsed option value. Only the fields matching `kind` are populated. */
export type CliArgValue =
  | { name: string; kind: 'flag'; present: boolean }
  | { name: string; kind: 'text'; present: boolean; value: string | null }
  | { name: string; kind: 'text-list'; present: boolean; values: string[] }
  | { name: string; kind: 'number'; present: boolean; number: number | null }
  | { name: string; kind: 'number-list'; present: boolean; numbers: number[] }

export interface CliParseResult {
  success: boolean
  error: string | null
  values: CliArgValue[]
  positionalName: string | null
  positionals: string[]
}

/**
 * Parses CLI arguments against a declared schema. A required option that is absent, a malformed typed value,
 * or an unknown option fails the whole parse with a descriptive `error`.
 */
export async function parseCli(options: CliParseOptions): Promise<CliParseResult> {
  return invoke<CliParseResult>('core:cli|parse', {
    options: options.options,
    positionalName: options.positionalName ?? undefined,
    positionalRequired: options.positionalRequired ?? undefined,
    args: options.args && options.args.length > 0 ? options.args : undefined,
  })
}

/** Parses the current process arguments against a declared schema. */
export async function matches(options: CliParseOptions): Promise<CliParseResult> {
  return parseCli(options)
}

export const cli = { parse: parseCli, matches } as const