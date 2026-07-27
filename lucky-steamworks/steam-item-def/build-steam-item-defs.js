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
const DEFAULT_OUTPUT_ROOT = path.join(TOOL_ROOT, "generated");

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

function parseArguments(argv) {
    const options = {
        itemDefInput: DEFAULT_ITEM_DEF_INPUT,
        linkTreeInput: DEFAULT_LINK_TREE_INPUT,
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

function validateAndBuildItemDefs(records) {
    const errors = [];
    const warnings = [];
    const seenIds = new Set();
    const seenKeys = new Set();
    const allById = new Map();
    const enabledById = new Map();
    const items = [];

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

        if (grantedManually && !promo) {
            errors.push(`${label}：GrantedManually=true 时 PromoRule 不能为空。`);
        }
        if ([2, 3, 4].includes(record.Type) && !bundle) {
            errors.push(`${label}：${typeName || "复杂物品"} 必须填写 Bundle。`);
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
        items.push(item);
    }

    items.sort((left, right) => left.itemdefid - right.itemdefid);
    return { items, allById, enabledById, errors, warnings };
}

function validateLinkTree(records, itemDefs) {
    const errors = [];
    const warnings = [];
    const claimedByItemDef = new Map();
    let checkedReferenceCount = 0;

    for (const record of records) {
        const key = typeof record.Key === "string" && record.Key.trim()
            ? record.Key.trim()
            : `LinkTree ${record.Id ?? "<未知>"}`;
        const itemDefId = record.SteamPromoItemDefId;
        const isEnabled = record.IsEnabled === true;
        const hasClaim = Number.isInteger(record.ClaimLimit) && record.ClaimLimit > 0;

        if (!Number.isInteger(itemDefId) || itemDefId < 0) {
            errors.push(`${key}：SteamPromoItemDefId 必须是大于等于 0 的整数。`);
            continue;
        }

        if (itemDefId === 0) {
            if (isEnabled && hasClaim) {
                warnings.push(`${key}：启用的限领入口没有配置 SteamPromoItemDefId。`);
            }
            continue;
        }

        checkedReferenceCount += 1;
        const definition = itemDefs.allById.get(itemDefId);
        if (!definition) {
            errors.push(`${key}：引用的 Steam ItemDef ${itemDefId} 不存在。`);
            continue;
        }
        if (!itemDefs.enabledById.has(itemDefId)) {
            errors.push(`${key}：引用的 Steam ItemDef ${itemDefId} 已禁用。`);
        }
        if (claimedByItemDef.has(itemDefId)) {
            errors.push(`${key}：Steam ItemDef ${itemDefId} 已被 ${claimedByItemDef.get(itemDefId)} 使用；每个永久回执只能对应一个入口。`);
        } else {
            claimedByItemDef.set(itemDefId, key);
        }

        if (record.ClaimLimit !== 1) {
            errors.push(`${key}：manual promo 永久回执当前只支持 ClaimLimit=1。`);
        }
        if (definition.Type !== 1) {
            errors.push(`${key}：永久回执 ${itemDefId} 必须是 Type=Item。`);
        }
        if (definition.PromoRule !== "manual") {
            errors.push(`${key}：永久回执 ${itemDefId} 必须配置 PromoRule=manual。`);
        }
        if (definition.GrantedManually !== true) {
            errors.push(`${key}：永久回执 ${itemDefId} 必须配置 GrantedManually=true。`);
        }
        if (definition.Tradable !== false || definition.Marketable !== false) {
            errors.push(`${key}：永久回执 ${itemDefId} 必须不可交易且不可出售。`);
        }
        if (definition.GameOnly !== true || definition.StoreHidden !== true) {
            errors.push(`${key}：永久回执 ${itemDefId} 必须配置 GameOnly=true 且 StoreHidden=true。`);
        }
        if (definition.Bundle) {
            errors.push(`${key}：永久回执 ${itemDefId} 的 Bundle 必须留空。`);
        }
    }

    return { checkedReferenceCount, errors, warnings };
}

function buildArtifacts(itemDefRecords, linkTreeRecords) {
    const itemDefs = validateAndBuildItemDefs(itemDefRecords);
    const linkTree = validateLinkTree(linkTreeRecords, itemDefs);
    return {
        items: itemDefs.items,
        checkedReferenceCount: linkTree.checkedReferenceCount,
        errors: [...itemDefs.errors, ...linkTree.errors],
        warnings: [...itemDefs.warnings, ...linkTree.warnings],
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
    const result = buildArtifacts(itemDefRecords, linkTreeRecords);

    fs.mkdirSync(options.outputRoot, { recursive: true });
    const reportPath = path.join(options.outputRoot, "validation-report.json");
    writeJson(reportPath, {
        sources: {
            steamItemDef: projectRelative(options.itemDefInput),
            linkTree: projectRelative(options.linkTreeInput),
        },
        sourceItemDefCount: itemDefRecords.length,
        exportedItemDefCount: result.items.length,
        checkedLinkTreeReferenceCount: result.checkedReferenceCount,
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

    console.log(`校验通过：${result.items.length} 条 Steam ItemDef，${result.checkedReferenceCount} 条 LinkTree 引用。`);
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
