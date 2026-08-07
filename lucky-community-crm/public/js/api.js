// 与本地文件管家服务的通信封装

async function request(url, method, body) {
  try {
    const res = await fetch(url, {
      method,
      headers: body !== undefined ? { 'Content-Type': 'application/json' } : undefined,
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok) throw new Error(data.error || `请求失败 (${res.status})`);
    return data;
  } catch (err) {
    alert(`网络/服务错误：${err.message || err}`);
    throw err;
  }
}

export function readKeywords() { return request('/api/keywords', 'GET'); }
export function writeKeywords(data) { return request('/api/keywords', 'POST', data); }
export function readCandidates() { return request('/api/candidates', 'GET'); }
export function writeCandidates(data) { return request('/api/candidates', 'POST', data); }
export function readTabs() { return request('/api/tabs', 'GET'); }
export function writeTabs(data) { return request('/api/tabs', 'POST', data); }
export function getConfig() { return request('/api/config', 'GET'); }
export function setConfig(cfg) { return request('/api/config', 'POST', cfg); }
