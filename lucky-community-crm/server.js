// Lucky Dog Rise X 关系管理工作台 - 本地文件管家服务
// 零依赖，仅用 Node 内置模块。数据落项目文件夹，被 git 跟踪备份。
// 只监听本机 127.0.0.1，不开放局域网。
'use strict';

const http = require('http');
const fs = require('fs');
const path = require('path');
const { exec } = require('child_process');

const ROOT = __dirname; // server.js 所在目录，作为所有相对路径基准（保证整个文件夹可移动）
const PUBLIC_DIR = path.join(ROOT, 'public');
const CONFIG_FILE = path.join(ROOT, 'server.config.json');
const DEFAULT_CONFIG = { keywordsDir: 'data/keywords', candidatesDir: 'data/candidates', tabsFile: 'data/tabs.json', preferredPort: 3020 };

// 把用户填的保存位置解析成绝对路径：相对路径以项目根为基准，绝对路径直接用
function resolveDataPath(...segments) {
  const joined = path.join(...segments);
  return path.isAbsolute(joined) ? joined : path.resolve(ROOT, joined);
}

// ---------- 配置 ----------
function loadConfig() {
  let raw = {};
  try { raw = JSON.parse(fs.readFileSync(CONFIG_FILE, 'utf8')); } catch {}

  const cfg = {
    keywordsDir: raw.keywordsDir || DEFAULT_CONFIG.keywordsDir,
    candidatesDir: raw.candidatesDir || DEFAULT_CONFIG.candidatesDir,
    tabsFile: raw.tabsFile || DEFAULT_CONFIG.tabsFile,
    preferredPort: Number(raw.preferredPort) || DEFAULT_CONFIG.preferredPort,
  };

  migrateToPerTab(cfg, raw);

  saveConfig(cfg);
  return cfg;
}

// 页签独立文件迁移：把旧格式（合并 database.json / 扁平 / 单文件 map）收敛成"每页签一个文件"
function migrateToPerTab(cfg, raw) {
  const tabsFile = resolveDataPath(cfg.tabsFile);

  // 1. tabs.json 确保存在
  let tabsData = readFile(tabsFile);
  if (!tabsData || !Array.isArray(tabsData.tabs) || tabsData.tabs.length === 0) {
    tabsData = {
      schemaVersion: 4,
      tabs: [
        { id: 'steam', name: 'Steam桌宠用户' },
        { id: 'shiba', name: '柴犬用户' },
      ],
      activeId: 'steam',
      updatedAt: Date.now(),
    };
    writeFileAtomic(tabsFile, tabsData);
    console.log('已创建默认页签（Steam桌宠用户 / 柴犬用户）。');
  }
  const tabIds = tabsData.tabs.map(t => t.id);

  // 2. 旧合并 database.json 拆分（若存在且尚未处理）
  const legacyFile = raw.dataFile ? resolveDataPath(raw.dataFile) : resolveDataPath('data/database.json');
  if (fs.existsSync(legacyFile)) {
    const legacy = readFile(legacyFile);
    if (legacy) {
      writeFileAtomic(resolveDataPath(cfg.keywordsDir, 'steam.json'), { schemaVersion: 4, keywords: legacy.keywords || [], collapsed: !!legacy.collapsed, updatedAt: legacy.updatedAt || Date.now() });
      writeFileAtomic(resolveDataPath(cfg.candidatesDir, 'steam.json'), { schemaVersion: 4, candidates: legacy.candidates || [], updatedAt: legacy.updatedAt || Date.now() });
      fs.renameSync(legacyFile, legacyFile + '.legacy.bak');
      console.log('已把旧合并数据拆分为 关键词/候选人 独立文件。');
    }
  }

  // 3. 旧单文件（扁平或 map）迁移到按页签文件
  migrateOldTypeFile('keywords', cfg, raw, tabIds);
  migrateOldTypeFile('candidates', cfg, raw, tabIds);

  // 4. 确保每个页签都有独立文件
  tabIds.forEach(id => {
    const kp = resolveDataPath(cfg.keywordsDir, id + '.json');
    if (!fs.existsSync(kp)) writeFileAtomic(kp, { schemaVersion: 4, keywords: [], collapsed: false, updatedAt: Date.now() });
    const cp = resolveDataPath(cfg.candidatesDir, id + '.json');
    if (!fs.existsSync(cp)) writeFileAtomic(cp, { schemaVersion: 4, candidates: [], updatedAt: Date.now() });
  });
}

