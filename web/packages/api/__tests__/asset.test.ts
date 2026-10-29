import { describe, expect, it } from 'vitest'
import { convertFileSrc } from '../asset'

describe('convertFileSrc', () => {
  it('encodes a Windows path as a single asset:// segment', () => {
    expect(convertFileSrc('C:\\photos\\a b.png')).toBe(
      'asset://localhost/C%3A%5Cphotos%5Ca%20b.png',
    )
  })

  it('encodes a POSIX path so leading slashes stay inside the segment', () => {
    expect(convertFileSrc('/tmp/a.png')).toBe('asset://localhost/%2Ftmp%2Fa.png')
  })

  it('honours a custom protocol for applications that register one', () => {
    expect(convertFileSrc('/tmp/a.png', 'media')).toBe('media://localhost/%2Ftmp%2Fa.png')
  })

  it('rejects an empty path or protocol instead of producing a root URL', () => {
    expect(() => convertFileSrc('')).toThrow(/non-empty file path/)
    expect(() => convertFileSrc('/tmp/a.png', '')).toThrow(/non-empty protocol/)
  })
})
