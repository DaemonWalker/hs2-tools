export const pathJoin = (...parts: string[]) => {
  const sep = '/'
  return parts.join(sep).replace(new RegExp(sep + '{1,}', 'g'), sep)
}

