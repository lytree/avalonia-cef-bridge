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

/** Appends items to the end of the calling window's root menu level. */
export interface MenuAppendOptions {
  items: MenuItemDefinition[]
}

/** Inserts items into the calling window's root menu level at the given index. */
export interface MenuInsertOptions {
  index: number
  items: MenuItemDefinition[]
}

/** Removes the item with the given id (depth-first search) from the calling window's menu. */
export interface MenuRemoveOptions {
  id: string
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

export async function appendItems(options: MenuAppendOptions): Promise<void> {
  await invoke('plugin:menu|append', options)
}

export async function insertItems(options: MenuInsertOptions): Promise<void> {
  await invoke('plugin:menu|insert', options)
}

export async function removeItem(options: MenuRemoveOptions): Promise<void> {
  await invoke('plugin:menu|remove', options)
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
  appendItems,
  insertItems,
  removeItem,
  removeWindowMenu,
  showContextMenu,
} as const