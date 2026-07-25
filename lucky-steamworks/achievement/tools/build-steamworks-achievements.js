#!/usr/bin/env node
"use strict";

/**
 * Builds Steamworks achievement artifacts from Luban's tbachievement.json.
 *
 * Outputs:
 * - ../generated/steamworks-achievements.json  (input for the browser sync script)
 * - ../vdf/steamworks-achievements.english.vdf
 * - ../vdf/steamworks-achievements.schinese.vdf
 * - ../generated/validation-report.json
 */

const fs = require("node:fs");
const path = require("node:path");

const TOOL_DIR = __dirname;
const ACHIEVEMENT_ROOT = path.resolve(TOOL_DIR, "..");
const PROJECT_ROOT = path.resolve(ACHIEVEMENT_ROOT, "..", "..");
const DEFAULT_INPUT = path.join(PROJECT_ROOT, "lucky-dog-rise", "Data", "Json", "tbachievement.json");
const DEFAULT_ICON_ROOT = path.join(ACHIEVEMENT_ROOT, "icon");
const DEFAULT_GENERATED_ROOT = path.join(ACHIEVEMENT_ROOT, "generated");
const DEFAULT_VDF_ROOT = path.join(ACHIEVEMENT_ROOT, "vdf");

function parseArguments(argv) {
    const options = {
        input: DEFAULT_INPUT,
        iconRoot: DEFAULT_ICON_ROOT,
        generatedRoot: DEFAULT_GENERATED_ROOT,
        vdfRoot: DEFAULT_VDF_ROOT,
        checkIcons: true,
    };

    for (let index = 0; index < argv.length; index += 1) {
        const argument = argv[index];
        if (argument === "--help" || argument === "-h") {
            printUsage();
            process.exit(0);
        }
        if (argument === "--skip-icon-check") {
            options.checkIcons = false;
            continue;
        }
        if (!argument.startsWith("--")) {
            throw new Error(`未知参数：${argument}`);
        }
        const value = argv[index + 1];
        if (!value || value.startsWith("--")) {
            throw new Error(`${argument} 需要一个路径参数。`);
        }
        index += 1;
        switch (argument) {
            case "--input": options.input = path.resolve(value); break;
            case "--icon-root": options.iconRoot = path.resolve(value); break;
            case "--generated-root": options.generatedRoot = path.resolve(value); break;
            case "--vdf-root": options.vdfRoot = path.resolve(value); break;
            default: throw new Error(`未知参数：${argument}`);
        }
    }
    return options;
}

function printUsage() {
    console.log(`用法：\n  node tools/build-steamworks-achievements.js [选项]\n\n选项：\n  --input <file>            Luban 导出的 tbachievement.json\n  --icon-root <dir>         图标根目录（默认 achievement/icon）\n  --generated-root <dir>    同步 JSON 与报告输出目录\n  --vdf-root <dir>          VDF 输出目录\n  --skip-icon-check         仅在文案开发期间跳过图标存在性校验\n  -h, --help                显示本帮助\n`);
}

function readJson(filePath) {
    if (!fs.existsSync(filePath)) {
        throw new Error(`找不到 Luban JSON：${filePath}`);
    }
    try {
        const value = JSON.parse(fs.readFileSync(filePath, "utf8"));
        if (!Array.isArray(value)) {
            throw new Error("根节点必须是成就数组。");
        }
        return value;
    } catch (error) {
        throw new Error(`无法读取 ${filePath}：${error.message}`);
    }
}

function requiredString(record, field, errors) {
    const value = record[field];
    if (typeof value !== "string" || !value.trim()) {
        errors.push(`${record.ApiName || "<缺少 ApiName>"}：缺少 ${field}。`);
        return "";
    }
    return value.trim();
}

function escapeVdf(value) {
    return value
        .replace(/\\/g, "\\\\")
        .replace(/\r\n|\r|\n/g, "\\n")
        .replace(/"/g, "\\\"");
}

function buildVdf(language, entries) {
    const lines = ["\"lang\"", "{", `\t\"${language}\"`, "\t{", "\t\t\"Tokens\"", "\t\t{"];
    for (const entry of entries) {
        lines.push(`\t\t\t\"${entry.nameToken}\"\t\"${escapeVdf(entry.name)}\"`);
        lines.push(`\t\t\t\"${entry.descriptionToken}\"\t\"${escapeVdf(entry.description)}\"`);
    }
    lines.push("\t\t}", "\t}", "}", "");
    return lines.join("\n");
}

function relativeIconPath(kind, apiName) {
    return `${kind}/${apiName}.png`;
}

