import { ipcHandler } from '../../../main/ipcHandler'

type BaseType<T> = T extends string
  ? string
  : T extends number
    ? number
    : T extends boolean
      ? boolean
      : T extends symbol
        ? symbol
        : T extends bigint
          ? bigint
          : T

type TransformFunction<T> = T extends (_: any, ...args: infer Args) => infer Return
  ? Return extends Promise<infer U>
    ? (...args: Args) => Promise<BaseType<U>> // 已经是 Promise，提取 U 并转换基类型
    : (...args: Args) => Promise<BaseType<Return>> // 非 Promise，转换基类型后包装
  : T

type TransformObject<T> = {
  [K in keyof T]: T[K] extends (...args: any) => any ? TransformFunction<T[K]> : T[K]
}

export type IPCHandlerModel = TransformObject<typeof ipcHandler.handle>
