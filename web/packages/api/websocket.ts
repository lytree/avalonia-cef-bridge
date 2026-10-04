import { Channel, invoke } from './ipc'

/** One frame pushed by the native websocket receive loop. */
export type WsMessageFrame =
  | { kind: 'text'; text: string }
  | { kind: 'binary'; dataBase64: string }
  | { kind: 'closed'; code: number; reason: string }

/** Handshake options for {@link TaruiWebSocket.connect}. */
export interface WsConnectConfig {
  /** Subprotocols to advertise in the `Sec-WebSocket-Protocol` header. */
  protocols?: string[]
  /** Extra handshake headers. */
  headers?: Record<string, string>
}

/**
 * A native websocket connection, mirroring the simplified tauri-plugin-websocket surface. Instances are
 * created via {@link TaruiWebSocket.connect}; every incoming frame (text, binary, and the terminal
 * `closed` frame) arrives through {@link onmessage}. The connect URL must be covered by the caller's
 * `plugin:websocket|connect` URL scopes, which are default-deny.
 */
export class TaruiWebSocket {
  private _id: string

  /** Native connection id used by the send/close commands. */
  get id(): string {
    return this._id
  }

  /** Receives every frame; the terminal `closed` frame fires exactly once per connection. */
  onmessage: ((frame: WsMessageFrame) => void) | undefined

  private constructor(id: string) {
    this._id = id
  }

  /** Opens a capability-scoped connection and resolves once the handshake completed. */
  static async connect(url: string, config: WsConnectConfig = {}): Promise<TaruiWebSocket> {
    const channel = new Channel<WsMessageFrame>()
    const headers = Object.entries(config.headers ?? {}).map(([name, value]) => ({ name, value }))
    const socket = new TaruiWebSocket('')
    channel.onmessage = frame => socket.onmessage?.(frame)
    const { id } = await invoke<{ id: string }>('plugin:websocket|connect', {
      url,
      protocols: config.protocols && config.protocols.length > 0 ? config.protocols : undefined,
      headers: headers.length > 0 ? headers : undefined,
      onMessage: channel,
    })
    socket._id = id
    return socket
  }

  /** Sends a text payload. */
  async send(text: string): Promise<void> {
    await invoke('plugin:websocket|send', { id: this._id, text })
  }

  /** Sends a binary payload. */
  async sendBinary(bytes: Uint8Array): Promise<void> {
    await invoke('plugin:websocket|send', { id: this._id, binaryBase64: base64Encode(bytes) })
  }

  /** Performs a close handshake; `code` defaults to a normal closure (1000). */
  async close(code?: number, reason?: string): Promise<void> {
    await invoke('plugin:websocket|close', { id: this._id, code, reason })
  }
}

function base64Encode(bytes: Uint8Array): string {
  let binary = ''
  for (const value of bytes) {
    binary += String.fromCharCode(value)
  }
  return btoa(binary)
}

export const websocket = { connect: TaruiWebSocket.connect.bind(TaruiWebSocket) } as const
