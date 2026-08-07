// 入口：启动流程、页签+关键词+候选人三文件加载、首次运行迁移
import * as api from './api.js';
import * as ui from './ui.js';
import { mergeSeeds, normalizeTabs, ensureTabKeys, readLocalStorageMigrations } from './data.js';

async function boot() {
  const [tr, kr, cr] = await Promise.all([api.readTabs(), api.readKeywords(), api.readCandidates()]);

  const { tabs, activeId } = normalizeTabs(tr?.exists ? tr.data : null);
  const tabIds = tabs.map(t => t.id);
  let firstRun = false;

  // 关键词 map：文件已是按页签结构则用；否则按默认页签起步
  let kwMap;
  if (kr.exists && kr.data && kr.data.tabs) {
    kwMap = ensureTabKeys(kr.data.tabs, tabIds, { keywords: [], collapsed: false });
  } else {
    firstRun = true;
    const base = (kr.exists && Array.isArray(kr.data?.keywords)) ? kr.data.keywords : [];
    kwMap = ensureTabKeys({ [tabIds[0]]: { keywords: base, collapsed: false } }, tabIds, { keywords: [], collapsed: false });
  }

  // 候选人 map
  let cdMap;
  if (cr.exists && cr.data && cr.data.tabs) {
    cdMap = ensureTabKeys(cr.data.tabs, tabIds, { candidates: [] });
  } else {
    firstRun = true;
    const base = (cr.exists && Array.isArray(cr.data?.candidates)) ? cr.data.candidates : [];
    cdMap = ensureTabKeys({ [tabIds[0]]: { candidates: base } }, tabIds, { candidates: [] });
  }

  // 首次运行：尝试从旧 localStorage 迁移到默认页签
  let migratedFrom = null;
  if (firstRun && !kr.exists && !cr.exists) {
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

  if (firstRun) {
    await ui.saveKeywords();
    await ui.saveCandidates();
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
