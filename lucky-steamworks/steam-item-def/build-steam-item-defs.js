#!/usr/bin/env node
"use strict";

const fs = require("node:fs");
const path = require("node:path");

const TOOL_ROOT = __dirname;
const PROJECT_ROOT = path.resolve(TOOL_ROOT, "..", "..");
const DEFAULT_ITEM_DEF_INPUT = path.join(
    PROJECT_ROOT,
    "lucky-dog-rise",
    "Data",
    "Json",
    "tbsteamitemdef.json",
);
const DEFAULT_LINK_TREE_INPUT = path.join(
    PROJECT_ROOT,
    "lucky-dog-rise",
    "Data",
    "Json",
    "tblinktree.json",
);
const DEFAULT_ITEM_INPUT = path.join(
    PROJECT_ROOT,
    "lucky-dog-rise",
    "Data",
    "Json",
    "tbitem.json",
);
const DEFAULT_BLIND_BOX_INPUT = path.join(
    PROJECT_ROOT,
    "lucky-dog-rise",
    "Data",
    "Json",
    "tbblindbox.json",
);
const DEFAULT_BLIND_BOX_SCHEDULE_INPUT = path.join(
    PROJECT_ROOT,
    "lucky-dog-rise",
    "Data",
    "Json",
    "tbblindboxschedule.json",
);
const DEFAULT_BLIND_BOX_RARITY_RATE_INPUT = path.join(
    PROJECT_ROOT,
    "lucky-dog-rise",
    "Data",
    "Json",
    "tbblindboxrarityrate.json",
);
const DEFAULT_GAME_DEVELOP_CONFIG_INPUT = path.join(
    PROJECT_ROOT,
    "lucky-dog-rise",
    "Data",
    "Json",
    "tbgamedevelopconfig.json",
);
const DEFAULT_ITEM_DEF_ID_RANGE_INPUT = path.join(
    PROJECT_ROOT,
    "lucky-dog-rise",
    "Data",
    "Json",
    "tbsteamitemdefidrange.json",
);
const DEFAULT_OUTPUT_ROOT = path.join(TOOL_ROOT, "generated");
const AUTO_BUNDLE_MARKER = "@AUTO";
const AUTO_GENERATOR_WEIGHT_SCALE = 1000000;

const CHANNELS = Object.freeze({
    playtest: { appId: 4972240, fileName: "steam-itemdefs.playtest.json" },
    release: { appId: 2583700, fileName: "steam-itemdefs.release.json" },
});

const BUILD_CHANNEL_MASKS = Object.freeze({
    playtest: 2,
    demo: 4,
    release: 8,
    all: 2 | 4 | 8,
});

const ITEM_TYPE_NAMES = Object.freeze({
    1: "item",
    2: "bundle",
    3: "generator",
    4: "playtimegenerator",
    5: "tag_generator",
});

const RARITY_TAGS = Object.freeze({
    1: "mythic",
    2: "legendary",
    3: "epic",
    4: "rare",
    5: "uncommon",
    6: "common",
    21: "special_1",
    22: "special_2",
});

const BLIND_BOX_ITEM_RULES = Object.freeze({
    1: { acquisitionType: 2, weightField: "StandardBoxWeight" },
    2: { acquisitionType: 2, weightField: "NewbieBoxWeight" },
    3: { acquisitionType: 3, weightField: "RefreshmentBoxWeight" },
    4: { acquisitionType: 4, weightField: "EventBoxWeight" },
});

const ID_RANGE_ROWS = Object.freeze({
    formalItem: 1001,
    blindBoxCost: 1003,
    blindBoxGenerator: 1006,
    playtestOnly: 1009,
    linkTreeReceipt: 1013,
    newbieProgressReceipt: 1033,
    claimBundle: 1016,
    linkTreeBundle: 1017,
    playtimeGenerator: 1021,
    newbiePlaytimeGenerator: 1023,
    recurringPlaytimeGenerator: 1024,
    reservedLow: 1028,
    newbieDirectRewardPlaytimeGenerator: 1030,
    recurringDirectRewardPlaytimeGenerator: 1032,
});

function parseArguments(argv) {
    const options = {
        itemDefInput: DEFAULT_ITEM_DEF_INPUT,
        linkTreeInput: DEFAULT_LINK_TREE_INPUT,
        itemInput: DEFAULT_ITEM_INPUT,
        blindBoxInput: DEFAULT_BLIND_BOX_INPUT,
        blindBoxScheduleInput: DEFAULT_BLIND_BOX_SCHEDULE_INPUT,
        blindBoxRarityRateInput: DEFAULT_BLIND_BOX_RARITY_RATE_INPUT,
        gameDevelopConfigInput: DEFAULT_GAME_DEVELOP_CONFIG_INPUT,
        itemDefIdRangeInput: DEFAULT_ITEM_DEF_ID_RANGE_INPUT,
        outputRoot: DEFAULT_OUTPUT_ROOT,
        channels: Object.keys(CHANNELS),
    };

    for (let index = 0; index < argv.length; index += 1) {
        const argument = argv[index];
        if (argument === "--help" || argument === "-h") {
            printUsage();
            return null;
        }

        const value = argv[index + 1];
        if (!argument.startsWith("--")) {
            throw new Error(`未知参数：${argument}`);
        }
        if (!value || value.startsWith("--")) {
            throw new Error(`${argument} 需要一个参数值。`);
        }
        index += 1;

        switch (argument) {
            case "--item-def-input":
                options.itemDefInput = path.resolve(value);
                break;
            case "--link-tree-input":
                options.linkTreeInput = path.resolve(value);
                break;
            case "--item-input":
                options.itemInput = path.resolve(value);
                break;
            case "--blind-box-input":
                options.blindBoxInput = path.resolve(value);
                break;
            case "--blind-box-schedule-input":
                options.blindBoxScheduleInput = path.resolve(value);
                break;
            case "--blind-box-rarity-rate-input":
                options.blindBoxRarityRateInput = path.resolve(value);
                break;
            case "--game-develop-config-input":
                options.gameDevelopConfigInput = path.resolve(value);
                break;
            case "--item-def-id-range-input":
                options.itemDefIdRangeInput = path.resolve(value);
                break;
            case "--output-root":
                options.outputRoot = path.resolve(value);
                break;
            case "--channel": {
                const normalized = value.toLowerCase();
                if (normalized === "both") {
                    options.channels = Object.keys(CHANNELS);
                } else if (Object.hasOwn(CHANNELS, normalized)) {
                    options.channels = [normalized];
                } else {
                    throw new Error(`--channel 只支持 both、playtest 或 release，实际为：${value}`);
                }
                break;
            }
            default:
                throw new Error(`未知参数：${argument}`);
        }
    }

    return options;
}

function printUsage() {
    console.log(`用法：
  node lucky-steamworks/steam-item-def/build-steam-item-defs.js [选项]

选项：
  --item-def-input <file>  Luban 导出的 tbsteamitemdef.json
  --link-tree-input <file> Luban 导出的 tblinktree.json
  --item-input <file>      Luban 导出的 tbitem.json
  --blind-box-input <file> Luban 导出的 tbblindbox.json
  --blind-box-schedule-input <file> Luban 导出的 tbblindboxschedule.json
  --blind-box-rarity-rate-input <file> Luban 导出的 tbblindboxrarityrate.json
  --game-develop-config-input <file> Luban 导出的 tbgamedevelopconfig.json
  --item-def-id-range-input <file> Luban 导出的 tbsteamitemdefidrange.json
  --output-root <dir>      输出目录
  --channel <value>        both（默认）、playtest 或 release
  -h, --help               显示本帮助
`);
}

function readJsonArray(filePath, label) {
    if (!fs.existsSync(filePath)) {
        throw new Error(`找不到${label}：${filePath}`);
    }

    let value;
    try {
        value = JSON.parse(fs.readFileSync(filePath, "utf8"));
    } catch (error) {
        throw new Error(`无法解析${label} ${filePath}：${error.message}`);
    }

    if (!Array.isArray(value)) {
        throw new Error(`${label}根节点必须是数组：${filePath}`);
    }
    return value;
}

