let db: IDBDatabase | null = null

const openDB = (dbName: string, ...storeNames: string[]): Promise<void> => {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(dbName, 1)
    request.onupgradeneeded = (event) => {
      const db = (event.target as IDBOpenDBRequest).result
      for (const storeName of storeNames) {
        if (!db.objectStoreNames.contains(storeName)) {
          db.createObjectStore(storeName, { keyPath: 'id', autoIncrement: true })
        }
      }
    }
    request.onsuccess = (event) => {
      db = (event.target as IDBOpenDBRequest).result
      resolve()
    }
    request.onerror = (event) => {
      console.error((event.target as IDBOpenDBRequest).error)
      reject((event.target as IDBOpenDBRequest).error)
    }
  })
}

const insert = (storeName: string, data: any): Promise<number> => {
  return new Promise((resolve, reject) => {
    if (!db) {
      reject(new Error('Database is not open'))
      return
    }

    const transaction = db.transaction([storeName], 'readwrite')
    const store = transaction.objectStore(storeName)
    const request = store.add(data)

    request.onsuccess = (event) => {
      resolve((event.target as IDBRequest).result as number)
    }

    request.onerror = (event) => {
      reject((event.target as IDBRequest).error)
    }
  })
}

const get = (storeName: string): Promise<any> => {
  return new Promise((resolve, reject) => {
    if (!db) {
      reject(new Error('Database is not open'))
      return
    }

    const transaction = db.transaction([storeName], 'readonly')
    const store = transaction.objectStore(storeName)
    const request = store.getAll()
    request.onsuccess = (event) => {
      const result = (event.target as IDBRequest).result
      if (result && result.length > 0) {
        resolve(result[0])
      } else {
        resolve(undefined)
      }
    }

    request.onerror = (event) => {
      reject((event.target as IDBRequest).error)
    }
  })
}

const clearStore = (storeName: string): Promise<void> => {
  return new Promise((resolve, reject) => {
    if (!db) {
      reject(new Error('Database is not open'))
      return
    }

    const transaction = db.transaction([storeName], 'readwrite')
    const store = transaction.objectStore(storeName)
    const request = store.clear()
    request.onsuccess = () => {
      resolve()
    }
    request.onerror = (event) => {
      reject((event.target as IDBRequest).error)
    }
  })
}

export const initDB = () =>
  openDB('modDB', SETTINGS_STORE_NAME, SCENE_STORE_NAME, LOCAL_MODS_STORE_NAME)

export const closeDB = () => {
  if (db) {
    db.close()
    db = null
  }
}

const SETTINGS_STORE_NAME = 'settings'
export const getSettings = () => {
  return get(SETTINGS_STORE_NAME)
}

export const saveSettings = async (data: any) => {
  await clearStore(SETTINGS_STORE_NAME)
  await insert(SETTINGS_STORE_NAME, data)
}

const SCENE_STORE_NAME = 'scene'
export const getScene = () => {
  return get(SCENE_STORE_NAME)
}

export const saveScene = async (data: any) => {
  await clearStore(SCENE_STORE_NAME)
  await insert(SCENE_STORE_NAME, data)
}

const LOCAL_MODS_STORE_NAME = 'localMods'
export const getLocalMods = () => {
  return get(LOCAL_MODS_STORE_NAME)
}

export const saveLocalMods = async (data: any) => {
  await clearStore(LOCAL_MODS_STORE_NAME)
  await insert(LOCAL_MODS_STORE_NAME, data)
}
