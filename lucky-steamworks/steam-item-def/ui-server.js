#!/usr/bin/env node
"use strict";

const fs = require("node:fs");
const http = require("node:http");
const path = require("node:path");
const { spawn } = require("node:child_process");

const converter = require("./build-steam-item-defs");

const TOOL_ROOT = __dirname;
const PROJECT_ROOT = path.resolve(TOOL_ROOT, "..", "..");
const UI_ROOT = path.join(TOOL_ROOT, "ui");
const OUTPUT_ROOT = path.join(TOOL_ROOT, "generated");
const ITEM_DEF_INPUT = path.join(PROJECT_ROOT, "lucky-dog-rise", "Data", "Json", "tbsteamitemdef.json");
const LINK_TREE_INPUT = path.join(PROJECT_ROOT, "lucky-dog-rise", "Data", "Json", "tblinktree.json");
const DEFAULT_PORT = 43117;
const DEFAULT_IDLE_TIMEOUT_MS = 10 * 60 * 1000;

const STATIC_FILES = Object.freeze({
    "/": { file: "index.html", type: "text/html; charset=utf-8" },
    "/app.js": { file: "app.js", type: "text/javascript; charset=utf-8" },
    "/styles.css": { file: "styles.css", type: "text/css; charset=utf-8" },
});

function readJsonArray(filePath) {
    const value = JSON.parse(fs.readFileSync(filePath, "utf8"));
    if (!Array.isArray(value)) throw new Error(`${filePath} 的根节点必须是数组。`);
    return value;
}

function relativePath(filePath) {
    return path.relative(PROJECT_ROOT, filePath).replace(/\\/g, "/");
}

function fileInfo(filePath) {
    if (!fs.existsSync(filePath)) return { exists: false, path: relativePath(filePath) };
    const stat = fs.statSync(filePath);
    return {
        exists: true,
        path: relativePath(filePath),
        modifiedAt: stat.mtime.toISOString(),
        modifiedMs: stat.mtimeMs,
    };
}

function loadPreview() {
    const itemDefRecords = readJsonArray(ITEM_DEF_INPUT);
    const linkTreeRecords = readJsonArray(LINK_TREE_INPUT);
    const result = converter.buildArtifacts(itemDefRecords, linkTreeRecords);
    const linkTreesByItemDef = new Map();

    for (const entry of linkTreeRecords) {
        if (!Number.isInteger(entry.SteamPromoItemDefId) || entry.SteamPromoItemDefId <= 0) continue;
        const references = linkTreesByItemDef.get(entry.SteamPromoItemDefId) || [];
        references.push({
            id: entry.Id,
            key: entry.Key,
            rewardType: entry.RewardType,
            rewardItemId: entry.RewardItemId,
            rewardChips: entry.RewardChips,
            claimLimit: entry.ClaimLimit,
            isEnabled: entry.IsEnabled,
        });
        linkTreesByItemDef.set(entry.SteamPromoItemDefId, references);
    }

    const rows = itemDefRecords.map(record => ({
        id: record.Id,
        key: record.Key,
        type: result.items.find(item => item.itemdefid === record.Id)?.type || `unknown:${record.Type}`,
        name: record.Name,
        description: record.Description,
        promoRule: record.PromoRule,
        grantedManually: record.GrantedManually,
        tradable: record.Tradable,
        marketable: record.Marketable,
        gameOnly: record.GameOnly,
        storeHidden: record.StoreHidden,
        autoStack: record.AutoStack,
        bundle: record.Bundle,
        isEnabled: record.IsEnabled,
        linkTrees: linkTreesByItemDef.get(record.Id) || [],
    }));

    const sources = {
        steamItemDef: fileInfo(ITEM_DEF_INPUT),
        linkTree: fileInfo(LINK_TREE_INPUT),
    };
    const latestSourceMs = Math.max(sources.steamItemDef.modifiedMs || 0, sources.linkTree.modifiedMs || 0);
    const channels = Object.entries(converter.CHANNELS).map(([name, configuration]) => {
        const outputPath = path.join(OUTPUT_ROOT, configuration.fileName);
        const output = fileInfo(outputPath);
        return {
            name,
            appid: configuration.appId,
            fileName: configuration.fileName,
            output: {
                ...output,
                current: output.exists && output.modifiedMs >= latestSourceMs,
            },
            schema: {
                appid: configuration.appId,
                items: result.items,
            },
        };
    });

    return {
        ok: result.errors.length === 0,
        stats: {
            sourceItemDefs: itemDefRecords.length,
            exportedItemDefs: result.items.length,
            linkTreeReferences: result.checkedReferenceCount,
            errors: result.errors.length,
            warnings: result.warnings.length,
        },
        sources,
        rows,
        channels,
        errors: result.errors,
        warnings: result.warnings,
        loadedAt: new Date().toISOString(),
    };
}

