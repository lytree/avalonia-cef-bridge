import { invoke } from './ipc'

export const menuItemKinds = {
  Normal: 'normal',
  Divider: 'divider',
  Check: 'check',
  Submenu: 'submenu',
} as const

export type MenuItemKind = (typeof menuItemKinds)[keyof typeof menuItemKinds]

export interface MenuItemDefinition {
  id: string
  kind?: MenuItemKind
  text?: string | undefined
  enabled?: boolean | undefined
  checked?: boolean | undefined
  accelerator?: string | undefined
  items?: MenuItemDefinition[] | undefined
}

export interface SetWindowMenuOptions {
  items: MenuItemDefinition[]
}

export interface MenuUpdateItemOptions {
  id: string
  text?: string | undefined
  enabled?: boolean | undefined
  checked?: boolean | undefined
}

export interface MenuItemClicked {
  id: string
  text?: string | undefined
  checked?: boolean | undefined
}

export interface ContextMenuOptions {
  items: MenuItemDefinition[]
  /** Logical X coordinate relative to the window's top-left. */
  x?: number
  /** Logical Y coordinate relative to the window's top-left. */
  y?: number
}

export async function setWindowMenu(options: SetWindowMenuOptions): Promise<void> {
  await invoke('plugin:menu|set-window-menu', options)
}

export async function updateItem(options: MenuUpdateItemOptions): Promise<void> {
  await invoke('plugin:menu|update-item', options)
}

export async function removeWindowMenu(): Promise<void> {
  await invoke('plugin:menu|remove-window-menu', {})
}

/**
 * Pops a temporary context menu on the calling window at the requested (x, y) logical coordinates.
 * Item clicks route to the same `menu://item-clicked` event as the window menu.
 */
export async function showContextMenu(options: ContextMenuOptions): Promise<void> {
  await invoke('plugin:menu|show-context-menu', options)
}

export const menu = {
  setWindowMenu,
  updateItem,
  removeWindowMenu,
  showContextMenu,
} as const