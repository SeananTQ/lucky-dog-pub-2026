// 入口：启动流程、双文件数据加载、首次运行迁移
import * as api from './api.js';
import * as ui from './ui.js';
import { upgrade, mergeSeeds, readLocalStorageMigrations, CURRENT_SCHEMA_VERSION } from './data.js';
import { seed } from './seed.js';

async function boot() {
  const [kr, cr] = await Promise.all([api.readKeywords(), api.readCandidates()]);
  let state;
  let migratedFrom = null;
  let firstRun = false;

  if (!kr.exists && !cr.exists) {
    // 首次运行：尝试从旧 localStorage 迁移，否则用种子数据
    firstRun = true;
    const mig = readLocalStorageMigrations();
    if (mig) {
      state = mergeSeeds(mig.data);
      migratedFrom = mig.from;
    } else {
      state = seed();
    }
  } else {
    // 已存在数据：分别加载两个文件，缺失关键词则补种子
    const kw = kr.exists ? upgrade(kr.data) : mergeSeeds(seed());
    const cd = cr.exists ? upgrade(cr.data) : null;
    state = {
      schemaVersion: CURRENT_SCHEMA_VERSION,
      keywords: kw.keywords,
      collapsed: kw.collapsed,
      candidates: cd ? cd.candidates : [],
      updatedAt: Date.now(),
    };
  }

  ui.setData(state);
  ui.initUI();
  ui.render();

  if (firstRun) {
    await ui.saveKeywords();
    await ui.saveCandidates();
  }

  if (migratedFrom) {
    alert(`已从旧浏览器数据（${migratedFrom}）迁移到项目文件夹数据文件。以后数据随项目 git 备份。`);
  }
}

boot().catch(err => {
  console.error(err);
  alert(`启动失败：${err.message || err}`);
});
