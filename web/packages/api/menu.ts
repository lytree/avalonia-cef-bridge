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

export async function setWindowMenu(options: SetWindowMenuOptions): Promise<void> {
  await invoke('plugin:menu|set-window-menu', options)
}

export async function updateItem(options: MenuUpdateItemOptions): Promise<void> {
  await invoke('plugin:menu|update-item', options)
}

export async function removeWindowMenu(): Promise<void> {
  await invoke('plugin:menu|remove-window-menu', {})
}

export const menu = {
  setWindowMenu,
  updateItem,
  removeWindowMenu,
} as const