function buildIdRangePlan(records) {
    const errors = [];
    const byId = new Map();
    for (const record of records) {
        const label = `SteamItemDefIdRange ${record.Id ?? "<未知>"}`;
        if (!Number.isInteger(record.Id) || record.Id <= 0) {
            errors.push(`${label}：Id 必须是正整数。`);
            continue;
        }
        if (byId.has(record.Id)) {
            errors.push(`${label}：Id 重复。`);
            continue;
        }
        if (!Number.isInteger(record.StartItemDefId)
            || !Number.isInteger(record.EndItemDefId)
            || record.StartItemDefId <= 0
            || record.EndItemDefId >= 1000000
            || record.StartItemDefId > record.EndItemDefId) {
            errors.push(`${label}：起止 ItemDef ID 必须位于 1..999999，且起始值不能大于截止值。`);
            continue;
        }
        byId.set(record.Id, record);
    }

    for (const [name, id] of Object.entries(ID_RANGE_ROWS)) {
        if (!byId.has(id)) errors.push(`SteamItemDefIdRange：缺少 ${name} 规划行 ${id}。`);
    }

    return { records, byId, errors };
}

function idInRange(plan, rangeId, itemDefId) {
    const range = plan?.byId.get(rangeId);
    return Boolean(range)
        && Number.isInteger(itemDefId)
        && itemDefId >= range.StartItemDefId
        && itemDefId <= range.EndItemDefId;
}

function validateIdPlanning(
    plan,
    itemDefRecords,
    itemRecords,
    linkTreeRecords,
    blindBoxRecords,
    scheduleRecords,
) {
    if (!plan || plan.records.length === 0) return [];
    const errors = [...plan.errors];
    if (plan.errors.length > 0) return errors;

    for (const item of itemRecords) {
        if (!Number.isInteger(item.SteamItemDefId) || item.SteamItemDefId <= 0) continue;
        if (idInRange(plan, ID_RANGE_ROWS.playtestOnly, item.SteamItemDefId)) continue;
        const expected = 100000 + item.Id;
        if (!idInRange(plan, ID_RANGE_ROWS.formalItem, item.SteamItemDefId)
            || item.SteamItemDefId !== expected) {
            errors.push(
                `Item ${item.Id} ${item.Name || ""}：正式 SteamItemDefId 必须为 100000 + Item.Id = ${expected}，实际为 ${item.SteamItemDefId}。`,
            );
        }
    }

    for (const definition of itemDefRecords.filter(record => record.IsEnabled === true)) {
        if (idInRange(plan, ID_RANGE_ROWS.playtestOnly, definition.Id)) continue;
        const valid = definition.Type === 1
            ? idInRange(plan, ID_RANGE_ROWS.blindBoxCost, definition.Id)
                || idInRange(plan, ID_RANGE_ROWS.newbieProgressReceipt, definition.Id)
                || idInRange(plan, ID_RANGE_ROWS.linkTreeReceipt, definition.Id)
            : definition.Type === 2
                ? idInRange(plan, ID_RANGE_ROWS.claimBundle, definition.Id)
                : definition.Type === 3
                    ? idInRange(plan, ID_RANGE_ROWS.blindBoxGenerator, definition.Id)
                    : definition.Type === 4
                        ? idInRange(plan, ID_RANGE_ROWS.playtimeGenerator, definition.Id)
                        : false;
        if (!valid) {
            errors.push(`${definition.Key || definition.Id}：ItemDef ${definition.Id} 不在其 Type 对应的已规划正式 ID 段内。`);
        }
    }

    for (const entry of linkTreeRecords.filter(record => record.IsEnabled === true)) {
        if (!idInRange(plan, ID_RANGE_ROWS.playtestOnly, entry.SteamReceiptItemDefId)
            && !idInRange(plan, ID_RANGE_ROWS.linkTreeReceipt, entry.SteamReceiptItemDefId)) {
            errors.push(`LinkTree ${entry.Id}：永久回执 ${entry.SteamReceiptItemDefId} 不在 LinkTree 回执正式段内。`);
        }
        if (!idInRange(plan, ID_RANGE_ROWS.playtestOnly, entry.SteamClaimBundleItemDefId)
            && !idInRange(plan, ID_RANGE_ROWS.linkTreeBundle, entry.SteamClaimBundleItemDefId)) {
            errors.push(`LinkTree ${entry.Id}：领奖 Bundle ${entry.SteamClaimBundleItemDefId} 不在 LinkTree Bundle 正式段内。`);
        }
    }

    for (const schedule of scheduleRecords.filter(record =>
        record.IsEnabled === true && record.SteamPlaytimeGeneratorItemDefId > 0)) {
        const itemDefId = schedule.SteamPlaytimeGeneratorItemDefId;
        if (idInRange(plan, ID_RANGE_ROWS.playtestOnly, itemDefId)) continue;
        const expectedRange = schedule.IsLoopTrack === true
            ? ID_RANGE_ROWS.recurringDirectRewardPlaytimeGenerator
            : ID_RANGE_ROWS.newbieDirectRewardPlaytimeGenerator;
        if (!idInRange(plan, expectedRange, itemDefId)) {
            errors.push(`BlindBoxSchedule ${schedule.Id}：PlaytimeGenerator ${itemDefId} 不在对应的正式投放段内。`);
        }
    }
    for (const schedule of scheduleRecords.filter(record =>
        record.IsEnabled === true && record.SteamCompletionReceiptItemDefId > 0)) {
        const itemDefId = schedule.SteamCompletionReceiptItemDefId;
        if (idInRange(plan, ID_RANGE_ROWS.playtestOnly, itemDefId)) continue;
        if (!idInRange(plan, ID_RANGE_ROWS.newbieProgressReceipt, itemDefId)) {
            errors.push(`BlindBoxSchedule ${schedule.Id}：完成回执 ${itemDefId} 不在新手状态永久回执正式段内。`);
        }
    }
    return errors;
}

function requiredString(record, field, label, errors) {
    const value = record[field];
    if (typeof value !== "string" || !value.trim()) {
        errors.push(`${label}：${field} 必须是非空字符串。`);
        return "";
    }
    return value.trim();
}

function optionalString(record, field, label, errors) {
    const value = record[field];
    if (typeof value !== "string") {
        errors.push(`${label}：${field} 必须是字符串。`);
        return "";
    }
    return value.trim();
}

function requiredBoolean(record, field, label, errors) {
    const value = record[field];
    if (typeof value !== "boolean") {
        errors.push(`${label}：${field} 必须是 true 或 false。`);
        return false;
    }
    return value;
}

function greatestCommonDivisor(left, right) {
    let a = Math.abs(left);
    let b = Math.abs(right);
    while (b !== 0) [a, b] = [b, a % b];
    return a;
}

function reduceWeights(entries) {
    const divisor = entries.reduce(
        (current, entry) => current === 0 ? entry.weight : greatestCommonDivisor(current, entry.weight),
        0,
    );
    if (divisor <= 1) return entries;
    return entries.map(entry => ({ ...entry, weight: entry.weight / divisor }));
}

function allocateGeneratorWeights(entries) {
    const weighted = entries.map(entry => {
        const rawWeight = entry.probability * AUTO_GENERATOR_WEIGHT_SCALE;
        return {
            ...entry,
            rawWeight,
            weight: Math.max(1, Math.floor(rawWeight)),
            remainder: rawWeight - Math.floor(rawWeight),
        };
    });
    let difference = AUTO_GENERATOR_WEIGHT_SCALE - weighted.reduce((sum, entry) => sum + entry.weight, 0);

    if (difference > 0) {
        const order = [...weighted].sort((left, right) =>
            right.remainder - left.remainder || left.itemDefId - right.itemDefId);
        for (let index = 0; index < difference; index += 1) order[index % order.length].weight += 1;
    } else if (difference < 0) {
        const order = [...weighted].sort((left, right) =>
            right.weight - left.weight || left.itemDefId - right.itemDefId);
        let remaining = -difference;
        for (const entry of order) {
            const removable = Math.min(remaining, entry.weight - 1);
            entry.weight -= removable;
            remaining -= removable;
            if (remaining === 0) break;
        }
        if (remaining !== 0) throw new Error("自动奖池权重无法压缩到 Steam 整数范围。");
    }

    return reduceWeights(weighted)
        .sort((left, right) => left.itemDefId - right.itemDefId);
}