// 把旧的关键词/候选人单文件（扁平或 map）拆成按页签的独立文件
function migrateOldTypeFile(type, cfg, raw, tabIds) {
  const isKw = type === 'keywords';
  const oldKey = isKw ? 'keywordsFile' : 'candidatesFile';
  const oldPath = raw[oldKey] ? resolveDataPath(raw[oldKey]) : resolveDataPath(isKw ? 'data/keywords.json' : 'data/candidates.json');
  if (!fs.existsSync(oldPath)) return;
  const data = readFile(oldPath);
  if (!data) return;
  const dir = isKw ? cfg.keywordsDir : cfg.candidatesDir;
  const first = resolveDataPath(dir, tabIds[0] + '.json');
  if (fs.existsSync(first)) return; // 已迁移过，跳过
  let perTab;
  if (data.tabs) {
    perTab = data.tabs;
  } else {
    perTab = { [tabIds[0]]: isKw ? { keywords: data.keywords || [], collapsed: !!data.collapsed } : { candidates: data.candidates || [] } };
  }
  tabIds.forEach(id => {
    const t = perTab[id] || {};
    const obj = isKw
      ? { schemaVersion: 4, keywords: t.keywords || [], collapsed: !!t.collapsed, updatedAt: data.updatedAt || Date.now() }
      : { schemaVersion: 4, candidates: t.candidates || [], updatedAt: data.updatedAt || Date.now() };
    writeFileAtomic(resolveDataPath(dir, id + '.json'), obj);
  });
  fs.renameSync(oldPath, oldPath + '.map.bak');
  console.log(`已将${isKw ? '关键词' : '候选人'}数据拆分为按页签独立文件。`);
}
function saveConfig(cfg) {
  fs.writeFileSync(CONFIG_FILE, JSON.stringify(cfg, null, 2), 'utf8');
}
function configView(cfg) {
  // 暴露给前端的配置视图：相对路径保留原样显示，绝对路径原样显示
  return { keywordsDir: cfg.keywordsDir, candidatesDir: cfg.candidatesDir, tabsFile: cfg.tabsFile, preferredPort: cfg.preferredPort, root: ROOT };
}

const config = loadConfig();

// ---------- 数据读写（原子写，防损坏） ----------
function readFile(abs) {
  if (!fs.existsSync(abs)) return null;
  return JSON.parse(fs.readFileSync(abs, 'utf8'));
}
function writeFileAtomic(abs, obj) {
  const dir = path.dirname(abs);
  if (!fs.existsSync(dir)) fs.mkdirSync(dir, { recursive: true });
  const tmp = abs + '.tmp';
  fs.writeFileSync(tmp, JSON.stringify(obj, null, 2), 'utf8');
  fs.renameSync(tmp, abs); // 原子替换，避免半写损坏
}
const kwPath = (id) => resolveDataPath(config.keywordsDir, id + '.json');
const cdPath = (id) => resolveDataPath(config.candidatesDir, id + '.json');
const readKeywords = (id) => readFile(kwPath(id));
const writeKeywords = (id, d) => writeFileAtomic(kwPath(id), d);
const deleteKeywords = (id) => { if (fs.existsSync(kwPath(id))) fs.unlinkSync(kwPath(id)); };
const readCandidates = (id) => readFile(cdPath(id));
const writeCandidates = (id, d) => writeFileAtomic(cdPath(id), d);
const deleteCandidates = (id) => { if (fs.existsSync(cdPath(id))) fs.unlinkSync(cdPath(id)); };
const readTabs = () => readFile(resolveDataPath(config.tabsFile));
const writeTabs = (d) => writeFileAtomic(resolveDataPath(config.tabsFile), d);

