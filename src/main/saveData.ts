import { app } from 'electron'
import { join } from 'path'
import fs from 'fs'

const save = (path: string, data: any) => fs.writeFileSync(path, JSON.stringify(data))
const load = (path: string) =>
  fs.existsSync(path) ? JSON.parse(fs.readFileSync(path).toString('utf8')) : undefined

const settingsPath = join(app.getPath('appData'), 'hs2-tools', 'setting.json')
const localModsPath = join(app.getPath('appData'), 'hs2-tools', 'localMods.json')

export const saveSettings = (settings: any) => save(settingsPath, settings)
export const loadSettings = () => load(settingsPath)

export const saveLocalMods = (mods: any) => save(localModsPath, mods)
export const loadLocalMods = () => load(localModsPath)