function buildSteamTags(record, label, errors) {
    const rarityTag = RARITY_TAGS[record.ItemRarity];
    if (!rarityTag) {
        errors.push(`${label}：ItemRarity ${record.ItemRarity} 没有对应的 Steam rarity 标签。`);
        return "";
    }

    const customTags = optionalString(record, "SteamTags", label, errors)
        .split(";")
        .map(tag => tag.trim())
        .filter(Boolean);
    if (customTags.some(tag => tag.toLowerCase().startsWith("rarity:"))) {
        errors.push(`${label}：SteamTags 不应手填 rarity 标签；该标签由 ItemRarity 自动生成。`);
    }
    return [`rarity:${rarityTag}`, ...customTags].join(";");
}

function validateAndBuildItemDefs(records) {
    const errors = [];
    const warnings = [];
    const seenIds = new Set();
    const seenKeys = new Set();
    const allById = new Map();
    const enabledById = new Map();
    const items = [];
    const definitions = [];

    for (const record of records) {
        const id = record.Id;
        const rawKey = typeof record.Key === "string" ? record.Key.trim() : "";
        const label = rawKey || (Number.isInteger(id) ? `ItemDef ${id}` : "<未知 SteamItemDef>");
        const key = requiredString(record, "Key", label, errors);

        if (!Number.isInteger(id) || id <= 0 || id >= 1000000) {
            errors.push(`${label}：Id 必须是 1 到 999999 之间的整数。`);
        } else if (seenIds.has(id)) {
            errors.push(`${label}：ItemDef Id ${id} 重复。`);
        } else {
            seenIds.add(id);
            allById.set(id, record);
        }

        if (key) {
            if (!/^[A-Za-z][A-Za-z0-9_]*$/.test(key)) {
                errors.push(`${label}：Key 只能使用字母、数字和下划线，且必须以字母开头。`);
            }
            if (seenKeys.has(key)) {
                errors.push(`${label}：Key ${key} 重复。`);
            }
            seenKeys.add(key);
        }

        const typeName = ITEM_TYPE_NAMES[record.Type];
        if (!typeName) {
            errors.push(`${label}：不支持的 Type 值 ${record.Type}。`);
        }

        const name = requiredString(record, "Name", label, errors);
        const description = optionalString(record, "Description", label, errors);
        const promo = optionalString(record, "PromoRule", label, errors);
        const bundle = optionalString(record, "Bundle", label, errors);
        const grantedManually = requiredBoolean(record, "GrantedManually", label, errors);
        const tradable = requiredBoolean(record, "Tradable", label, errors);
        const marketable = requiredBoolean(record, "Marketable", label, errors);
        const gameOnly = requiredBoolean(record, "GameOnly", label, errors);
        const storeHidden = requiredBoolean(record, "StoreHidden", label, errors);
        const autoStack = requiredBoolean(record, "AutoStack", label, errors);
        const isEnabled = requiredBoolean(record, "IsEnabled", label, errors);
        const steamUseDropLimit = record.SteamUseDropLimit ?? false;
        const steamDropLimit = record.SteamDropLimit ?? 0;

        if (typeof steamUseDropLimit !== "boolean") {
            errors.push(`${label}：SteamUseDropLimit 必须是布尔值。`);
        }
        if (!Number.isInteger(steamDropLimit) || steamDropLimit < 0) {
            errors.push(`${label}：SteamDropLimit 必须是非负整数。`);
        }
        if (steamUseDropLimit === true && record.Type !== 4) {
            errors.push(`${label}：只有 PlaytimeGenerator 可以配置 SteamUseDropLimit。`);
        }
        if (steamUseDropLimit !== true && steamDropLimit !== 0) {
            errors.push(`${label}：SteamUseDropLimit=false 时 SteamDropLimit 必须为 0。`);
        }

        if (grantedManually && !promo) {
            errors.push(`${label}：GrantedManually=true 时 PromoRule 不能为空。`);
        }
        if ([2, 3, 4].includes(record.Type) && !bundle) {
            errors.push(`${label}：${typeName || "复杂物品"} 必须填写 Bundle。`);
        }
        if (bundle === AUTO_BUNDLE_MARKER && record.Type !== 3) {
            errors.push(`${label}：${AUTO_BUNDLE_MARKER} 目前只支持 Type=Generator。`);
        }
        if (record.Type === 1 && bundle) {
            errors.push(`${label}：普通 item 的 Bundle 必须留空。`);
        }
        if (record.Type === 5) {
            errors.push(`${label}：当前表缺少 tag_generator 所需字段，转换器暂不支持该类型。`);
        }

        if (!isEnabled) {
            warnings.push(
                `${label}：IsEnabled=false，本次不会输出；请继续保留该行和 ItemDef ID 台账，已发布定义不得复用。`,
            );
            continue;
        }

        if (Number.isInteger(id)) {
            enabledById.set(id, record);
        }

        const item = {
            itemdefid: id,
            type: typeName,
            name,
        };
        if (description) item.description = description;
        if (promo) item.promo = promo;
        item.granted_manually = grantedManually;
        item.tradable = tradable;
        item.marketable = marketable;
        item.game_only = gameOnly;
        item.store_hidden = storeHidden;
        item.auto_stack = autoStack;
        if (bundle) item.bundle = bundle;
        if (steamUseDropLimit === true) {
            item.use_drop_limit = true;
            item.drop_limit = steamDropLimit;
        }
        items.push(item);
        definitions.push({
            id,
            key,
            source: "SteamItemDef",
            sourceId: id,
            schemaItem: item,
        });
    }

    items.sort((left, right) => left.itemdefid - right.itemdefid);
    return { items, definitions, allById, enabledById, errors, warnings };
}

function validateAndBuildGameItems(records) {
    const errors = [];
    const warnings = [];
    const allById = new Map();
    const enabledById = new Map();
    const items = [];
    const definitions = [];

    for (const record of records) {
        const localItemId = record.Id;
        const itemDefId = record.SteamItemDefId;
        const label = `Item ${localItemId ?? "<未知>"} ${typeof record.Name === "string" ? record.Name : ""}`.trim();

        if (!Number.isInteger(itemDefId) || itemDefId < 0 || itemDefId >= 1000000) {
            errors.push(`${label}：SteamItemDefId 必须是 0 到 999999 之间的整数。`);
            continue;
        }
        if (itemDefId === 0) continue;
        if (!Number.isInteger(localItemId) || localItemId <= 0) {
            errors.push(`${label}：Item.Id 必须是正整数。`);
        }
        if (record.SteamItemDefType !== 1) {
            errors.push(`${label}：实际游戏物品的 SteamItemDefType 必须是 Item。`);
        }
        if (allById.has(itemDefId)) {
            errors.push(`${label}：SteamItemDefId ${itemDefId} 已被 Item ${allById.get(itemDefId).Id} 使用。`);
            continue;
        }

        const name = requiredString(record, "Name", label, errors);
        const description = optionalString(record, "SteamDescription", label, errors);
        const gameOnly = requiredBoolean(record, "SteamGameOnly", label, errors);
        const tradable = requiredBoolean(record, "SteamTradable", label, errors);
        const marketable = requiredBoolean(record, "SteamMarketable", label, errors);
        const autoStack = requiredBoolean(record, "SteamAutoStack", label, errors);
        const hidden = requiredBoolean(record, "SteamHidden", label, errors);
        const displayType = optionalString(record, "SteamDisplayType", label, errors);
        const tags = buildSteamTags(record, label, errors);
        const iconUrl = optionalString(record, "SteamIconUrl", label, errors);
        const iconUrlLarge = optionalString(record, "SteamIconUrlLarge", label, errors);

        const item = {
            itemdefid: itemDefId,
            type: "item",
            name,
            granted_manually: false,
            tradable,
            marketable,
            game_only: gameOnly,
            auto_stack: autoStack,
            hidden,
        };
        if (description) item.description = description;
        if (displayType) item.display_type = displayType;
        if (tags) item.tags = tags;
        if (iconUrl) item.icon_url = iconUrl;
        if (iconUrlLarge) item.icon_url_large = iconUrlLarge;

        allById.set(itemDefId, record);
        enabledById.set(itemDefId, record);
        items.push(item);
        definitions.push({
            id: itemDefId,
            key: `Item_${localItemId}`,
            source: "Item",
            sourceId: localItemId,
            schemaItem: item,
        });
    }

    items.sort((left, right) => left.itemdefid - right.itemdefid);
    return { items, definitions, allById, enabledById, errors, warnings };
}

