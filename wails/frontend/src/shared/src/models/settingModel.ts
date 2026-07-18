import { ProxyInfo } from './proxyInfo'

export interface SettingModel {
  path: string | undefined
  proxy: ProxyInfo
  windowsSleep?: {
    disabled: boolean
    taskId?: number
  }
}

export type SettingWindowForm = Omit<SettingModel, 'path'>
