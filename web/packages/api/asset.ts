/**
 * Converts an absolute file path into a URL served by the restricted asset protocol.
 *
 * Mirrors Tauri's `convertFileSrc`: the path is percent-encoded as a single segment under
 * `asset://localhost/`, so a Windows path (`C:\photos\a.png`) becomes
 * `asset://localhost/C%3A%5Cphotos%5Ca.png` and a POSIX path (`/tmp/a.png`) becomes
 * `asset://localhost/%2Ftmp%2Fa.png`.
 *
 * Only files under the directories configured in `Tarui:Web:Asset:Roots` are served; a path
 * outside those roots resolves to a 403 instead of leaking the file.
 */
export function convertFileSrc(filePath: string, protocol = 'asset'): string {
  if (typeof filePath !== 'string' || filePath.length === 0) {
    throw new Error('convertFileSrc requires a non-empty file path.')
  }
  if (typeof protocol !== 'string' || protocol.length === 0) {
    throw new Error('convertFileSrc requires a non-empty protocol.')
  }

  return `${protocol}://localhost/${encodeURIComponent(filePath)}`
}
