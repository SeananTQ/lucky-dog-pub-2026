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
const DEFAULT_CONFIG = { keywordsFile: 'data/keywords.json', candidatesFile: 'data/candidates.json', preferredPort: 3020 };

// 把用户填的保存位置解析成绝对路径：相对路径以项目根为基准，绝对路径直接用
function resolveDataPath(p) {
  return path.isAbsolute(p) ? p : path.resolve(ROOT, p);
}

// ---------- 配置 ----------
function loadConfig() {
  let raw = {};
  try { raw = JSON.parse(fs.readFileSync(CONFIG_FILE, 'utf8')); } catch {}

  const cfg = {
    keywordsFile: raw.keywordsFile || DEFAULT_CONFIG.keywordsFile,
    candidatesFile: raw.candidatesFile || DEFAULT_CONFIG.candidatesFile,
    preferredPort: Number(raw.preferredPort) || DEFAULT_CONFIG.preferredPort,
  };

  // 旧版单文件迁移：把合并的 database.json 拆成关键词/候选人两个文件
  const legacyFile = raw.dataFile ? resolveDataPath(raw.dataFile) : resolveDataPath('data/database.json');
  const hasLegacy = fs.existsSync(legacyFile);
  const splitReady = fs.existsSync(resolveDataPath(cfg.keywordsFile)) && fs.existsSync(resolveDataPath(cfg.candidatesFile));
  if (hasLegacy && !splitReady) {
    try {
      const d = JSON.parse(fs.readFileSync(legacyFile, 'utf8'));
      writeFileAtomic(resolveDataPath(cfg.keywordsFile), { schemaVersion: 3, keywords: d.keywords || [], collapsed: !!d.collapsed, updatedAt: d.updatedAt || Date.now() });
      writeFileAtomic(resolveDataPath(cfg.candidatesFile), { schemaVersion: 3, candidates: d.candidates || [], updatedAt: d.updatedAt || Date.now() });
      fs.renameSync(legacyFile, legacyFile + '.legacy.bak');
      console.log('已把旧合并数据拆分为 关键词/候选人 两个文件。');
    } catch (e) {
      console.error('迁移旧数据失败：', e.message);
    }
  }

  saveConfig(cfg);
  return cfg;
}
function saveConfig(cfg) {
  fs.writeFileSync(CONFIG_FILE, JSON.stringify(cfg, null, 2), 'utf8');
}
function configView(cfg) {
  // 暴露给前端的配置视图：相对路径保留原样显示，绝对路径原样显示
  return { keywordsFile: cfg.keywordsFile, candidatesFile: cfg.candidatesFile, preferredPort: cfg.preferredPort, root: ROOT };
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
const readKeywords = () => readFile(resolveDataPath(config.keywordsFile));
const readCandidates = () => readFile(resolveDataPath(config.candidatesFile));
const writeKeywords = (d) => writeFileAtomic(resolveDataPath(config.keywordsFile), d);
const writeCandidates = (d) => writeFileAtomic(resolveDataPath(config.candidatesFile), d);

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

    if (p === '/api/keywords' && req.method === 'GET') return sendJson(res, 200, dataReply(readKeywords()));
    if (p === '/api/keywords' && req.method === 'POST') {
      const d = await readBody(req);
      if (!d || typeof d !== 'object') return sendJson(res, 400, { error: '无效数据' });
      writeKeywords(d);
      return sendJson(res, 200, { ok: true });
    }
    if (p === '/api/candidates' && req.method === 'GET') return sendJson(res, 200, dataReply(readCandidates()));
    if (p === '/api/candidates' && req.method === 'POST') {
      const d = await readBody(req);
      if (!d || typeof d !== 'object') return sendJson(res, 400, { error: '无效数据' });
      writeCandidates(d);
      return sendJson(res, 200, { ok: true });
    }
    if (p === '/api/config' && req.method === 'GET') {
      return sendJson(res, 200, configView(config));
    }
    if (p === '/api/config' && req.method === 'POST') {
      const body = await readBody(req);
      if (body.keywordsFile !== undefined && typeof body.keywordsFile === 'string' && body.keywordsFile.trim()) {
        config.keywordsFile = body.keywordsFile.trim();
      }
      if (body.candidatesFile !== undefined && typeof body.candidatesFile === 'string' && body.candidatesFile.trim()) {
        config.candidatesFile = body.candidatesFile.trim();
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
  console.log(` 关键词文件: ${config.keywordsFile}`);
  console.log(` 候选人文件: ${config.candidatesFile}`);
  console.log(' 按 Ctrl+C 停止服务');
  console.log('==============================================');
  // Windows 自动打开浏览器到实际端口
  exec(`start "" "${url}"`, { cwd: ROOT }, () => {});
})().catch((err) => {
  console.error(err.message || err);
  process.exit(1);
});