function parseFixedBundleRecipe(bundle) {
    if (typeof bundle !== "string" || !bundle.trim()) return null;

    const quantities = new Map();
    for (const rawRecipe of bundle.split(";")) {
        const recipe = rawRecipe.trim();
        const match = /^(\d+)(?:x(\d+))?$/.exec(recipe);
        if (!match) return null;
        const itemDefId = Number.parseInt(match[1], 10);
        const quantity = match[2] ? Number.parseInt(match[2], 10) : 1;
        if (quantity <= 0) return null;
        quantities.set(itemDefId, (quantities.get(itemDefId) ?? 0) + quantity);
    }
    return quantities;
}

function formatExpectedBundle(entries) {
    return entries.map(([itemDefId, quantity]) => `${itemDefId}x${quantity}`).join(";");
}

function validateLinkTree(records, itemDefs, itemRecords) {
    const errors = [];
    const warnings = [];
    const claimedByReceipt = new Map();
    const claimedByBundle = new Map();
    const itemsById = new Map(itemRecords.map(record => [record.Id, record]));
    let checkedReferenceCount = 0;

    for (const record of records) {
        const key = typeof record.Key === "string" && record.Key.trim()
            ? record.Key.trim()
            : `LinkTree ${record.Id ?? "<未知>"}`;
        const receiptItemDefId = record.SteamReceiptItemDefId ?? record.SteamPromoItemDefId ?? 0;
        const claimBundleItemDefId = record.SteamClaimBundleItemDefId ?? 0;
        const isEnabled = record.IsEnabled === true;
        const buildChannelMask = record.BuildChannelMask;

        if (!Number.isInteger(buildChannelMask)
            || buildChannelMask < 0
            || (buildChannelMask & ~BUILD_CHANNEL_MASKS.all) !== 0) {
            errors.push(`${key}：BuildChannelMask 必须只包含 Playtest、Demo、Release。`);
            continue;
        }
        if (isEnabled && buildChannelMask === 0) {
            errors.push(`${key}：启用的入口必须至少配置一个 BuildChannelMask。`);
            continue;
        }

        if (!Number.isInteger(receiptItemDefId) || receiptItemDefId < 0) {
            errors.push(`${key}：SteamReceiptItemDefId 必须是大于等于 0 的整数。`);
            continue;
        }
        if (!Number.isInteger(claimBundleItemDefId) || claimBundleItemDefId < 0) {
            errors.push(`${key}：SteamClaimBundleItemDefId 必须是大于等于 0 的整数。`);
            continue;
        }

        if (receiptItemDefId === 0) {
            if (isEnabled) {
                errors.push(`${key}：启用的入口必须配置 SteamReceiptItemDefId。`);
            }
            continue;
        }

        checkedReferenceCount += 1;
        const receipt = itemDefs.allById.get(receiptItemDefId);
        if (!receipt) {
            errors.push(`${key}：引用的永久回执 Steam ItemDef ${receiptItemDefId} 不存在。`);
            continue;
        }
        if (!itemDefs.enabledById.has(receiptItemDefId)) {
            errors.push(`${key}：引用的永久回执 Steam ItemDef ${receiptItemDefId} 已禁用。`);
        }
        if (claimedByReceipt.has(receiptItemDefId)) {
            errors.push(`${key}：Steam ItemDef ${receiptItemDefId} 已被 ${claimedByReceipt.get(receiptItemDefId)} 使用；每个永久回执只能对应一个入口。`);
        } else {
            claimedByReceipt.set(receiptItemDefId, key);
        }

        if (receipt.Type !== 1) {
            errors.push(`${key}：永久回执 ${receiptItemDefId} 必须是 Type=Item。`);
        }
        if (receipt.PromoRule !== "manual") {
            errors.push(`${key}：永久回执 ${receiptItemDefId} 必须配置 PromoRule=manual。`);
        }
        if (receipt.GrantedManually !== true) {
            errors.push(`${key}：永久回执 ${receiptItemDefId} 必须配置 GrantedManually=true。`);
        }
        if (receipt.Tradable !== false || receipt.Marketable !== false) {
            errors.push(`${key}：永久回执 ${receiptItemDefId} 必须不可交易且不可出售。`);
        }
        if (receipt.GameOnly !== true || receipt.StoreHidden !== true) {
            errors.push(`${key}：永久回执 ${receiptItemDefId} 必须配置 GameOnly=true 且 StoreHidden=true。`);
        }
        if (receipt.Bundle) {
            errors.push(`${key}：永久回执 ${receiptItemDefId} 的 Bundle 必须留空。`);
        }

        // Disabled legacy entries may retain only their historical receipt.
        if (!isEnabled && claimBundleItemDefId === 0) continue;
        if (claimBundleItemDefId === 0) {
            errors.push(`${key}：启用的入口必须配置 SteamClaimBundleItemDefId。`);
            continue;
        }

        const claimBundle = itemDefs.allById.get(claimBundleItemDefId);
        if (!claimBundle) {
            errors.push(`${key}：引用的领奖 Bundle Steam ItemDef ${claimBundleItemDefId} 不存在。`);
            continue;
        }
        if (!itemDefs.enabledById.has(claimBundleItemDefId)) {
            errors.push(`${key}：引用的领奖 Bundle Steam ItemDef ${claimBundleItemDefId} 已禁用。`);
        }
        if (claimedByBundle.has(claimBundleItemDefId)) {
            errors.push(`${key}：领奖 Bundle ${claimBundleItemDefId} 已被 ${claimedByBundle.get(claimBundleItemDefId)} 使用；每个入口必须使用独立 Bundle。`);
        } else {
            claimedByBundle.set(claimBundleItemDefId, key);
        }
        if (claimBundle.Type !== 2) {
            errors.push(`${key}：领奖目标 ${claimBundleItemDefId} 必须是 Type=Bundle。`);
        }
        if (claimBundle.PromoRule !== "manual" || claimBundle.GrantedManually !== true) {
            errors.push(`${key}：领奖 Bundle ${claimBundleItemDefId} 必须配置 PromoRule=manual 且 GrantedManually=true。`);
        }
        if (claimBundle.Tradable !== false || claimBundle.Marketable !== false
            || claimBundle.GameOnly !== true || claimBundle.StoreHidden !== true) {
            errors.push(`${key}：领奖 Bundle ${claimBundleItemDefId} 必须不可交易、不可出售，并配置 GameOnly=true、StoreHidden=true。`);
        }

        const expected = new Map([[receiptItemDefId, 1]]);
        if (record.RewardType === 1) {
            const item = itemsById.get(record.RewardItemId);
            if (!item) {
                errors.push(`${key}：RewardItemId ${record.RewardItemId} 不存在。`);
            } else if (!Number.isInteger(item.SteamItemDefId) || item.SteamItemDefId <= 0) {
                errors.push(`${key}：奖励 Item ${record.RewardItemId} 没有配置 SteamItemDefId。`);
            } else {
                expected.set(item.SteamItemDefId, (expected.get(item.SteamItemDefId) ?? 0) + 1);
            }
        } else if (record.RewardType === 2) {
            if (!Number.isInteger(record.RewardChips) || record.RewardChips <= 0) {
                errors.push(`${key}：FixedChips 必须配置大于 0 的 RewardChips。`);
            }
        } else if (record.RewardType === 4) {
            errors.push(
                `${key}：LinkTree BlindBox 奖励尚未迁移到直接奖励架构；请改用固定物品/筹码，或先完成独立的数据设计。`,
            );
            continue;
        } else if (record.RewardType === 3) {
            errors.push(`${key}：SequentialPack 尚未定义可信的 Steam Bundle 配方。`);
        } else if (record.RewardType !== 0) {
            errors.push(`${key}：不支持的 RewardType ${record.RewardType}。`);
        }

        const actual = parseFixedBundleRecipe(claimBundle.Bundle);
        const expectedRecipe = formatExpectedBundle([...expected.entries()]);
        if (!actual || actual.size !== expected.size
            || [...expected].some(([id, quantity]) => actual.get(id) !== quantity)) {
            errors.push(`${key}：领奖 Bundle ${claimBundleItemDefId} 的内容必须为 ${expectedRecipe}，实际为 ${claimBundle.Bundle || "<空>"}。`);
        }
    }

    return { checkedReferenceCount, errors, warnings };
}

