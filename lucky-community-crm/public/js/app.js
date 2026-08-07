// 入口：启动流程、数据加载、首次运行迁移
import * as api from './api.js';
import * as ui from './ui.js';
import { upgrade, mergeSeeds, readLocalStorageMigrations } from './data.js';
import { seed } from './seed.js';

async function boot() {
  const res = await api.readData();
  let state;
  let migratedFrom = null;

  if (res.exists) {
    state = upgrade(res.data);
  } else {
    // 首次运行：尝试从浏览器旧 localStorage 迁移
    const mig = readLocalStorageMigrations();
    if (mig) {
      state = mergeSeeds(mig.data);
      migratedFrom = mig.from;
    } else {
      state = seed();
    }
    await api.writeData(state); // 落到项目文件，作为初始数据
  }

  ui.setData(state);
  ui.initUI();
  ui.render();

  if (migratedFrom) {
    alert(`已从旧浏览器数据（${migratedFrom}）迁移到项目文件夹 data 文件。以后数据随项目 git 备份。`);
  }
}

boot().catch(err => {
  console.error(err);
  alert(`启动失败：${err.message || err}`);
});
