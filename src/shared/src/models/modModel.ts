export type ModModel = Record<
  string,
  { name: string; version: string; path: string; used: number } | undefined
>