function validateBlindBoxCompletionReceipts(scheduleRecords, itemDefs) {
    const errors = [];
    const references = [];
    const claimedByReceipt = new Map();

    for (const schedule of scheduleRecords) {
        const label = `BlindBoxSchedule ${schedule.Id ?? "<未知>"}`;
        const receiptItemDefId = schedule.SteamCompletionReceiptItemDefId;
        if (!Number.isInteger(receiptItemDefId)
            || receiptItemDefId < 0
            || receiptItemDefId >= 1000000) {
            errors.push(`${label}：SteamCompletionReceiptItemDefId 必须是 0 到 999999 之间的整数。`);
            continue;
        }
        if (receiptItemDefId === 0) continue;
        if (schedule.IsLoopTrack === true) {
            errors.push(`${label}：循环 Schedule 不得配置一次性完成回执。`);
        }

        const previousScheduleId = claimedByReceipt.get(receiptItemDefId);
        if (previousScheduleId) {
            errors.push(`${label}：完成回执 ${receiptItemDefId} 已被 BlindBoxSchedule ${previousScheduleId} 使用。`);
        } else {
            claimedByReceipt.set(receiptItemDefId, schedule.Id);
        }

        const receipt = itemDefs.allById.get(receiptItemDefId);
        if (!receipt) {
            errors.push(`${label}：引用的完成回执 Steam ItemDef ${receiptItemDefId} 不存在。`);
            continue;
        }
        if (!itemDefs.enabledById.has(receiptItemDefId)) {
            errors.push(`${label}：引用的完成回执 Steam ItemDef ${receiptItemDefId} 已禁用。`);
        }
        if (receipt.Type !== 1) {
            errors.push(`${label}：完成回执 ${receiptItemDefId} 必须是 Type=Item。`);
        }
        if (receipt.PromoRule !== "manual" || receipt.GrantedManually !== true) {
            errors.push(`${label}：完成回执 ${receiptItemDefId} 必须配置 PromoRule=manual 且 GrantedManually=true。`);
        }
        if (receipt.Tradable !== false || receipt.Marketable !== false) {
            errors.push(`${label}：完成回执 ${receiptItemDefId} 必须不可交易且不可出售。`);
        }
        if (receipt.GameOnly !== true || receipt.StoreHidden !== true) {
            errors.push(`${label}：完成回执 ${receiptItemDefId} 必须配置 GameOnly=true 且 StoreHidden=true。`);
        }
        if (receipt.AutoStack !== false) {
            errors.push(`${label}：完成回执 ${receiptItemDefId} 必须配置 AutoStack=false。`);
        }
        if (receipt.Bundle) {
            errors.push(`${label}：完成回执 ${receiptItemDefId} 的 Bundle 必须留空。`);
        }

        if (schedule.IsEnabled === true) {
            references.push({ scheduleId: schedule.Id, receiptItemDefId });
        }
    }

    return { references, errors };
}

function mergeDefinitions(itemDefs, gameItems) {
    const errors = [];
    const items = [...itemDefs.items];
    const definitions = [...itemDefs.definitions];
    const enabledById = new Map(definitions.map(definition => [definition.id, definition]));

    for (const definition of gameItems.definitions) {
        const existing = itemDefs.allById.get(definition.id);
        if (existing) {
            errors.push(
                `Item ${definition.sourceId}：SteamItemDefId ${definition.id} 与 SteamItemDef ${existing.Key || existing.Id} 重复。`,
            );
            continue;
        }
        enabledById.set(definition.id, definition);
        definitions.push(definition);
        items.push(definition.schemaItem);
    }

    items.sort((left, right) => left.itemdefid - right.itemdefid);
    definitions.sort((left, right) => left.id - right.id);
    return { items, definitions, enabledById, errors };
}

function validateBundleReferences(definitions, enabledById) {
    const errors = [];

    for (const definition of definitions) {
        const item = definition.schemaItem;
        if (!["bundle", "generator", "playtimegenerator"].includes(item.type) || !item.bundle) continue;
        if (item.bundle === AUTO_BUNDLE_MARKER) {
            errors.push(`${definition.key}：${AUTO_BUNDLE_MARKER} 没有找到可生成配方的 BlindBox 映射。`);
            continue;
        }

        for (const rawRecipe of item.bundle.split(";")) {
            const recipe = rawRecipe.trim();
            const match = /^(\d+)(?:x(\d+))?$/.exec(recipe);
            if (!match) {
                errors.push(`${definition.key}：Bundle 配方 ${recipe || "<空>"} 格式无效。`);
                continue;
            }

            const referencedId = Number.parseInt(match[1], 10);
            const quantity = match[2] ? Number.parseInt(match[2], 10) : 1;
            if (quantity <= 0) {
                errors.push(`${definition.key}：Bundle 配方 ${recipe} 的数量或权重必须大于 0。`);
            }
            if (!enabledById.has(referencedId)) {
                errors.push(`${definition.key}：Bundle 引用的 Steam ItemDef ${referencedId} 未导出。`);
            }
        }
    }

    return errors;
}

function buildAutoGeneratorBundle(box, itemRecords, rarityRateRecords, merged, errors) {
    const label = `BlindBox ${box.Id} ${box.Name || ""}`.trim();
    const rule = BLIND_BOX_ITEM_RULES[box.BoxType];
    if (!rule) {
        errors.push(`${label}：BoxType ${box.BoxType} 没有对应的 Item 权重字段。`);
        return "";
    }

    const rarityWeights = new Map();
    for (const rate of rarityRateRecords.filter(rate => rate.IsEnabled === true && rate.BlindBoxId === box.Id)) {
        if (!Number.isInteger(rate.Rarity)) {
            errors.push(`${label}：BlindBoxRarityRate ${rate.Id ?? "<未知>"} 的 Rarity 必须是整数。`);
            continue;
        }
        if (!Number.isInteger(rate.Weight) || rate.Weight < 0) {
            errors.push(`${label}：BlindBoxRarityRate ${rate.Id ?? "<未知>"} 的 Weight 必须是非负整数。`);
            continue;
        }
        if (rate.Weight > 0) {
            rarityWeights.set(rate.Rarity, (rarityWeights.get(rate.Rarity) || 0) + rate.Weight);
        }
    }
    if (rarityWeights.size === 0) {
        errors.push(`${label}：没有启用且权重大于 0 的 BlindBoxRarityRate。`);
        return "";
    }

    const errorCountBefore = errors.length;
    const totalRarityWeight = [...rarityWeights.values()].reduce((sum, weight) => sum + weight, 0);
    const probabilities = [];
    for (const [rarity, rarityWeight] of [...rarityWeights].sort((left, right) => left[0] - right[0])) {
        const weightedItems = itemRecords
            .filter(item => item.ItemRarity === rarity)
            .filter(item => Number.isInteger(item[rule.weightField]) && item[rule.weightField] > 0);
        const preferredItems = weightedItems.filter(item => item.AcquisitionType === rule.acquisitionType);
        const candidates = preferredItems.length > 0 ? preferredItems : weightedItems;
        if (candidates.length === 0) {
            errors.push(`${label}：品质 ${rarity} 的概率大于 0，但没有配置 ${rule.weightField} 候选物品。`);
            continue;
        }

        const totalItemWeight = candidates.reduce((sum, item) => sum + item[rule.weightField], 0);
        for (const item of candidates) {
            if (!Number.isInteger(item.SteamItemDefId) || item.SteamItemDefId <= 0) {
                errors.push(`${label}：候选 Item ${item.Id} ${item.Name || ""} 没有配置 SteamItemDefId。`);
                continue;
            }
            if (!merged.enabledById.has(item.SteamItemDefId)) {
                errors.push(`${label}：候选 Item ${item.Id} 引用的 Steam ItemDef ${item.SteamItemDefId} 未导出。`);
                continue;
            }
            probabilities.push({
                itemDefId: item.SteamItemDefId,
                probability: (rarityWeight / totalRarityWeight) * (item[rule.weightField] / totalItemWeight),
            });
        }
    }

    if (errors.length > errorCountBefore || probabilities.length === 0) return "";
    const weights = allocateGeneratorWeights(probabilities);
    return weights.map(entry => `${entry.itemDefId}x${entry.weight}`).join(";");
}

