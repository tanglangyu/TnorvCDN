declare module '@/utils/rapiddev-crypto-sdk.js' {
  export const SDK_VERSION: string
  export class CryptoSession {
    constructor(opts?: any)
    ensureSession(): Promise<void>
    forceRehandshake(): Promise<void>
    sendAes(inner: any, retryCount?: number): Promise<any>
  }
  export function installCryptoInterceptor(
    axiosInstance: any,
    opts?: { baseUrl?: string; mode?: string; debug?: boolean; enabled?: boolean | (() => boolean); passthroughPaths?: string[] },
  ): () => void
}
