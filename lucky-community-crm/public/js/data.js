// 数据升级 / 归一化 / 旧数据迁移
// upgrade() 是唯一的"旧格式 → 新格式"入口，未来升版本时在此追加步骤即可。
import { seed, seedKeywords, uid } from './seed.js';

export const CURRENT_SCHEMA_VERSION = 2;

// 旧的浏览器 localStorage key
const V3_KEY = 'lucky_dog_x_workbench_v3';
const V1_KEY = 'ldr_x_research_v1';

// 把任意旧格式（V1 / V3 导出 / 旧 localStorage）升级到当前 schema
export function upgrade(raw) {
  if (!raw || typeof raw !== 'object') return seed();
  return {
    schemaVersion: CURRENT_SCHEMA_VERSION,
    keywords: (raw.keywords || []).map(k => ({
      id: k.id || uid(),
      group: k.group || '未分类',
      q: k.q || k.query || '',
      why: k.why || '',
      status: k.status || 'testing',
      note: k.note || '',
      opens: Number(k.opens || 0),
    })),
    candidates: (raw.candidates || []).map(c => ({
      id: c.id || uid(),
      name: c.name || '',
      url: c.url || '',
      rank: c.rank || c.grade || 'A',
      type: c.type || '普通玩家',
      state: c.state || '待互动',
      list: c.list || '未分类',
      risk: c.risk || '无明显连接',
      notes: c.notes || '',
    })),
    collapsed: !!raw.collapsed,
    updatedAt: raw.updatedAt || Date.now(),
  };
}

// 把缺失的种子关键词合并进已有数据（不改变已存在的关键词）
export function mergeSeeds(data) {
  const keys = new Set(data.keywords.map(k => (k.q || '').trim().toLowerCase()));
  const merged = {
    ...data,
    keywords: data.keywords.concat(
      seedKeywords
        .filter(x => !keys.has(x.q.trim().toLowerCase()))
        .map(x => ({ ...x, id: uid(), status: 'testing', note: '', opens: 0 }))
    ),
  };
  return merged;
}

// 从浏览器 localStorage 读取旧数据；返回 { data, from }，没有则返回 null
export function readLocalStorageMigrations() {
  try {
    const v3 = JSON.parse(localStorage.getItem(V3_KEY) || 'null');
    if (v3?.keywords && v3?.candidates) return { data: upgrade(v3), from: 'V3' };
  } catch (e) {}
  try {
    const v1 = JSON.parse(localStorage.getItem(V1_KEY) || 'null');
    if (v1?.keywords && v1?.candidates) return { data: upgrade(v1), from: 'V1' };
  } catch (e) {}
  return null;
}
