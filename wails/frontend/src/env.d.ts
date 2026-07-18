/// <reference types="vite/client" />

// 声明资源文件导入
declare module '*?asset' {
  const src: string
  export default src
}

declare module '*.zip?asset' {
  const src: string
  export default src
}
