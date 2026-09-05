import { invoke } from './ipc'

/** Reads plain text from the system clipboard. */
export async function readText(): Promise<string> {
  const result = await invoke<{ text: string }>('core:clipboard|read-text', {})
  return result.text
}

/** Writes plain text to the system clipboard. */
export async function writeText(text: string): Promise<void> {
  await invoke('core:clipboard|write-text', { text })
}

export interface ClipboardHtmlReadResult {
  /** False when the clipboard holds no rich HTML. */
  available: boolean
  html: string | null
}

export interface ClipboardImageReadResult {
  /** False when the clipboard holds no image. */
  available: boolean
  /** The image as PNG bytes. */
  png: Uint8Array | null
}

/**
 * Reads rich HTML from the clipboard. Returns `available: false` when the clipboard holds no HTML so the
 * caller can degrade honestly instead of guessing from an empty string.
 */
export async function readHtml(): Promise<ClipboardHtmlReadResult> {
  return invoke<ClipboardHtmlReadResult>('core:clipboard|read-html', {})
}

/** Writes rich HTML to the clipboard, optionally with a plain-text fallback. */
export async function writeHtml(html: string, plainText?: string): Promise<void> {
  await invoke('core:clipboard|write-html', { html, plainText: plainText ?? undefined })
}

/**
 * Reads the clipboard image as PNG bytes. Returns `available: false` when the clipboard holds no image.
 * Bytes are transported base64 by the native bridge and decoded here.
 */
export async function readImage(): Promise<ClipboardImageReadResult> {
  const result = await invoke<{ available: boolean; png: string | null }>('core:clipboard|read-image', {})
  return { available: result.available, png: result.png === null ? null : toBytes(result.png) }
}

/** Writes a PNG image to the clipboard. Provide the image as PNG-encoded bytes. */
export async function writeImage(png: Uint8Array): Promise<void> {
  await invoke('core:clipboard|write-image', { png: toBase64(png) })
}

function toBase64(bytes: Uint8Array): string {
  let binary = ''
  for (const value of bytes) {
    binary += String.fromCharCode(value)
  }
  return btoa(binary)
}

function toBytes(base64: string): Uint8Array {
  const binary = atob(base64)
  const bytes = new Uint8Array(binary.length)
  for (let index = 0; index < binary.length; index++) {
    bytes[index] = binary.charCodeAt(index)
  }
  return bytes
}
