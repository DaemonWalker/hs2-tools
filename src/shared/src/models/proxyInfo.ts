import { ProxyAgent } from 'undici'

export type ProxyInfo = Pick<ProxyAgent.Options, 'uri' | 'token'>