// ---------- 静态文件 / MIME ----------
const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.ico': 'image/x-icon',
};
function send(res, code, body, type) {
  res.writeHead(code, { 'Content-Type': type || 'text/plain; charset=utf-8' });
  res.end(body);
}
function sendJson(res, code, obj) {
  send(res, code, JSON.stringify(obj), 'application/json; charset=utf-8');
}
function readBody(req) {
  return new Promise((resolve) => {
    let body = '';
    req.on('data', (c) => (body += c));
    req.on('end', () => {
      try { resolve(JSON.parse(body || 'null')); } catch { resolve(null); }
    });
  });
}

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, 'http://localhost');
  const p = url.pathname;

  try {
    // ---------- JSON 接口 ----------
    const dataReply = (d) => (d ? { exists: true, data: d } : { exists: false });

    const kwMatch = p.match(/^\/api\/keywords\/([A-Za-z0-9_-]+)$/);
    if (kwMatch) {
      const id = kwMatch[1];
      if (req.method === 'GET') return sendJson(res, 200, dataReply(readKeywords(id)));
      if (req.method === 'POST') {
        const d = await readBody(req);
        if (!d || typeof d !== 'object') return sendJson(res, 400, { error: '无效数据' });
        writeKeywords(id, d);
        return sendJson(res, 200, { ok: true });
      }
      if (req.method === 'DELETE') { deleteKeywords(id); return sendJson(res, 200, { ok: true }); }
    }
    const cdMatch = p.match(/^\/api\/candidates\/([A-Za-z0-9_-]+)$/);
    if (cdMatch) {
      const id = cdMatch[1];
      if (req.method === 'GET') return sendJson(res, 200, dataReply(readCandidates(id)));
      if (req.method === 'POST') {
        const d = await readBody(req);
        if (!d || typeof d !== 'object') return sendJson(res, 400, { error: '无效数据' });
        writeCandidates(id, d);
        return sendJson(res, 200, { ok: true });
      }
      if (req.method === 'DELETE') { deleteCandidates(id); return sendJson(res, 200, { ok: true }); }
    }
    if (p === '/api/tabs' && req.method === 'GET') return sendJson(res, 200, dataReply(readTabs()));
    if (p === '/api/tabs' && req.method === 'POST') {
      const d = await readBody(req);
      if (!d || typeof d !== 'object') return sendJson(res, 400, { error: '无效数据' });
      writeTabs(d);
      return sendJson(res, 200, { ok: true });
    }
    if (p === '/api/config' && req.method === 'GET') {
      return sendJson(res, 200, configView(config));
    }
    if (p === '/api/config' && req.method === 'POST') {
      const body = await readBody(req);
      if (body.keywordsDir !== undefined && typeof body.keywordsDir === 'string' && body.keywordsDir.trim()) {
        config.keywordsDir = body.keywordsDir.trim();
      }
      if (body.candidatesDir !== undefined && typeof body.candidatesDir === 'string' && body.candidatesDir.trim()) {
        config.candidatesDir = body.candidatesDir.trim();
      }
      if (body.tabsFile !== undefined && typeof body.tabsFile === 'string' && body.tabsFile.trim()) {
        config.tabsFile = body.tabsFile.trim();
      }
      if (body.preferredPort !== undefined) {
        const port = Number(body.preferredPort);
        if (Number.isInteger(port) && port > 0 && port < 65536) config.preferredPort = port;
      }
      saveConfig(config);
      return sendJson(res, 200, configView(config));
    }

    // ---------- 静态资源 ----------
    let rel = p === '/' ? '/index.html' : p;
    // 只允许 public 目录下，防目录穿越
    const file = path.normalize(path.join(PUBLIC_DIR, rel));
    if (!file.startsWith(PUBLIC_DIR)) return send(res, 403, 'Forbidden');
    if (!fs.existsSync(file) || !fs.statSync(file).isFile()) return send(res, 404, 'Not Found');
    send(res, 200, fs.readFileSync(file), MIME[path.extname(file)] || 'application/octet-stream');
  } catch (err) {
    sendJson(res, 500, { error: String(err && err.message || err) });
  }
});

// ---------- 端口退避 + 自动开浏览器 ----------
function listenWithFallback(port, maxAttempts) {
  return new Promise((resolve, reject) => {
    const attempt = (n) => {
      if (n > maxAttempts) return reject(new Error(`端口 ${port}~${port + maxAttempts} 均被占用，请修改 server.config.json 的 preferredPort 后重试。`));
      const candidate = port + n;
      let failed = false; // 防止重试时失败的端口残留 'listening' 回调抢先 resolve
      const srv = server.listen(candidate, '127.0.0.1', () => {
        if (!failed) resolve({ srv, port: candidate });
      });
      srv.on('error', (e) => {
        if (e.code === 'EADDRINUSE') { failed = true; srv.close(() => attempt(n + 1)); }
        else reject(e);
      });
    };
    attempt(0);
  });
}

(async () => {
  const { port } = await listenWithFallback(config.preferredPort, 5);
  const url = `http://localhost:${port}`;
  console.log('==============================================');
  console.log(' Lucky Dog X 关系管理工作台');
  console.log(` 服务地址: ${url}`);
  console.log(` 关键词目录: ${config.keywordsDir}`);
  console.log(` 候选人目录: ${config.candidatesDir}`);
  console.log(` 页签文件: ${config.tabsFile}`);
  console.log(' 按 Ctrl+C 停止服务');
  console.log('==============================================');
  // Windows 自动打开浏览器到实际端口
  exec(`start "" "${url}"`, { cwd: ROOT }, () => {});
})().catch((err) => {
  console.error(err.message || err);
  process.exit(1);
});