function applyBlindBoxMappings(
    records,
    scheduleRecords,
    merged,
    itemRecords = [],
    rarityRateRecords = [],
) {
    const errors = [];
    const warnings = [];
    const references = [];
    const boxesById = new Map(records.map(record => [record.Id, record]));
    const autoGeneratorIds = new Set(
        merged.definitions
            .filter(definition => definition.schemaItem.bundle === AUTO_BUNDLE_MARKER)
            .map(definition => definition.id),
    );
    const autoGeneratorRecipes = new Map();
    const checkedMappings = new Set();

    for (const schedule of scheduleRecords.filter(record => record.IsEnabled === true)) {
        const scheduleLabel = `BlindBoxSchedule ${schedule.Id ?? "<未知>"}`;
        if (!Number.isInteger(schedule.CostChipsOverride)) {
            errors.push(`${scheduleLabel}：CostChipsOverride 必须是整数。`);
        }

        const playtimeGeneratorId = schedule.SteamPlaytimeGeneratorItemDefId;
        if (!Number.isInteger(playtimeGeneratorId)
            || playtimeGeneratorId < 0
            || playtimeGeneratorId >= 1000000) {
            errors.push(`${scheduleLabel}：SteamPlaytimeGeneratorItemDefId 必须是 0 到 999999 之间的整数。`);
            continue;
        }
        if (playtimeGeneratorId === 0) continue;

        const record = boxesById.get(schedule.BlindBoxId);
        if (!record || record.IsEnabled !== true) {
            errors.push(`${scheduleLabel}：引用的 BlindBox ${schedule.BlindBoxId} 不存在或未启用。`);
            continue;
        }

        const fallbackBlindBoxId = schedule.FallbackBlindBoxId ?? 0;
        if (!Number.isInteger(fallbackBlindBoxId) || fallbackBlindBoxId < 0) {
            errors.push(`${scheduleLabel}：FallbackBlindBoxId 必须是非负整数。`);
        } else if (record.IsPlatformInventoryRequired === true) {
            const fallbackBox = boxesById.get(fallbackBlindBoxId);
            if (fallbackBlindBoxId === 0) {
                errors.push(`${scheduleLabel}：平台库存盲盒必须配置 FallbackBlindBoxId。`);
            } else if (!fallbackBox || fallbackBox.IsEnabled !== true) {
                errors.push(`${scheduleLabel}：Fallback BlindBox ${fallbackBlindBoxId} 不存在或未启用。`);
            } else if (fallbackBox.IsPlatformInventoryRequired === true || fallbackBox.BoxType !== 3) {
                errors.push(`${scheduleLabel}：Fallback BlindBox ${fallbackBlindBoxId} 必须是本地 Refreshment 盲盒。`);
            }
        } else if (fallbackBlindBoxId !== 0) {
            errors.push(`${scheduleLabel}：本地盲盒不应配置 FallbackBlindBoxId。`);
        }

        const label = `BlindBox ${record.Id} ${typeof record.Name === "string" ? record.Name : ""}`.trim();
        if (record.IsPlatformInventoryRequired !== true) {
            errors.push(`${scheduleLabel}：配置了 Steam PlaytimeGenerator，但 BlindBox ${record.Id} 不需要平台库存。`);
            continue;
        }

        const playtimeGenerator = merged.enabledById.get(playtimeGeneratorId);
        if (!playtimeGenerator || playtimeGenerator.schemaItem.type !== "playtimegenerator") continue;
        const recipe = parseFixedBundleRecipe(playtimeGenerator.schemaItem.bundle);
        if (!recipe || recipe.size !== 1) {
            errors.push(`${scheduleLabel}：PlaytimeGenerator ${playtimeGeneratorId} 必须只引用一个盲盒奖励池 Generator。`);
            continue;
        }
        const [[targetId, quantity]] = [...recipe.entries()];
        if (quantity !== 1) {
            errors.push(`${scheduleLabel}：PlaytimeGenerator ${playtimeGeneratorId} 必须以数量 1 引用奖励池 ${targetId}。`);
            continue;
        }

        const target = merged.enabledById.get(targetId);
        if (!target) {
            errors.push(`${scheduleLabel}：奖励池 Generator ${targetId} 不存在或未导出。`);
            continue;
        }
        if (target.schemaItem.type !== "generator") {
            errors.push(`${scheduleLabel}：PlaytimeGenerator ${playtimeGeneratorId} 的目标 ${targetId} 必须是 Type=Generator。`);
            continue;
        }

        if (autoGeneratorIds.has(targetId)) {
            const generatedBundle = buildAutoGeneratorBundle(
                record,
                itemRecords,
                rarityRateRecords,
                merged,
                errors,
            );
            const previous = autoGeneratorRecipes.get(targetId);
            if (generatedBundle && previous && previous.bundle !== generatedBundle) {
                errors.push(
                    `${label}：自动 Generator ${targetId} 已由 BlindBox ${previous.boxId} 生成不同奖池，不能复用。`,
                );
            } else if (generatedBundle && !previous) {
                autoGeneratorRecipes.set(targetId, { boxId: record.Id, bundle: generatedBundle });
                target.schemaItem.bundle = generatedBundle;
            }
        }

        const mappingKey = `${record.Id}:${targetId}`;
        if (!checkedMappings.has(mappingKey)) {
            checkedMappings.add(mappingKey);
            references.push({
                blindBoxId: record.Id,
                name: record.Name,
                targetItemDefId: targetId,
            });
        }
    }

    return { checkedReferenceCount: references.length, references, errors, warnings };
}

function getScheduleDropLimit(schedule, label, errors) {
    if (!Number.isInteger(schedule.MaxGrantCount) || schedule.MaxGrantCount < -1) {
        errors.push(`${label}：MaxGrantCount 必须是 -1 或非负整数。`);
        return null;
    }
    if (schedule.MaxGrantCount >= 0) return schedule.MaxGrantCount;
    return null;
}

