// 界面渲染 + 全部事件绑定。持有内存中的 data，通过 api 持久化到项目文件。
import * as api from './api.js';
import { upgrade, mergeSeeds } from './data.js';
import { seed, uid } from './seed.js';

const el = id => document.getElementById(id);
let data = null;

export function setData(d) { data = d; }
export function getData() { return data; }

const esc = s => String(s ?? '').replace(/[&<>"']/g, m => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[m]));
const rankClass = r => (r === 'A+' ? 'Ap' : r);
const xSearch = (q, mode) => `https://x.com/search?q=${encodeURIComponent(q)}&src=typed_query&f=${mode}`;

async function save() {
  data.updatedAt = Date.now();
  await api.writeData(data);
  el('saveState').textContent = '刚刚自动保存';
  setTimeout(() => { el('saveState').textContent = '已自动保存'; }, 900);
  render();
}

export function render() {
  renderCandidates();
  renderKeywords();
  renderStats();
}

function renderStats() {
  el('stKeywords').textContent = data.keywords.length;
  el('stGood').textContent = data.keywords.filter(k => k.status === 'good').length;
  el('stGreat').textContent = data.keywords.filter(k => k.status === 'great').length;
  el('stTesting').textContent = data.keywords.filter(k => k.status === 'testing').length;
  el('stBad').textContent = data.keywords.filter(k => k.status === 'bad').length;
  el('stCandidates').textContent = data.candidates.length;
  el('keywordCountTitle').textContent = `完整库：${data.keywords.length} 条`;
}

function renderCandidates() {
  const term = el('candidateSearch').value.toLowerCase();
  const rf = el('candidateRankFilter').value;
  const sf = el('candidateStateFilter').value;
  const arr = data.candidates.filter(c =>
    (!rf || c.rank === rf) &&
    (!sf || c.state === sf) &&
    JSON.stringify(c).toLowerCase().includes(term)
  );
  const list = el('candidateList');
  list.innerHTML = arr.length ? '' : '<div class="empty">还没有符合筛选条件的候选人。</div>';
  arr.forEach(c => {
    const node = document.createElement('article');
    node.className = 'candidate';
    node.innerHTML = `<div><h3>${esc(c.name || '未命名')}</h3><div class="meta">${esc(c.type)} · ${esc(c.state)} · ${esc(c.list)} · 竞品连接：${esc(c.risk)}</div><div class="proof">${esc(c.notes || '尚未记录证据')}</div><div class="actions" style="margin-top:9px">${c.url ? `<a class="btn primary small" href="${esc(c.url)}" target="_blank">打开</a>` : ''}<button class="btn small" data-edit-c="${c.id}">编辑</button><button class="btn small" data-next-c="${c.id}">推进状态</button><button class="btn danger small" data-del-c="${c.id}">删除</button></div></div><div class="rank ${rankClass(c.rank)}">${esc(c.rank)}</div>`;
    list.appendChild(node);
  });
  list.querySelectorAll('[data-edit-c]').forEach(b => b.onclick = () => editCandidate(b.dataset.editC));
  list.querySelectorAll('[data-next-c]').forEach(b => b.onclick = () => nextCandidate(b.dataset.nextC));
  list.querySelectorAll('[data-del-c]').forEach(b => b.onclick = () => deleteCandidate(b.dataset.delC));
}

function renderKeywords() {
  const term = el('keywordSearch').value.toLowerCase();
  const sf = el('keywordStatusFilter').value;
  const arr = data.keywords.filter(k => (!sf || k.status === sf) && `${k.q} ${k.group} ${k.why} ${k.note}`.toLowerCase().includes(term));
  const groups = {};
  arr.forEach(k => (groups[k.group] ??= []).push(k));
  const box = el('keywordList');
  box.innerHTML = '';
  Object.entries(groups).forEach(([g, items]) => {
    const section = document.createElement('section');
    section.className = 'group';
    section.innerHTML = `<div class="group-title">${esc(g)}（${items.length}）</div>`;
    if (!data.collapsed) items.forEach(k => {
      const row = document.createElement('div');
      row.className = 'keyword';
      row.innerHTML = `<div><div class="keyword-name">${esc(k.q)}</div><div class="keyword-desc">${esc(k.why)}</div><div class="keyword-note">已打开 ${k.opens || 0} 次${k.note ? ` · 备注：${esc(k.note)}` : ''}</div></div><div class="actions"><a class="btn primary small" data-open-k="${k.id}" href="${xSearch(k.q, 'live')}" target="_blank">最新</a><a class="btn small" data-open-k="${k.id}" href="${xSearch(k.q, 'top')}" target="_blank">热门</a><span class="pill ${k.status}">${k.status === 'good' ? '有效' : k.status === 'great' ? '很棒' : k.status === 'bad' ? '无效' : '待验证'}</span><button class="btn small" data-cycle-k="${k.id}">切换状态</button><button class="btn small" data-edit-k="${k.id}">编辑</button></div>`;
      section.appendChild(row);
    });
    box.appendChild(section);
  });
  box.querySelectorAll('[data-open-k]').forEach(a => a.onclick = () => {
    const k = data.keywords.find(x => x.id === a.dataset.openK);
    if (k) { k.opens++; api.writeData(data); renderStats(); }
  });
  box.querySelectorAll('[data-cycle-k]').forEach(b => b.onclick = () => cycleKeyword(b.dataset.cycleK));
  box.querySelectorAll('[data-edit-k]').forEach(b => b.onclick = () => openKeywordModal(b.dataset.editK));
}

// ---------- 候选人 ----------
function clearCandidate() {
  el('candidateId').value = '';
  el('cName').value = '';
  el('cUrl').value = '';
  el('cRank').value = 'A+';
  el('cType').value = '主播/VTuber';
  el('cState').value = '待互动';
  el('cList').value = '桌面陪伴玩家';
  el('cRisk').value = '无明显连接';
  el('cNotes').value = '';
}
function editCandidate(id) {
  const c = data.candidates.find(x => x.id === id);
  if (!c) return;
  el('candidateId').value = c.id;
  el('cName').value = c.name;
  el('cUrl').value = c.url;
  el('cRank').value = c.rank;
  el('cType').value = c.type;
  el('cState').value = c.state;
  el('cList').value = c.list;
  el('cRisk').value = c.risk;
  el('cNotes').value = c.notes;
  window.scrollTo({ top: 0, behavior: 'smooth' });
}
function nextCandidate(id) {
  const c = data.candidates.find(x => x.id === id);
  if (!c) return;
  const states = ['待互动', '已回复', '已回应', '待邀请', '已邀请', '已接受', '仅观察'];
  c.state = states[(states.indexOf(c.state) + 1) % states.length];
  save();
}
function deleteCandidate(id) {
  if (confirm('删除这个候选人？')) {
    data.candidates = data.candidates.filter(x => x.id !== id);
    save();
  }
}

// ---------- 关键词 ----------
function cycleKeyword(id) {
  const k = data.keywords.find(x => x.id === id);
  if (!k) return;
  k.status = k.status === 'testing' ? 'good' : k.status === 'good' ? 'great' : k.status === 'great' ? 'bad' : 'testing';
  save();
}
function openKeywordModal(id = '') {
  const k = id ? data.keywords.find(x => x.id === id) : null;
  el('kId').value = k?.id || '';
  el('kQuery').value = k?.q || '';
  el('kGroup').value = k?.group || '自定义实验';
  el('kWhy').value = k?.why || '';
  el('kNote').value = k?.note || '';
  el('kStatus').value = k?.status || 'testing';
  el('kOpens').value = k?.opens || 0;
  el('keywordModalTitle').textContent = k ? '编辑关键词' : '添加关键词';
  el('deleteKeywordBtn').style.display = k ? 'inline-flex' : 'none';
  el('keywordModal').classList.add('show');
}

// ---------- 下载 ----------
function download(name, text, type) {
  const a = document.createElement('a');
  a.href = URL.createObjectURL(new Blob([text], { type }));
  a.download = name;
  a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}

// ---------- 事件绑定 + 配置面板 ----------
export function initUI() {
  // 候选人
  el('saveCandidateBtn').onclick = () => {
    if (!el('cName').value.trim() && !el('cUrl').value.trim()) return alert('至少填写名称或链接。');
    const id = el('candidateId').value || uid();
    const obj = {
      id,
      name: el('cName').value.trim(),
      url: el('cUrl').value.trim(),
      rank: el('cRank').value,
      type: el('cType').value,
      state: el('cState').value,
      list: el('cList').value,
      risk: el('cRisk').value,
      notes: el('cNotes').value.trim(),
    };
    const i = data.candidates.findIndex(x => x.id === id);
    if (i >= 0) data.candidates[i] = obj;
    else data.candidates.unshift(obj);
    clearCandidate();
    save();
  };
  el('clearCandidateBtn').onclick = clearCandidate;

  // 关键词
  el('addKeywordBtn').onclick = () => openKeywordModal();
  el('saveKeywordBtn').onclick = () => {
    if (!el('kQuery').value.trim()) return alert('请填写关键词。');
    const id = el('kId').value || uid();
    const obj = {
      id,
      q: el('kQuery').value.trim(),
      group: el('kGroup').value.trim() || '自定义实验',
      why: el('kWhy').value.trim(),
      note: el('kNote').value.trim(),
      status: el('kStatus').value,
      opens: Number(el('kOpens').value || 0),
    };
    const i = data.keywords.findIndex(x => x.id === id);
    if (i >= 0) data.keywords[i] = obj;
    else data.keywords.unshift(obj);
    el('keywordModal').classList.remove('show');
    save();
  };
  el('deleteKeywordBtn').onclick = () => {
    if (el('kId').value && confirm('删除这个关键词？')) {
      data.keywords = data.keywords.filter(x => x.id !== el('kId').value);
      el('keywordModal').classList.remove('show');
      save();
    }
  };
  el('collapseBtn').onclick = () => {
    data.collapsed = !data.collapsed;
    el('collapseBtn').textContent = data.collapsed ? '展开分组' : '折叠分组';
    save();
  };

  // 模态框关闭
  document.querySelectorAll('[data-close]').forEach(b => b.onclick = () => el(b.dataset.close).classList.remove('show'));
  el('keywordModal').onclick = e => { if (e.target === el('keywordModal')) el('keywordModal').classList.remove('show'); };

  // 筛选
  [el('candidateSearch'), el('candidateRankFilter'), el('candidateStateFilter'), el('keywordSearch'), el('keywordStatusFilter')]
    .forEach(f => f.addEventListener(f.tagName === 'INPUT' ? 'input' : 'change', render));

  // 导出 / 导入 / 重置
  el('exportJsonBtn').onclick = () => download('lucky-dog-x-workbench-v3.json', JSON.stringify(data, null, 2), 'application/json');
  el('exportCsvBtn').onclick = () => {
    const rows = [['name', 'url', 'rank', 'type', 'state', 'list', 'risk', 'notes'],
      ...data.candidates.map(c => [c.name, c.url, c.rank, c.type, c.state, c.list, c.risk, c.notes])];
    const csv = rows.map(r => r.map(v => `"${String(v ?? '').replaceAll('"', '""')}"`).join(',')).join('\n');
    download('lucky-dog-candidates-v3.csv', '﻿' + csv, 'text/csv;charset=utf-8');
  };
  el('importFile').onchange = async e => {
    const f = e.target.files[0];
    if (!f) return;
    try {
      const incoming = mergeSeeds(upgrade(JSON.parse(await f.text())));
      data = incoming;
      save();
      alert('导入成功，并补齐了默认关键词。');
    } catch (err) {
      alert('无法识别这个 JSON 文件。');
    }
    e.target.value = '';
  };
  el('resetBtn').onclick = () => {
    if (confirm('确定恢复默认关键词并清空候选人？此操作不可撤销。')) {
      data = seed();
      save();
    }
  };

  // 保存位置设置
  api.getConfig().then(cfg => {
    el('cfgDataFile').value = cfg.dataFile;
  }).catch(() => {});
  el('saveConfigBtn').onclick = async () => {
    const p = el('cfgDataFile').value.trim();
    if (!p) return alert('请填写数据文件路径。');
    const cfg = await api.setConfig({ dataFile: p });
    el('cfgStatus').textContent = '已保存（重启后生效）';
    setTimeout(() => { el('cfgStatus').textContent = ''; }, 2500);
    el('cfgDataFile').value = cfg.dataFile;
  };

  el('collapseBtn').textContent = data.collapsed ? '展开分组' : '折叠分组';
}
