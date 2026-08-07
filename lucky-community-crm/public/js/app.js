// 入口：启动流程、加载全部页签的独立文件、首次运行迁移
import * as api from './api.js';
import * as ui from './ui.js';
import { mergeSeeds, normalizeTabs, readLocalStorageMigrations } from './data.js';

async function boot() {
  const tr = await api.readTabs();
  const { tabs, activeId } = normalizeTabs(tr?.exists ? tr.data : null);
  const tabIds = tabs.map(t => t.id);
  let firstRun = !tr?.exists;

  // 加载每个页签独立的关键词/候选人文件
  const kwMap = {};
  const cdMap = {};
  await Promise.all(tabIds.map(async id => {
    const [kr, cr] = await Promise.all([api.readKeywords(id), api.readCandidates(id)]);
    if (!kr.exists || !cr.exists) firstRun = true;
    kwMap[id] = { keywords: kr.exists ? (kr.data.keywords || []) : [], collapsed: kr.exists ? !!kr.data.collapsed : false };
    cdMap[id] = { candidates: cr.exists ? (cr.data.candidates || []) : [] };
  }));

  // 首次运行：尝试从旧 localStorage 迁移到默认页签
  let migratedFrom = null;
  if (firstRun && tabIds.length > 0) {
    const mig = readLocalStorageMigrations();
    if (mig) {
      kwMap[tabIds[0]].keywords = mergeSeeds(mig.data).keywords;
      cdMap[tabIds[0]].candidates = mig.data.candidates;
      migratedFrom = mig.from;
    }
  }

  ui.loadState({ tabs, activeId, kwMap, cdMap });
  ui.initUI();
  ui.render();

  // 首次运行：把每个页签落盘
  if (firstRun) {
    for (const id of tabIds) {
      await api.writeKeywords(id, { schemaVersion: 4, keywords: kwMap[id].keywords, collapsed: kwMap[id].collapsed, updatedAt: Date.now() });
      await api.writeCandidates(id, { schemaVersion: 4, candidates: cdMap[id].candidates, updatedAt: Date.now() });
    }
    await ui.persistTabs();
  }

  if (migratedFrom) {
    alert(`已从旧浏览器数据（${migratedFrom}）迁移到默认页签。以后数据随项目 git 备份。`);
  }
}

boot().catch(err => {
  console.error(err);
  alert(`启动失败：${err.message || err}`);
});