function applyPlaytimeMappings(scheduleRecords, _configRecords, blindBoxRecords, merged) {
    const errors = [];
    const warnings = [];
    const references = [];
    const usedGenerators = new Map();
    const boxesById = new Map(blindBoxRecords.map(box => [box.Id, box]));
    const enabledSteamSchedules = scheduleRecords.filter(schedule =>
        schedule.IsEnabled === true && schedule.SteamPlaytimeGeneratorItemDefId > 0);
    if (enabledSteamSchedules.length === 0) {
        return { checkedReferenceCount: 0, references, errors, warnings };
    }

    for (const schedule of scheduleRecords.filter(schedule => schedule.IsEnabled === true)) {
        const label = `BlindBoxSchedule ${schedule.Id ?? "<未知>"}`;
        const generatorId = schedule.SteamPlaytimeGeneratorItemDefId;
        if (!Number.isInteger(generatorId) || generatorId < 0 || generatorId >= 1000000) {
            errors.push(`${label}：SteamPlaytimeGeneratorItemDefId 必须是 0 到 999999 之间的整数。`);
            continue;
        }

        const box = boxesById.get(schedule.BlindBoxId);
        if (!box || box.IsEnabled !== true) {
            errors.push(`${label}：引用的 BlindBox ${schedule.BlindBoxId} 不存在或未启用。`);
            continue;
        }
        if (generatorId === 0) {
            if (box.IsPlatformInventoryRequired === true) {
                warnings.push(`${label}：平台库存盲盒 ${box.Id} 没有配置 SteamPlaytimeGeneratorItemDefId；该展示点只能使用本地 Fallback。`);
            }
            continue;
        }

        const previousScheduleId = usedGenerators.get(generatorId);
        if (previousScheduleId) {
            errors.push(`${label}：PlaytimeGenerator ${generatorId} 已被 BlindBoxSchedule ${previousScheduleId} 使用。`);
            continue;
        }
        usedGenerators.set(generatorId, schedule.Id);

        const definition = merged.enabledById.get(generatorId);
        if (!definition) {
            errors.push(`${label}：Steam PlaytimeGenerator ${generatorId} 不存在或未导出。`);
            continue;
        }
        if (definition.schemaItem.type !== "playtimegenerator") {
            errors.push(`${label}：Steam ItemDef ${generatorId} 必须是 Type=PlaytimeGenerator。`);
            continue;
        }
        if (definition.schemaItem.use_drop_limit === true) {
            errors.push(`${label}：PlaytimeGenerator ${generatorId} 已在 SteamItemDef 中配置显式投放上限，不能再被启用 Schedule 引用。`);
            continue;
        }
        if (box.IsPlatformInventoryRequired !== true) {
            errors.push(`${label}：配置了 PlaytimeGenerator，但 BlindBox ${box.Id} 的 IsPlatformInventoryRequired 不是 true。`);
            continue;
        }

        const rewardPoolRecipe = parseFixedBundleRecipe(definition.schemaItem.bundle);
        if (!rewardPoolRecipe || rewardPoolRecipe.size !== 1) {
            errors.push(`${label}：PlaytimeGenerator ${generatorId} 必须只引用一个盲盒奖励池 Generator。`);
            continue;
        }
        const [[rewardPoolItemDefId, rewardPoolQuantity]] = [...rewardPoolRecipe.entries()];
        if (rewardPoolQuantity !== 1) {
            errors.push(`${label}：PlaytimeGenerator ${generatorId} 必须以数量 1 引用奖励池 ${rewardPoolItemDefId}。`);
            continue;
        }
        const rewardPool = merged.enabledById.get(rewardPoolItemDefId);
        if (!rewardPool || rewardPool.schemaItem.type !== "generator") {
            errors.push(`${label}：PlaytimeGenerator ${generatorId} 的目标 ${rewardPoolItemDefId} 必须是已启用的 Generator。`);
            continue;
        }
        if (!Number.isInteger(schedule.StartSeconds) || schedule.StartSeconds < 0) {
            errors.push(`${label}：StartSeconds 必须是非负整数。`);
            continue;
        }
        if (!Number.isInteger(schedule.IntervalSeconds) || schedule.IntervalSeconds <= 0) {
            errors.push(`${label}：IntervalSeconds 必须是正整数。`);
            continue;
        }

        const isLoop = schedule.IsLoopTrack === true;
        if (isLoop && schedule.StartSeconds !== 0) {
            errors.push(`${label}：循环 Schedule 的 StartSeconds 必须为 0。`);
            continue;
        }
        const steamDropIntervalSeconds = schedule.SteamDropIntervalSeconds ?? 0;
        if (!Number.isInteger(steamDropIntervalSeconds) || steamDropIntervalSeconds < 0) {
            errors.push(`${label}：SteamDropIntervalSeconds 必须是非负整数。`);
            continue;
        }
        if (steamDropIntervalSeconds <= 0) {
            errors.push(`${label}：配置了 PlaytimeGenerator 时必须配置正数 SteamDropIntervalSeconds。`);
            continue;
        }

        definition.schemaItem.drop_interval = Math.max(1, Math.ceil(steamDropIntervalSeconds / 60));

        const dropWindowSeconds = schedule.SteamDropWindowSeconds ?? 0;
        const dropMaxPerWindow = schedule.SteamDropMaxPerWindow ?? 0;
        if (!Number.isInteger(dropWindowSeconds) || dropWindowSeconds < 0) {
            errors.push(`${label}：SteamDropWindowSeconds 必须是非负整数。`);
        } else if (!Number.isInteger(dropMaxPerWindow) || dropMaxPerWindow < 0 || dropMaxPerWindow > 10) {
            errors.push(`${label}：SteamDropMaxPerWindow 必须是 0 到 10 之间的整数。`);
        } else if (dropWindowSeconds === 0 && dropMaxPerWindow !== 0) {
            errors.push(`${label}：SteamDropWindowSeconds=0 时 SteamDropMaxPerWindow 必须为 0。`);
        } else if (dropWindowSeconds > 0 && dropMaxPerWindow === 0) {
            errors.push(`${label}：启用 Steam 掉落窗口时 SteamDropMaxPerWindow 必须大于 0。`);
        } else if (!isLoop && (dropWindowSeconds > 0 || dropMaxPerWindow > 0)) {
            errors.push(`${label}：Steam 掉落窗口只用于循环 Schedule。`);
        } else if (dropWindowSeconds > 0) {
            definition.schemaItem.use_drop_window = true;
            definition.schemaItem.drop_window = Math.max(
                1,
                Math.ceil(dropWindowSeconds / 60),
            );
            definition.schemaItem.drop_max_per_window = dropMaxPerWindow;
        } else {
            delete definition.schemaItem.use_drop_window;
            delete definition.schemaItem.drop_window;
            delete definition.schemaItem.drop_max_per_window;
        }
        const dropLimit = getScheduleDropLimit(schedule, label, errors);
        definition.schemaItem.use_drop_limit = dropLimit !== null;
        if (dropLimit !== null) definition.schemaItem.drop_limit = dropLimit;
        else delete definition.schemaItem.drop_limit;

        references.push({
            scheduleId: schedule.Id,
            blindBoxId: box.Id,
            playtimeGeneratorItemDefId: generatorId,
            outputItemDefId: rewardPoolItemDefId,
            dropIntervalMinutes: definition.schemaItem.drop_interval,
            dropWindowMinutes: definition.schemaItem.drop_window ?? null,
            dropMaxPerWindow: definition.schemaItem.drop_max_per_window ?? null,
            dropLimit,
        });
    }

    return {
        checkedReferenceCount: references.length,
        references,
        errors,
        warnings,
    };
}

function buildArtifacts(
    itemDefRecords,
    linkTreeRecords,
    itemRecords = [],
    blindBoxRecords = [],
    blindBoxScheduleRecords = [],
    blindBoxRarityRateRecords = [],
    gameDevelopConfigRecords = [],
    itemDefIdRangeRecords = [],
) {
    const idRangePlan = itemDefIdRangeRecords.length > 0
        ? buildIdRangePlan(itemDefIdRangeRecords)
        : null;
    const itemDefs = validateAndBuildItemDefs(itemDefRecords);
    const gameItems = validateAndBuildGameItems(itemRecords);
    const merged = mergeDefinitions(itemDefs, gameItems);
    const blindBoxes = applyBlindBoxMappings(
        blindBoxRecords,
        blindBoxScheduleRecords,
        merged,
        itemRecords,
        blindBoxRarityRateRecords,
    );
    const playtime = applyPlaytimeMappings(
        blindBoxScheduleRecords,
        gameDevelopConfigRecords,
        blindBoxRecords,
        merged,
    );
    const bundleErrors = validateBundleReferences(merged.definitions, merged.enabledById);
    const linkTree = validateLinkTree(linkTreeRecords, itemDefs, itemRecords);
    const completionReceipts = validateBlindBoxCompletionReceipts(
        blindBoxScheduleRecords,
        itemDefs,
    );
    const idPlanningErrors = validateIdPlanning(
        idRangePlan,
        itemDefRecords,
        itemRecords,
        linkTreeRecords,
        blindBoxRecords,
        blindBoxScheduleRecords,
    );
    return {
        items: merged.items,
        definitions: merged.definitions,
        blindBoxReferences: blindBoxes.references,
        playtimeReferences: playtime.references,
        completionReceiptReferences: completionReceipts.references,
        checkedReferenceCount: linkTree.checkedReferenceCount,
        checkedBlindBoxReferenceCount: blindBoxes.checkedReferenceCount,
        checkedPlaytimeReferenceCount: playtime.checkedReferenceCount,
        checkedCompletionReceiptReferenceCount: completionReceipts.references.length,
        idRangePlan,
        channelReferences: [
            ...linkTreeRecords.filter(record => record.IsEnabled === true).flatMap(record => [
                {
                    label: `LinkTree ${record.Id} 永久回执`,
                    itemDefId: record.SteamReceiptItemDefId,
                    buildChannelMask: record.BuildChannelMask,
                },
                {
                    label: `LinkTree ${record.Id} 领奖 Bundle`,
                    itemDefId: record.SteamClaimBundleItemDefId,
                    buildChannelMask: record.BuildChannelMask,
                },
            ]),
            ...blindBoxes.references.flatMap(reference => [
                { label: `BlindBox ${reference.blindBoxId} 奖励池`, itemDefId: reference.targetItemDefId },
            ]),
            ...playtime.references.map(reference => ({
                label: `BlindBoxSchedule ${reference.scheduleId} PlaytimeGenerator`,
                itemDefId: reference.playtimeGeneratorItemDefId,
            })),
            ...completionReceipts.references.map(reference => ({
                label: `BlindBoxSchedule ${reference.scheduleId} 完成回执`,
                itemDefId: reference.receiptItemDefId,
            })),
        ],
        errors: [
            ...itemDefs.errors,
            ...gameItems.errors,
            ...merged.errors,
            ...blindBoxes.errors,
            ...playtime.errors,
            ...bundleErrors,
            ...linkTree.errors,
            ...completionReceipts.errors,
            ...idPlanningErrors,
        ],
        warnings: [
            ...itemDefs.warnings,
            ...gameItems.warnings,
            ...blindBoxes.warnings,
            ...playtime.warnings,
            ...linkTree.warnings,
        ],
    };
}