function sendJson(response, statusCode, value) {
    const body = `${JSON.stringify(value)}\n`;
    response.writeHead(statusCode, {
        "Content-Type": "application/json; charset=utf-8",
        "Cache-Control": "no-store",
        "Content-Length": Buffer.byteLength(body),
    });
    response.end(body);
}

function sendStatic(response, route) {
    const configuration = STATIC_FILES[route];
    if (!configuration) return false;
    const filePath = path.join(UI_ROOT, configuration.file);
    const body = fs.readFileSync(filePath);
    response.writeHead(200, {
        "Content-Type": configuration.type,
        "Cache-Control": "no-store",
        "Content-Length": body.length,
    });
    response.end(body);
    return true;
}

function createServer({ idleTimeoutMs = DEFAULT_IDLE_TIMEOUT_MS } = {}) {
    let server;
    let lastActivityAt = Date.now();
    const idleCheckIntervalMs = Math.min(30000, Math.max(25, idleTimeoutMs));
    const idleTimer = setInterval(() => {
        if (Date.now() - lastActivityAt >= idleTimeoutMs) server.close();
    }, idleCheckIntervalMs);
    idleTimer.unref();

    server = http.createServer((request, response) => {
        lastActivityAt = Date.now();
        const url = new URL(request.url, "http://127.0.0.1");
        try {
            if (request.method === "GET" && sendStatic(response, url.pathname)) return;

            if (request.method === "GET" && url.pathname === "/api/preview") {
                sendJson(response, 200, loadPreview());
                return;
            }

            if (request.method === "GET" && url.pathname === "/api/heartbeat") {
                sendJson(response, 200, { ok: true, idleTimeoutSeconds: idleTimeoutMs / 1000 });
                return;
            }

            if (request.method === "POST" && request.headers["x-steam-itemdef-tool"] !== "1") {
                sendJson(response, 403, { ok: false, message: "拒绝非工具页面发起的写操作。" });
                return;
            }

            if (request.method === "POST" && url.pathname === "/api/generate") {
                const preview = loadPreview();
                if (!preview.ok) {
                    sendJson(response, 422, preview);
                    return;
                }
                const exitCode = converter.main([]);
                if (exitCode !== 0) {
                    sendJson(response, 500, { ok: false, message: "转换器返回失败状态。" });
                    return;
                }
                sendJson(response, 200, { ...loadPreview(), message: "Playtest 与 Release schema 已生成。" });
                return;
            }

            if (request.method === "POST" && url.pathname === "/api/open-output") {
                fs.mkdirSync(OUTPUT_ROOT, { recursive: true });
                const child = spawn("explorer.exe", [OUTPUT_ROOT], {
                    detached: true,
                    stdio: "ignore",
                    windowsHide: true,
                });
                child.unref();
                sendJson(response, 200, { ok: true });
                return;
            }

            if (request.method === "POST" && url.pathname === "/api/shutdown") {
                sendJson(response, 200, { ok: true, message: "工具已关闭。" });
                setTimeout(() => server.close(), 50);
                return;
            }

            sendJson(response, 404, { ok: false, message: "Not found" });
        } catch (error) {
            sendJson(response, 500, { ok: false, message: error.message });
        }
    });
    server.on("close", () => clearInterval(idleTimer));
    return server;
}

function main() {
    const portArgument = Number.parseInt(process.env.STEAM_ITEM_DEF_TOOL_PORT || "", 10);
    const port = Number.isInteger(portArgument) && portArgument > 0 ? portArgument : DEFAULT_PORT;
    const server = createServer();
    server.on("error", error => {
        if (error.code === "EADDRINUSE") {
            console.error(`Steam ItemDef UI 已在运行：http://127.0.0.1:${port}`);
        } else {
            console.error(`Steam ItemDef UI 启动失败：${error.message}`);
        }
        process.exitCode = 1;
    });
    server.listen(port, "127.0.0.1", () => {
        console.log(`Steam ItemDef UI：http://127.0.0.1:${port}`);
    });
}

if (require.main === module) main();

module.exports = {
    DEFAULT_IDLE_TIMEOUT_MS,
    createServer,
    loadPreview,
};
