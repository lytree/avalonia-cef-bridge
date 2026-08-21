import { invoke } from './ipc'
import type { MenuItemDefinition } from './menu'

export interface TrayCreateOptions {
  id: string
  tooltip?: string | undefined
  visible?: boolean
  icon?: string | undefined
  showMenuOnLeftClick?: boolean
  menu?: MenuItemDefinition[] | undefined
}

export interface TraySetMenuOptions {
  id: string
  menu: MenuItemDefinition[]
}

export interface TraySetIconOptions {
  id: string
  icon?: string | undefined
}

export interface TraySetTooltipOptions {
  id: string
  tooltip?: string | undefined
}

export interface TraySetVisibleOptions {
  id: string
  visible: boolean
}

export interface TrayRemoveOptions {
  id: string
}

export interface TrayClicked {
  id: string
  button?: string | undefined
}

export interface TrayMenuItemClicked {
  id: string
  itemId: string
  text?: string | undefined
}

export async function create(options: TrayCreateOptions): Promise<void> {
  await invoke('plugin:tray|create', options)
}

export async function setMenu(options: TraySetMenuOptions): Promise<void> {
  await invoke('plugin:tray|set-menu', options)
}

export async function setIcon(options: TraySetIconOptions): Promise<void> {
  await invoke('plugin:tray|set-icon', options)
}

export async function setTooltip(options: TraySetTooltipOptions): Promise<void> {
  await invoke('plugin:tray|set-tooltip', options)
}

export async function setVisible(options: TraySetVisibleOptions): Promise<void> {
  await invoke('plugin:tray|set-visible', options)
}

export async function remove(options: TrayRemoveOptions): Promise<void> {
  await invoke('plugin:tray|remove', options)
}

export const tray = {
  create,
  setMenu,
  setIcon,
  setTooltip,
  setVisible,
  remove,
} as const