function buildChannelArtifact(result, channel) {
    if (!Object.hasOwn(CHANNELS, channel)) throw new Error(`未知渠道：${channel}`);
    const errors = [];
    const plan = result.idRangePlan;
    const definitions = channel === "release" && plan
        ? result.definitions.filter(definition =>
            !idInRange(plan, ID_RANGE_ROWS.playtestOnly, definition.id))
        : result.definitions;
    const enabledById = new Map(definitions.map(definition => [definition.id, definition]));

    if (channel === "release" && plan) {
        for (const reference of result.channelReferences) {
            if (Number.isInteger(reference.buildChannelMask)
                && (reference.buildChannelMask & BUILD_CHANNEL_MASKS.release) === 0) {
                continue;
            }
            if (idInRange(plan, ID_RANGE_ROWS.playtestOnly, reference.itemDefId)) {
                errors.push(`${reference.label} 引用了 Playtest 专用 ItemDef ${reference.itemDefId}，不得进入 Release。`);
            }
        }
    }
    errors.push(...validateBundleReferences(definitions, enabledById));
    return {
        items: definitions.map(definition => definition.schemaItem),
        definitions,
        errors,
    };
}

function writeJson(filePath, value) {
    fs.writeFileSync(filePath, `${JSON.stringify(value, null, 2)}\n`, "utf8");
}

function projectRelative(filePath) {
    return path.relative(PROJECT_ROOT, filePath).replace(/\\/g, "/");
}

function main(argv = process.argv.slice(2)) {
    const options = parseArguments(argv);
    if (!options) return 0;

    const itemDefRecords = readJsonArray(options.itemDefInput, "SteamItemDef JSON");
    const linkTreeRecords = readJsonArray(options.linkTreeInput, "LinkTree JSON");
    const itemRecords = readJsonArray(options.itemInput, "Item JSON");
    const blindBoxRecords = readJsonArray(options.blindBoxInput, "BlindBox JSON");
    const blindBoxScheduleRecords = readJsonArray(options.blindBoxScheduleInput, "BlindBoxSchedule JSON");
    const blindBoxRarityRateRecords = readJsonArray(options.blindBoxRarityRateInput, "BlindBoxRarityRate JSON");
    const gameDevelopConfigRecords = readJsonArray(options.gameDevelopConfigInput, "GameDevelopConfig JSON");
    const itemDefIdRangeRecords = readJsonArray(options.itemDefIdRangeInput, "SteamItemDefIdRange JSON");
    const result = buildArtifacts(
        itemDefRecords,
        linkTreeRecords,
        itemRecords,
        blindBoxRecords,
        blindBoxScheduleRecords,
        blindBoxRarityRateRecords,
        gameDevelopConfigRecords,
        itemDefIdRangeRecords,
    );
    const channelResults = new Map(options.channels.map(channel => [
        channel,
        buildChannelArtifact(result, channel),
    ]));
    const channelErrors = options.channels.flatMap(channel =>
        channelResults.get(channel).errors.map(error => `${channel}：${error}`));
    const allErrors = [...result.errors, ...channelErrors];

    fs.mkdirSync(options.outputRoot, { recursive: true });
    const reportPath = path.join(options.outputRoot, "validation-report.json");
    writeJson(reportPath, {
        sources: {
            steamItemDef: projectRelative(options.itemDefInput),
            linkTree: projectRelative(options.linkTreeInput),
            item: projectRelative(options.itemInput),
            blindBox: projectRelative(options.blindBoxInput),
            blindBoxSchedule: projectRelative(options.blindBoxScheduleInput),
            blindBoxRarityRate: projectRelative(options.blindBoxRarityRateInput),
            gameDevelopConfig: projectRelative(options.gameDevelopConfigInput),
            steamItemDefIdRange: projectRelative(options.itemDefIdRangeInput),
        },
        sourceItemDefCount: itemDefRecords.length,
        sourceGameItemCount: itemRecords.length,
        sourceBlindBoxCount: blindBoxRecords.length,
        sourceBlindBoxScheduleCount: blindBoxScheduleRecords.length,
        sourceBlindBoxRarityRateCount: blindBoxRarityRateRecords.length,
        exportedItemDefCountByChannel: Object.fromEntries(options.channels.map(channel => [
            channel,
            channelResults.get(channel).items.length,
        ])),
        checkedLinkTreeReferenceCount: result.checkedReferenceCount,
        checkedBlindBoxReferenceCount: result.checkedBlindBoxReferenceCount,
        checkedPlaytimeReferenceCount: result.checkedPlaytimeReferenceCount,
        checkedCompletionReceiptReferenceCount: result.checkedCompletionReceiptReferenceCount,
        channels: options.channels.map(channel => ({
            name: channel,
            appid: CHANNELS[channel].appId,
        })),
        errors: allErrors,
        warnings: result.warnings,
    });

    if (allErrors.length) {
        console.error(`生成已停止：发现 ${allErrors.length} 个错误。`);
        for (const error of allErrors) console.error(`- ${error}`);
        console.error(`校验报告：${reportPath}`);
        return 1;
    }

    console.log(
        `校验通过：Playtest ${channelResults.get("playtest")?.items.length ?? "未生成"} 条，`
        + `Release ${channelResults.get("release")?.items.length ?? "未生成"} 条 Steam ItemDef，`
        + `${result.checkedReferenceCount} 条 LinkTree 引用，`
        + `${result.checkedBlindBoxReferenceCount} 条 BlindBox 映射，`
        + `${result.checkedPlaytimeReferenceCount} 条 PlaytimeGenerator 调度，`
        + `${result.checkedCompletionReceiptReferenceCount} 条新手进度回执引用。`,
    );
    for (const channel of options.channels) {
        const configuration = CHANNELS[channel];
        const outputPath = path.join(options.outputRoot, configuration.fileName);
        writeJson(outputPath, {
            appid: configuration.appId,
            items: channelResults.get(channel).items,
        });
        console.log(`- ${channel} (${configuration.appId})：${outputPath}`);
    }
    console.log(`- 校验报告：${reportPath}`);

    if (result.warnings.length) {
        console.warn(`警告：${result.warnings.length} 条。`);
        for (const warning of result.warnings) console.warn(`- ${warning}`);
    }
    return 0;
}

if (require.main === module) {
    try {
        process.exitCode = main();
    } catch (error) {
        console.error(`生成器错误：${error.message}`);
        process.exitCode = 1;
    }
}

module.exports = {
    BUILD_CHANNEL_MASKS,
    CHANNELS,
    buildArtifacts,
    buildChannelArtifact,
    main,
    parseArguments,
};
