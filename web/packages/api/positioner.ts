import { invoke } from './ipc'

/** Anchor slot the window is pinned to. `Tray*` anchors currently fall back to the matching bottom-of-work-area slot. */
export type PositionerAnchor =
  | 'TopLeft'
  | 'TopRight'
  | 'BottomLeft'
  | 'BottomRight'
  | 'TopCenter'
  | 'BottomCenter'
  | 'LeftCenter'
  | 'RightCenter'
  | 'TrayLeft'
  | 'TrayRight'
  | 'TrayCenter'
  | 'TrayBottomLeft'
  | 'TrayBottomRight'
  | 'TrayBottomCenter'

/** Placement refinements applied on top of the anchor. */
export interface PositionerPlacement {
  /** Target window label; defaults to the calling window. */
  label?: string
  /** Margin offset in physical pixels added to the anchored position. */
  x?: number
  /** Margin offset in physical pixels added to the anchored position. */
  y?: number
}

/** Moves a window to the given anchor inside its monitor's work area. */
export async function setPosition(anchor: PositionerAnchor, placement: PositionerPlacement = {}): Promise<void> {
  await invoke('plugin:positioner|set-position', {
    anchor,
    label: placement.label,
    x: placement.x,
    y: placement.y,
  })
}

/** Moves a window to a taskbar-flavored anchor (degrades to the matching bottom-of-work-area slot). */
export async function setTrayPosition(
  anchor: Extract<PositionerAnchor, `Tray${string}`>,
  placement: PositionerPlacement = {},
): Promise<void> {
  await setPosition(anchor, placement)
}

export const positioner = { setPosition, setTrayPosition } as const
