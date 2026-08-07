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

export function readData() { return request('/api/data', 'GET'); }
export function writeData(data) { return request('/api/data', 'POST', data); }
export function getConfig() { return request('/api/config', 'GET'); }
export function setConfig(cfg) { return request('/api/config', 'POST', cfg); }
