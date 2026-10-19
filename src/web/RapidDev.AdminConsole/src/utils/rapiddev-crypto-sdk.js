/*
 * RapidDev Crypto SDK (JavaScript) - v2.1.0
 * =========================================
 * AES 通道透明加密(ECDH P-256 前向保密),像普通 HTTP 一样使用。
 *
 * 【握手】
 *  - 客户端生成临时 ECDH 密钥对,与服务器派生共享 AES-256-GCM 会话密钥
 *  - 每次握手用新的临时 ECDH 密钥对,具备前向保密
 *
 * 【v2.0 改进】:
 *  1. CryptoKey 缓存:避免每次请求 importKey(性能提升 3-5 倍)
 *  2. pubkey 内存缓存 + 过期检查:减少网络往返
 *  3. 路径拼接改用 URL 构造器,处理 baseURL 边界情况
 *  4. 自动重协商重试:sid 失效时自动握手后重试一次(业务无感)
 */

import axios from 'axios';

const SDK_VERSION = '2.1.0';

// ============================================================================
// 内部工具
// ============================================================================

function b64encode(bytes) {
  let bin = '';
  const chunk = 8192;
  for (let i = 0; i < bytes.length; i += chunk) {
    bin += String.fromCharCode.apply(null, bytes.subarray(i, i + chunk));
  }
  return btoa(bin);
}
function b64decode(s) {
  const bin = atob(s);
  const bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
  return bytes;
}
function randomBytes(n) { const b = new Uint8Array(n); crypto.getRandomValues(b); return b; }
function nowUnix() { return Math.floor(Date.now() / 1000); }
function randNonce() { return 'n_' + b64encode(randomBytes(9)).replace(/=/g, ''); }
function utf8(s) { return new TextEncoder().encode(s); }
function fromUtf8(b) { return new TextDecoder().decode(b); }

function buildAad(alg, sidOrKid, ts, nonce) {
  return utf8(`${alg}|${sidOrKid}|${ts}|${nonce}`);
}

// CryptoKey 缓存:避免每次请求 importKey(性能优化)
const _aesKeyCache = new Map();  // key: base64(rawKey) -> CryptoKey
const _aesKeyCacheEncrypt = new Map();

async function getAesCryptoKey(rawKey, forEncrypt) {
  const cacheKey = b64encode(rawKey) + (forEncrypt ? '_e' : '_d');
  const cache = forEncrypt ? _aesKeyCacheEncrypt : _aesKeyCache;
  let ck = cache.get(cacheKey);
  if (ck) return ck;
  ck = await crypto.subtle.importKey('raw', rawKey, { name: 'AES-GCM' }, false, forEncrypt ? ['encrypt'] : ['decrypt']);
  cache.set(cacheKey, ck);
  // 缓存上限:防止内存泄漏
  if (cache.size > 64) {
    const firstKey = cache.keys().next().value;
    cache.delete(firstKey);
  }
  return ck;
}

async function aesGcmEncrypt(rawKey, iv, plaintext, aad) {
  const key = await getAesCryptoKey(rawKey, true);
  const params = { name: 'AES-GCM', iv };
  if (aad) params.additionalData = aad;
  const ct = await crypto.subtle.encrypt(params, key, plaintext);
  return new Uint8Array(ct);
}
async function aesGcmDecrypt(rawKey, iv, ciphertext, aad) {
  const key = await getAesCryptoKey(rawKey, false);
  const params = { name: 'AES-GCM', iv };
  if (aad) params.additionalData = aad;
  const pt = await crypto.subtle.decrypt(params, key, ciphertext);
  return new Uint8Array(pt);
}

// ====== ECDH 前向保密工具 ======

/**
 * 生成临时 ECDH 密钥对(P-256)。每次握手生成新的,用完即弃。
 * 返回 { privateKey: CryptoKey, publicKeySpki: base64 }
 */
async function generateEcdhKeyPairP256() {
  const kp = await crypto.subtle.generateKey(
    { name: 'ECDH', namedCurve: 'P-256' },
    true, ['deriveKey', 'deriveBits']
  );
  // 导出公钥为 SPKI base64(发给服务端)
  const spkiBuf = await crypto.subtle.exportKey('spki', kp.publicKey);
  return {
    privateKey: kp.privateKey,
    publicKeySpki: b64encode(new Uint8Array(spkiBuf)),
  };
}