function buildArtifacts(records, options) {
    const errors = [];
    const warnings = [];
    const seenApiNames = new Set();
    const seenAchievementIds = new Set();
    const seenTokens = new Set();
    const achievements = [];

    for (const record of records) {
        const apiName = requiredString(record, "ApiName", errors);
        const achievementId = record.AchievementId;
        if (!Number.isInteger(achievementId)) {
            errors.push(`${apiName || "<缺少 ApiName>"}：AchievementId 必须是整数。`);
        } else if (seenAchievementIds.has(achievementId)) {
            errors.push(`${apiName}：AchievementId ${achievementId} 重复。`);
        } else {
            seenAchievementIds.add(achievementId);
        }
        if (apiName) {
            if (!/^[A-Za-z][A-Za-z0-9_]*$/.test(apiName)) {
                errors.push(`${apiName}：ApiName 只能使用字母、数字和下划线，且必须以字母开头。`);
            }
            if (seenApiNames.has(apiName)) {
                errors.push(`${apiName}：ApiName 重复。`);
            }
            seenApiNames.add(apiName);
        }

        const nameEn = requiredString(record, "SteamNameEn", errors);
        const descriptionEn = requiredString(record, "SteamDescriptionEn", errors);
        const nameZhHans = requiredString(record, "SteamNameZhHans", errors);
        const descriptionZhHans = requiredString(record, "SteamDescriptionZhHans", errors);
        const titleBriefZhHans = typeof record.SteamTitleBriefZhHans === "string"
            ? record.SteamTitleBriefZhHans.trim()
            : "";
        if (!titleBriefZhHans) {
            warnings.push(`${apiName || "<缺少 ApiName>"}：缺少 SteamTitleBriefZhHans（不影响 Steam 上传）。`);
        }
        if (typeof record.IsHidden !== "boolean") {
            errors.push(`${apiName || "<缺少 ApiName>"}：IsHidden 必须是 true 或 false。`);
        }

        const nameToken = `${apiName}_NAME`;
        const descriptionToken = `${apiName}_DESC`;
        for (const token of [nameToken, descriptionToken]) {
            if (seenTokens.has(token)) {
                errors.push(`${apiName}：生成的本地化 Token 重复：${token}`);
            }
            seenTokens.add(token);
        }

        const achievedIcon = relativeIconPath("achieved", apiName);
        const unachievedIcon = relativeIconPath("unachieved", apiName);
        if (options.checkIcons && apiName) {
            for (const iconPath of [achievedIcon, unachievedIcon]) {
                if (!fs.existsSync(path.join(options.iconRoot, iconPath))) {
                    errors.push(`${apiName}：缺少图标 ${iconPath}`);
                }
            }
        }

        achievements.push({
            achievementId,
            apiName,
            hidden: record.IsHidden,
            permission: 0,
            progressStat: -1,
            minValue: 0,
            maxValue: 0,
            achievedIcon,
            unachievedIcon,
            displayName: nameEn,
            description: descriptionEn,
            localizations: {
                english: { name: nameEn, description: descriptionEn },
                schinese: { name: nameZhHans, description: descriptionZhHans },
            },
            steamTokens: { name: nameToken, description: descriptionToken },
            titleBriefZhHans,
        });
    }

    return { achievements, errors, warnings };
}

function writeJson(filePath, value) {
    fs.writeFileSync(filePath, `${JSON.stringify(value, null, 2)}\n`, "utf8");
}

function main() {
    const options = parseArguments(process.argv.slice(2));
    const records = readJson(options.input);
    const { achievements, errors, warnings } = buildArtifacts(records, options);

    fs.mkdirSync(options.generatedRoot, { recursive: true });
    const reportPath = path.join(options.generatedRoot, "validation-report.json");
    const report = {
        source: path.relative(PROJECT_ROOT, options.input).replace(/\\/g, "/"),
        achievementCount: records.length,
        iconCheckEnabled: options.checkIcons,
        errors,
        warnings,
    };
    writeJson(reportPath, report);

    if (errors.length) {
        console.error(`生成已停止：发现 ${errors.length} 个错误。报告：${reportPath}`);
        for (const error of errors) console.error(`- ${error}`);
        process.exitCode = 1;
        return;
    }

    fs.mkdirSync(options.vdfRoot, { recursive: true });
    const syncPath = path.join(options.generatedRoot, "steamworks-achievements.json");
    const englishPath = path.join(options.vdfRoot, "steamworks-achievements.english.vdf");
    const schinesePath = path.join(options.vdfRoot, "steamworks-achievements.schinese.vdf");
    writeJson(syncPath, { schemaVersion: 1, achievements });
    fs.writeFileSync(englishPath, buildVdf("english", achievements.map(entry => ({
        nameToken: entry.steamTokens.name,
        descriptionToken: entry.steamTokens.description,
        name: entry.localizations.english.name,
        description: entry.localizations.english.description,
    }))), "utf8");
    fs.writeFileSync(schinesePath, buildVdf("schinese", achievements.map(entry => ({
        nameToken: entry.steamTokens.name,
        descriptionToken: entry.steamTokens.description,
        name: entry.localizations.schinese.name,
        description: entry.localizations.schinese.description,
    }))), "utf8");

    console.log(`生成完成：${achievements.length} 条成就。`);
    console.log(`- 同步配置：${syncPath}`);
    console.log(`- 英文 VDF：${englishPath}`);
    console.log(`- 简中 VDF：${schinesePath}`);
    console.log(`- 校验报告：${reportPath}`);
    if (warnings.length) {
        console.warn(`警告：${warnings.length} 条。`);
        for (const warning of warnings) console.warn(`- ${warning}`);
    }
}

try {
    main();
} catch (error) {
    console.error(`生成器错误：${error.message}`);
    process.exitCode = 1;
}
