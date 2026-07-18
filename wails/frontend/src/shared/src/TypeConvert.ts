export type Stringify<T> = {
  [P in keyof T]: string
}