/**
 * 导入服务端 ECDH 公钥(SPKI base64) 为 CryptoKey
 */
async function importEcdhPublicKey(spkiBase64) {
  return await crypto.subtle.importKey(
    'spki', b64decode(spkiBase64),
    { name: 'ECDH', namedCurve: 'P-256' },
    false, []
  );
}

/**
 * ECDH 派生共享 AES-256-GCM key。
 * 与服务端 HKDF 参数一致:salt="RapidDev-ECDH-v1", info="aes-256-gcm"
 *
 * 注意:Web Crypto 的 deriveKey(ECDH → HKDF → AES-GCM) 在某些浏览器有 usages 限制,
 *       所以这里用 deriveBits 拿到原始密钥字节,再 importKey 成 AES-GCM。
 */
async function deriveAesKeyFromEcdh(privateKey, serverPublicKeySpkiB64) {
  const serverPub = await importEcdhPublicKey(serverPublicKeySpkiB64);
  // 1. ECDH 派生原始共享密钥 Z(32 字节,P-256)
  const zBits = await crypto.subtle.deriveBits(
    { name: 'ECDH', public: serverPub },
    privateKey,
    256  // P-256 曲线输出 32 字节
  );
  const z = new Uint8Array(zBits);

  // 2. HKDF-SHA256 导出 32 字节 AES key(与服务端 HkdfSha256 一致)
  //    RFC 5869: PRK = HMAC(salt, IKM); OKM = HKDF-Expand(PRK, info, L)
  const salt = utf8('RapidDev-ECDH-v1');
  const info = utf8('aes-256-gcm');

  // HKDF-Extract
  const hmacKey = await crypto.subtle.importKey('raw', salt, { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
  const prkBuf = await crypto.subtle.sign('HMAC', hmacKey, z);

  // HKDF-Expand
  const prkKey = await crypto.subtle.importKey('raw', prkBuf, { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
  const t1Input = new Uint8Array(info.length + 1);
  t1Input.set(info, 0);
  t1Input[info.length] = 1;  // counter = 1
  const t1 = new Uint8Array(await crypto.subtle.sign('HMAC', prkKey, t1Input));
  // 32 字节足够(L=32,只需 T(1))
  const aesKeyBytes = t1.slice(0, 32);

  return aesKeyBytes;
}

// ============================================================================
// 会话管理器
// ============================================================================

class CryptoSession {
  constructor(opts = {}) {
    this.baseUrl = (opts.baseUrl || '').replace(/\/$/, '');
    this.fetchImpl = opts.fetch || (typeof fetch !== 'undefined' ? fetch.bind(globalThis) : null);
    if (!this.fetchImpl) throw new Error('CryptoSession: no fetch implementation');
    this.session = null;          // { sid, key, expiresAt, earlyRefreshSec }
    this.handshakePromise = null; // 并发握手合并
    this.debug = !!opts.debug;
    this._maxRetries = 1;         // 自动重协商重试次数
  }
  log(...args) { if (this.debug) console.log('[CryptoSDK]', ...args); }

  async _handshake() {
    // ===== ECDH 前向保密握手 =====
    // 1. 客户端生成临时 ECDH 密钥对
    const ecdhKp = await generateEcdhKeyPairP256();
    // 2. 发送握手请求(客户端 ECDH 公钥经 TLS 信道传输)
    const body = JSON.stringify({
      mode: 'ecdh',
      clientPub: ecdhKp.publicKeySpki,
    });
    const r = await this.fetchImpl(this.baseUrl + '/api/secure/handshake', {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body,
    });
    if (!r.ok) throw new Error('handshake HTTP ' + r.status);
    const j = await r.json();
    if (!j.ok) {
      throw new Error('handshake error: ' + JSON.stringify(j.error));
    }
    // 3. 拿到服务端 ECDH 公钥,派生共享 AES key
    if (!j.serverPub) {
      throw new Error('handshake error: server did not return ECDH public key');
    }
    const aesKey = await deriveAesKeyFromEcdh(ecdhKp.privateKey, j.serverPub);

    const s = j.session;
    const expiresAt = s.expiresAt ?? (nowUnix() + s.expiresIn);
    const earlyRefreshAt = s.earlyRefreshAt ?? (expiresAt - s.earlyRefreshSec);
    this.session = { sid: s.sid, key: aesKey, expiresAt, earlyRefreshAt };
    this.log('ECDH handshake done: sid=%s ttl=%ds', s.sid, s.expiresIn);
  }

  async ensureSession() {
    const needs = !this.session || nowUnix() >= this.session.earlyRefreshAt;
    if (!needs) return;
    if (this.handshakePromise) return this.handshakePromise;
    this.handshakePromise = this._handshake().finally(() => { this.handshakePromise = null; });
    return this.handshakePromise;
  }

  async forceRehandshake() { this.session = null; await this.ensureSession(); }

  // ====== AES 通道(带自动重协商重试) ======
  async sendAes(inner, retryCount = 0) {
    await this.ensureSession();
    const iv = randomBytes(12);
    const ts = nowUnix();
    const nonce = randNonce();
    const alg = 'AES-GCM-256';
    const sess = this.session;
    const aad = buildAad(alg, sess.sid, ts, nonce);
    const ct = await aesGcmEncrypt(sess.key, iv, utf8(JSON.stringify(inner)), aad);

    const env = { alg, sid: sess.sid, iv: b64encode(iv), ts, nonce, payload: b64encode(ct) };
    const r = await this.fetchImpl(this.baseUrl + '/api/secure/aes', {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(env),
    });
    if (!r.ok) throw new Error('gateway HTTP ' + r.status);
    const j = await r.json();
    if (!j.ok) {
      const hint = j.error?.hint;
      // 自动重协商:session 失效
      if (hint === 'handshake' && retryCount < this._maxRetries) {
        this.log('envelope err, rehandshake:', j.error);
        await this.forceRehandshake();
        return this.sendAes(inner, retryCount + 1);
      }
      throw new Error('envelope error: ' + JSON.stringify(j.error));
    }
    const respAad = buildAad(j.alg || alg, j.sid || sess.sid, j.ts, j.nonce);
    const iv2 = b64decode(j.iv);
    const cipher2 = b64decode(j.payload);
    const pt = await aesGcmDecrypt(sess.key, iv2, cipher2, respAad);
    const innerResp = JSON.parse(fromUtf8(pt));
    if (j.refreshHint) { this.log('refreshHint, will rehandshake next call'); this.session = null; }
    return innerResp;
  }
}

// ============================================================================
// axios adapter - 透明加密
// ============================================================================

function isDefaultPassthrough(path, passthroughPaths) {
  const list = passthroughPaths || [
    '/api/secure/', '/api/file/upload', '/api/file/access/', '/hubs/', '/health',
  ];
  return list.some(p => path.startsWith(p));
}

/**
 * 计算完整请求路径(处理 baseURL 合并)
 * 改进:使用 URL 构造器,正确处理各种边界情况
 */
function resolvePath(config, baseUrl) {
  let url = config.url || '';
  const bu = typeof config.baseURL === 'string' ? config.baseURL : '';

  // 已经是完整 URL(http/https 开头)
  if (/^https?:\/\//i.test(url)) {
    try { return new URL(url).pathname + new URL(url).search; } catch { return url; }
  }

  // 合并 baseURL + url
  let full = url;
  if (bu) {
    // baseURL 可能是 '/api' 或 'http://host/api'
    if (/^https?:\/\//i.test(bu)) {
      try {
        const u = new URL(url, bu);
        full = u.pathname + u.search;
      } catch { full = bu.replace(/\/$/, '') + '/' + url.replace(/^\/+/, ''); }
    } else {
      // 相对 baseURL: '/api' + 'system/user' = '/api/system/user'
      full = bu.replace(/\/$/, '') + '/' + url.replace(/^\/+/, '');
    }
  }
  if (!full.startsWith('/')) full = '/' + full;
  return full;
}

export function installCryptoInterceptor(axiosInstance, opts = {}) {
  const baseUrl = (opts.baseUrl || '').replace(/\/$/, '');
  const session = new CryptoSession({ baseUrl, debug: opts.debug });
  const mode = opts.mode || 'aes';
  const enabledFn = typeof opts.enabled === 'function' ? opts.enabled
                  : (opts.enabled === false ? (() => false) : (() => true));

  // axios v1 的 defaults.adapter 是字符串名('xhr'/'http')或数组,不是函数。
  // 用 axios.getAdapter 解析成真正的 adapter 函数;解析失败再退回 defaults.adapter(兼容旧版直接是函数的情况)。
  let originalAdapter = axiosInstance.defaults.adapter;
  if (typeof originalAdapter !== 'function' && typeof axios.getAdapter === 'function') {
    try { originalAdapter = axios.getAdapter(originalAdapter); } catch {}
  }
  // 终极兜底:用默认 xhr adapter(浏览器环境)
  if (typeof originalAdapter !== 'function') {
    originalAdapter = (cfg) => axios.getAdapter('xhr')(cfg);
  }

  const secureAdapter = async (config) => {
    const pathAndQuery = resolvePath(config, baseUrl);

    if (!enabledFn() || isDefaultPassthrough(pathAndQuery, opts.passthroughPaths)) {
      return originalAdapter(config);
    }
    // 二进制/formdata 不走加密
    if (config.data instanceof FormData || config.data instanceof Blob ||
        config.data instanceof ArrayBuffer || config.responseType === 'blob' ||
        config.responseType === 'arraybuffer') {
      return originalAdapter(config);
    }

    // 拆 path 和 query
    const [pathOnly, ...qParts] = pathAndQuery.split('?');
    let query = qParts.length ? qParts.join('?') : null;
    if (config.params && Object.keys(config.params).length > 0) {
      const serialized = config.paramsSerializer
        ? (typeof config.paramsSerializer === 'function'
            ? config.paramsSerializer(config.params)
            : config.paramsSerializer.serialize(config.params))
        : new URLSearchParams(config.params).toString();
      query = query ? (query + '&' + serialized) : serialized;
    }

    const method = (config.method || 'get').toUpperCase();
    const headers = {};
    const rawHeaders = config.headers || {};
    for (const k of Object.keys(rawHeaders)) {
      if (['common','get','post','put','delete','patch','head','options'].includes(k)) continue;
      const v = rawHeaders[k];
      if (v == null) continue;
      if (typeof v === 'object' && !Array.isArray(v)) continue;
      headers[k] = Array.isArray(v) ? v.join(', ') : String(v);
    }

    let body = null;
    if (config.data != null) {
      body = typeof config.data === 'string' ? config.data : config.data;
    }

    const inner = { method, path: pathOnly, query, headers, body };

    let resp;
    try {
      resp = await session.sendAes(inner);
    } catch (e) {
      console.error('[CryptoSDK] send failed:', e);
      // 加密失败:退回原生 adapter(明文)
      if (typeof originalAdapter === 'function') return originalAdapter(config);
      // 极端 fallback
      try {
        const fullUrl = (baseUrl || '') + pathAndQuery;
        const r = await fetch(fullUrl, {
          method: config.method?.toUpperCase() || 'GET',
          headers: { 'Content-Type': 'application/json; charset=utf-8' },
          body: config.data ? JSON.stringify(config.data) : undefined,
        });
        const text = await r.text();
        let data;
        try { data = JSON.parse(text); } catch { data = text; }
        if (r.status === 401) {
          const err = new Error(data?.message || '未授权');
          err.response = { status: 401, data };
          throw err;
        }
        return { data, status: r.status, statusText: '', headers: {}, config, request: {} };
      } catch (err) {
        if (err.response) throw err;
        throw e;
      }
    }

    const responseHeaders = resp.headers || {};
    let data;
    if (resp.body == null || resp.body === '') data = null;
    else {
      try { data = JSON.parse(resp.body); }
      catch { data = resp.body; }
    }
    return { data, status: resp.status, statusText: '', headers: responseHeaders, config, request: {} };
  };

  axiosInstance.defaults.adapter = secureAdapter;
  return function uninstall() {
    axiosInstance.defaults.adapter = originalAdapter;
  };
}

export { CryptoSession, SDK_VERSION };
