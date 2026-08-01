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
const DEFAULT_OUTPUT_ROOT = path.join(TOOL_ROOT, "generated");
const AUTO_BUNDLE_MARKER = "@AUTO";
const AUTO_GENERATOR_WEIGHT_SCALE = 1000000;

const CHANNELS = Object.freeze({
    playtest: { appId: 4972240, fileName: "steam-itemdefs.playtest.json" },
    release: { appId: 2583700, fileName: "steam-itemdefs.release.json" },
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

function parseArguments(argv) {
    const options = {
        itemDefInput: DEFAULT_ITEM_DEF_INPUT,
        linkTreeInput: DEFAULT_LINK_TREE_INPUT,
        itemInput: DEFAULT_ITEM_INPUT,
        blindBoxInput: DEFAULT_BLIND_BOX_INPUT,
        blindBoxScheduleInput: DEFAULT_BLIND_BOX_SCHEDULE_INPUT,
        blindBoxRarityRateInput: DEFAULT_BLIND_BOX_RARITY_RATE_INPUT,
        gameDevelopConfigInput: DEFAULT_GAME_DEVELOP_CONFIG_INPUT,
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
            warnings.push(`${label}：IsEnabled=false，本次不会输出。已发布的 ItemDef 不应使用此方式移除。`);
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

function validateLinkTree(records, itemDefs, itemRecords, blindBoxRecords) {
    const errors = [];
    const warnings = [];
    const claimedByReceipt = new Map();
    const claimedByBundle = new Map();
    const itemsById = new Map(itemRecords.map(record => [record.Id, record]));
    const blindBoxesById = new Map(blindBoxRecords.map(record => [record.Id, record]));
    let checkedReferenceCount = 0;

    for (const record of records) {
        const key = typeof record.Key === "string" && record.Key.trim()
            ? record.Key.trim()
            : `LinkTree ${record.Id ?? "<未知>"}`;
        const receiptItemDefId = record.SteamReceiptItemDefId ?? record.SteamPromoItemDefId ?? 0;
        const claimBundleItemDefId = record.SteamClaimBundleItemDefId ?? 0;
        const isEnabled = record.IsEnabled === true;

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
            const box = blindBoxesById.get(record.RewardBlindBoxId);
            if (!box) {
                errors.push(`${key}：RewardBlindBoxId ${record.RewardBlindBoxId} 不存在。`);
            } else if (!Number.isInteger(box.SteamOpenCostItemDefId) || box.SteamOpenCostItemDefId <= 0) {
                errors.push(`${key}：奖励 BlindBox ${record.RewardBlindBoxId} 没有配置 SteamOpenCostItemDefId。`);
            } else {
                expected.set(box.SteamOpenCostItemDefId, (expected.get(box.SteamOpenCostItemDefId) ?? 0) + 1);
            }
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

function applyBlindBoxMappings(records, merged, itemRecords = [], rarityRateRecords = []) {
    const errors = [];
    const warnings = [];
    const references = [];
    const targetRecipes = new Map();
    const containerTargets = new Map();
    const autoGeneratorBoxes = new Map();
    let checkedReferenceCount = 0;

    for (const record of records) {
        const label = `BlindBox ${record.Id ?? "<未知>"} ${typeof record.Name === "string" ? record.Name : ""}`.trim();
        const isEnabled = requiredBoolean(record, "IsEnabled", label, errors);
        const requiresPlatform = requiredBoolean(record, "IsPlatformInventoryRequired", label, errors);
        const inputId = record.SteamOpenCostItemDefId;
        const targetId = record.SteamExchangeTargetItemDefId;

        if (!Number.isInteger(inputId) || inputId < 0 || inputId >= 1000000) {
            errors.push(`${label}：SteamOpenCostItemDefId 必须是 0 到 999999 之间的整数。`);
            continue;
        }
        if (!Number.isInteger(targetId) || targetId < 0 || targetId >= 1000000) {
            errors.push(`${label}：SteamExchangeTargetItemDefId 必须是 0 到 999999 之间的整数。`);
            continue;
        }
        if (!isEnabled) continue;

        if (inputId === 0 && targetId === 0) {
            if (requiresPlatform) {
                warnings.push(`${label}：需要平台库存，但尚未配置 Steam 开箱映射，本次跳过。`);
            }
            continue;
        }
        if (inputId === 0 || targetId === 0) {
            errors.push(`${label}：SteamOpenCostItemDefId 与 SteamExchangeTargetItemDefId 必须同时填写或同时为 0。`);
            continue;
        }

        checkedReferenceCount += 1;
        const input = merged.enabledById.get(inputId);
        const target = merged.enabledById.get(targetId);
        if (!input) {
            errors.push(`${label}：开箱消耗的 Steam ItemDef ${inputId} 不存在或未导出。`);
        } else if (input.schemaItem.type !== "item") {
            errors.push(`${label}：开箱消耗的 Steam ItemDef ${inputId} 必须是 Type=Item。`);
        }
        if (!target) {
            errors.push(`${label}：Steam 交换目标 ${targetId} 不存在或未导出。`);
        } else if (!["generator", "bundle"].includes(target.schemaItem.type)) {
            errors.push(`${label}：Steam 交换目标 ${targetId} 必须是 Generator 或 Bundle。`);
        }
        if (!input || !target) continue;

        if (target.schemaItem.bundle === AUTO_BUNDLE_MARKER) {
            const previousBoxId = autoGeneratorBoxes.get(targetId);
            if (previousBoxId && previousBoxId !== record.Id) {
                errors.push(`${label}：自动 Generator ${targetId} 已由 BlindBox ${previousBoxId} 生成奖池，不能复用。`);
            } else if (!previousBoxId) {
                autoGeneratorBoxes.set(targetId, record.Id);
                const generatedBundle = buildAutoGeneratorBundle(
                    record,
                    itemRecords,
                    rarityRateRecords,
                    merged,
                    errors,
                );
                if (generatedBundle) target.schemaItem.bundle = generatedBundle;
            }
        }

        const recipes = targetRecipes.get(targetId) || new Set();
        recipes.add(`${inputId}x1`);
        targetRecipes.set(targetId, recipes);

        if (target.schemaItem.type === "generator") {
            const previousTarget = containerTargets.get(inputId);
            if (previousTarget && previousTarget !== targetId) {
                errors.push(`${label}：Steam 容器 ${inputId} 已关联 Generator ${previousTarget}，不能再关联 ${targetId}。`);
            } else {
                containerTargets.set(inputId, targetId);
            }
        }

        references.push({
            blindBoxId: record.Id,
            name: record.Name,
            inputItemDefId: inputId,
            targetItemDefId: targetId,
        });
    }

    for (const [targetId, recipes] of targetRecipes) {
        merged.enabledById.get(targetId).schemaItem.exchange = [...recipes].join(";");
    }
    for (const [inputId, targetId] of containerTargets) {
        merged.enabledById.get(inputId).schemaItem.container_contents_generator = targetId;
    }

    return { checkedReferenceCount, references, errors, warnings };
}

function getScheduleDropLimit(schedule, label, errors) {
    if (!Number.isInteger(schedule.MaxGrantCount) || schedule.MaxGrantCount < -1) {
        errors.push(`${label}：MaxGrantCount 必须是 -1 或非负整数。`);
        return null;
    }
    if (schedule.MaxGrantCount >= 0) return schedule.MaxGrantCount;
    if (!Number.isInteger(schedule.EndSeconds)) {
        errors.push(`${label}：EndSeconds 必须是整数。`);
        return null;
    }
    if (schedule.EndSeconds < 0) return null;
    if (schedule.EndSeconds < schedule.StartSeconds) {
        errors.push(`${label}：EndSeconds 不能早于 StartSeconds。`);
        return null;
    }
    if (schedule.IntervalSeconds <= 0) return 1;
    return 1 + Math.floor((schedule.EndSeconds - schedule.StartSeconds) / schedule.IntervalSeconds);
}

function applyPlaytimeMappings(scheduleRecords, configRecords, blindBoxRecords, merged) {
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

    if (configRecords.length !== 1) {
        errors.push(`GameDevelopConfig：生成 Steam 游玩时间掉落时必须且只能有 1 行配置，实际为 ${configRecords.length} 行。`);
        return { checkedReferenceCount: 0, references, errors, warnings };
    }
    const configuration = configRecords[0];
    const durationMultiplier = configuration.BlindBoxWaitDurationMultiplier;
    const leadSeconds = configuration.SteamPlaytimeDropLeadSeconds;
    if (typeof durationMultiplier !== "number" || !Number.isFinite(durationMultiplier) || durationMultiplier <= 0) {
        errors.push("GameDevelopConfig：BlindBoxWaitDurationMultiplier 必须是大于 0 的数字。");
    }
    if (!Number.isInteger(leadSeconds) || leadSeconds < 0) {
        errors.push("GameDevelopConfig：SteamPlaytimeDropLeadSeconds 必须是非负整数。");
    }
    if (errors.length > 0) {
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
            if (box.IsPlatformInventoryRequired === true && box.SteamOpenCostItemDefId > 0) {
                warnings.push(`${label}：平台库存盲盒 ${box.Id} 没有配置 SteamPlaytimeGeneratorItemDefId。`);
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
        if (!Number.isInteger(box.SteamOpenCostItemDefId) || box.SteamOpenCostItemDefId <= 0) {
            errors.push(`${label}：BlindBox ${box.Id} 没有配置有效的 SteamOpenCostItemDefId。`);
            continue;
        }

        const expectedBundle = `${box.SteamOpenCostItemDefId}x1`;
        if (definition.schemaItem.bundle !== expectedBundle) {
            errors.push(`${label}：PlaytimeGenerator ${generatorId} 应当发放 ${expectedBundle}，实际为 ${definition.schemaItem.bundle || "<空>"}。`);
        }
        if (!Number.isInteger(schedule.StartSeconds) || schedule.StartSeconds < 0) {
            errors.push(`${label}：StartSeconds 必须是非负整数。`);
            continue;
        }
        if (!Number.isInteger(schedule.IntervalSeconds)) {
            errors.push(`${label}：IntervalSeconds 必须是整数。`);
            continue;
        }

        const isLoop = schedule.IsLoopTrack === true;
        const baseSeconds = isLoop ? Math.max(0, schedule.IntervalSeconds) : schedule.StartSeconds;
        const steamEligibilitySeconds = isLoop
            ? baseSeconds * durationMultiplier
            : Math.max(0, baseSeconds * durationMultiplier - leadSeconds);
        definition.schemaItem.drop_interval = Math.max(1, Math.ceil(steamEligibilitySeconds / 60));

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
                Math.ceil(dropWindowSeconds * durationMultiplier / 60),
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
            outputItemDefId: box.SteamOpenCostItemDefId,
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
) {
    const itemDefs = validateAndBuildItemDefs(itemDefRecords);
    const gameItems = validateAndBuildGameItems(itemRecords);
    const merged = mergeDefinitions(itemDefs, gameItems);
    const blindBoxes = applyBlindBoxMappings(
        blindBoxRecords,
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
    const linkTree = validateLinkTree(linkTreeRecords, itemDefs, itemRecords, blindBoxRecords);
    return {
        items: merged.items,
        definitions: merged.definitions,
        blindBoxReferences: blindBoxes.references,
        playtimeReferences: playtime.references,
        checkedReferenceCount: linkTree.checkedReferenceCount,
        checkedBlindBoxReferenceCount: blindBoxes.checkedReferenceCount,
        checkedPlaytimeReferenceCount: playtime.checkedReferenceCount,
        errors: [
            ...itemDefs.errors,
            ...gameItems.errors,
            ...merged.errors,
            ...blindBoxes.errors,
            ...playtime.errors,
            ...bundleErrors,
            ...linkTree.errors,
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
    const result = buildArtifacts(
        itemDefRecords,
        linkTreeRecords,
        itemRecords,
        blindBoxRecords,
        blindBoxScheduleRecords,
        blindBoxRarityRateRecords,
        gameDevelopConfigRecords,
    );

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
        },
        sourceItemDefCount: itemDefRecords.length,
        sourceGameItemCount: itemRecords.length,
        sourceBlindBoxCount: blindBoxRecords.length,
        sourceBlindBoxScheduleCount: blindBoxScheduleRecords.length,
        sourceBlindBoxRarityRateCount: blindBoxRarityRateRecords.length,
        exportedItemDefCount: result.items.length,
        checkedLinkTreeReferenceCount: result.checkedReferenceCount,
        checkedBlindBoxReferenceCount: result.checkedBlindBoxReferenceCount,
        checkedPlaytimeReferenceCount: result.checkedPlaytimeReferenceCount,
        channels: options.channels.map(channel => ({
            name: channel,
            appid: CHANNELS[channel].appId,
        })),
        errors: result.errors,
        warnings: result.warnings,
    });

    if (result.errors.length) {
        console.error(`生成已停止：发现 ${result.errors.length} 个错误。`);
        for (const error of result.errors) console.error(`- ${error}`);
        console.error(`校验报告：${reportPath}`);
        return 1;
    }

    console.log(
        `校验通过：${result.items.length} 条 Steam ItemDef，`
        + `${result.checkedReferenceCount} 条 LinkTree 引用，`
        + `${result.checkedBlindBoxReferenceCount} 条 BlindBox 映射，`
        + `${result.checkedPlaytimeReferenceCount} 条 PlaytimeGenerator 调度。`,
    );
    for (const channel of options.channels) {
        const configuration = CHANNELS[channel];
        const outputPath = path.join(options.outputRoot, configuration.fileName);
        writeJson(outputPath, {
            appid: configuration.appId,
            items: result.items,
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
    CHANNELS,
    buildArtifacts,
    main,
    parseArguments,
};
