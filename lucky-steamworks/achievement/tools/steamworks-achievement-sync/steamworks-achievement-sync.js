/*
 * Lucky Dog Rise - Steamworks Achievement Sync
 *
 * Run this file in the browser console while signed in at:
 * https://partner.steamgames.com/apps/achievements/<app-id>
 *
 * Safety properties:
 * - Only allows the Lucky Dog Rise Playtest and release AppIDs.
 * - Analysis is read-only.
 * - Synchronization requires an explicit confirmation.
 * - Never deletes achievements and never publishes Steamworks changes.
 * - Normal synchronization creates/updates achievement records and saves them automatically.
 * - Icon analysis is read-only and produces a deterministic handoff report.
 * - Refuses to run while an unsaved Steamworks edit row is open.
 */

(() => {
    "use strict";

    const TOOL_ID = "lucky-dog-steamworks-achievement-sync";
    const TOOL_VERSION = "0.6.1";
    const DEFAULT_ICON_ROOT = String.raw`G:\Workspace\godot-project\lucky-dog-pub-2026\lucky-steamworks\achievement\icon`;
    const ALLOWED_APPS = new Map([
        [4972240, "Lucky Dog Rise Playtest"],
        [2583700, "Lucky Dog Rise"],
    ]);

    const DEFAULT_TIMEOUT_MS = 20_000;
    const POLL_INTERVAL_MS = 100;

    if (document.getElementById(TOOL_ID)) {
        document.getElementById(TOOL_ID).scrollIntoView({ block: "nearest" });
        return;
    }

    const appId = readAppId();
    const appName = ALLOWED_APPS.get(appId);
    if (!appName) {
        window.alert(
            `Steamworks 成就同步 ${TOOL_VERSION}\n\n` +
            `当前页面不是允许操作的 Lucky Dog Rise AppID。\n` +
            `检测到的 AppID：${appId || "未知"}`,
        );
        return;
    }

    const state = {
        configFileName: "",
        achievements: [],
        plan: [],
        iconReport: "",
        running: false,
    };

    const panel = createPanel();
    document.body.appendChild(panel.host);
    log(`已在 ${appName}（${appId}）上就绪。请先载入成就 JSON。`);

    function readAppId() {
        const match = window.location.pathname.match(/\/apps\/achievements\/(\d+)/);
        return match ? Number.parseInt(match[1], 10) : 0;
    }

    function createPanel() {
        const host = document.createElement("div");
        host.id = TOOL_ID;
        host.style.cssText = [
            "position:fixed",
            "top:16px",
            "right:16px",
            "z-index:2147483647",
            "width:min(430px,calc(100vw - 32px))",
            "max-height:calc(100vh - 32px)",
        ].join(";");

        const root = host.attachShadow({ mode: "open" });
        root.innerHTML = `
            <style>
                :host { all: initial; }
                * { box-sizing: border-box; }
                .panel {
                    display: flex;
                    flex-direction: column;
                    max-height: calc(100vh - 32px);
                    overflow: hidden;
                    border: 1px solid #46657f;
                    border-radius: 8px;
                    background: #17202a;
                    color: #e8f0f7;
                    box-shadow: 0 12px 36px rgba(0,0,0,.55);
                    font: 13px/1.45 system-ui, -apple-system, "Segoe UI", sans-serif;
                }
                header {
                    display: flex;
                    align-items: center;
                    justify-content: space-between;
                    gap: 12px;
                    padding: 12px 14px;
                    background: #223548;
                    border-bottom: 1px solid #46657f;
                }
                h2 { margin: 0; font-size: 15px; color: #fff; }
                .app { margin-top: 2px; color: #9fc5e8; font-size: 12px; }
                .close {
                    border: 0;
                    background: transparent;
                    color: #c9d7e4;
                    font-size: 20px;
                    cursor: pointer;
                }
                main { overflow: auto; padding: 12px 14px 14px; }
                .field { margin-bottom: 11px; }
                label.title { display: block; margin-bottom: 4px; font-weight: 650; }
                input[type=file] { width: 100%; color: #dbe8f3; }
                input[type=text] {
                    width: 100%;
                    border: 1px solid #46657f;
                    border-radius: 4px;
                    padding: 6px 7px;
                    background: #0d141a;
                    color: #dbe8f3;
                    font: 12px/1.35 ui-monospace, SFMono-Regular, Consolas, monospace;
                }
                .hint { color: #9eb0c0; font-size: 12px; }
                .checks { display: grid; gap: 6px; margin: 10px 0; }
                .checks label { display: flex; align-items: flex-start; gap: 7px; }
                .actions { display: flex; flex-wrap: wrap; gap: 8px; margin: 12px 0; }
                button.action {
                    border: 1px solid #4e78a0;
                    border-radius: 4px;
                    padding: 7px 10px;
                    background: #315b7d;
                    color: white;
                    cursor: pointer;
                    font: inherit;
                }
                button.action.primary { background: #3c7d46; border-color: #63a56a; }
                button.action:disabled { opacity: .45; cursor: default; }
                .summary {
                    display: none;
                    margin: 10px 0;
                    padding: 9px;
                    border: 1px solid #3e566a;
                    border-radius: 5px;
                    background: #111a22;
                }
                .summary.visible { display: block; }
                .summary-grid {
                    display: grid;
                    grid-template-columns: repeat(4, minmax(0, 1fr));
                    gap: 6px;
                    text-align: center;
                }
                .metric { padding: 5px 3px; border-radius: 4px; background: #243240; }
                .metric strong { display: block; font-size: 17px; }
                details { margin-top: 8px; }
                .plan-list { max-height: 180px; overflow: auto; padding-left: 20px; }
                .plan-list li { margin: 3px 0; }
                .create { color: #8fd694; }
                .inspect { color: #ffd27d; }
                .same { color: #a8b7c4; }
                .warning {
                    display: none;
                    margin: 8px 0;
                    padding: 8px;
                    border: 1px solid #a86c29;
                    border-radius: 4px;
                    background: #3a2815;
                    color: #ffd69b;
                }
                .warning.visible { display: block; }
                pre {
                    margin: 8px 0 0;
                    padding: 8px;
                    max-height: 190px;
                    overflow: auto;
                    white-space: pre-wrap;
                    overflow-wrap: anywhere;
                    border: 1px solid #344a5e;
                    border-radius: 4px;
                    background: #0d141a;
                    color: #bcd1e2;
                    font: 11px/1.45 ui-monospace, SFMono-Regular, Consolas, monospace;
                }
                .report-wrap { display: none; margin-top: 10px; }
                .report-wrap.visible { display: block; }
                .report-title { margin-bottom: 5px; font-weight: 650; }
                textarea.icon-report {
                    display: block;
                    width: 100%;
                    min-height: 210px;
                    resize: vertical;
                    border: 1px solid #46657f;
                    border-radius: 4px;
                    padding: 8px;
                    background: #0d141a;
                    color: #dbe8f3;
                    font: 11px/1.45 ui-monospace, SFMono-Regular, Consolas, monospace;
                }
            </style>
            <section class="panel">
                <header>
                    <div>
                        <h2>Steamworks 成就同步 <small>v${TOOL_VERSION}</small></h2>
                        <div class="app">${escapeHtml(appName)} · AppID ${appId}</div>
                    </div>
                    <button class="close" type="button" title="Close">×</button>
                </header>
                <main>
                    <div class="field">
                        <label class="title">1. 成就 JSON</label>
                        <input class="config-file" type="file" accept="application/json,.json">
                        <div class="hint config-status">尚未载入配置。</div>
                    </div>
                    <div class="field">
                        <label class="title">2. 图标根目录（仅写入报表）</label>
                        <input class="icon-root" type="text" value="${escapeHtml(DEFAULT_ICON_ROOT)}">
                        <div class="hint">脚本不会读取或上传图片；此路径只用于生成完整文件路径。</div>
                    </div>
                    <div class="checks">
                        <label><input class="update-structure" type="checkbox" checked> 检查并更新已有成就的结构字段</label>
                        <label><input class="update-token-text" type="checkbox" checked> 检查并更新已有成就的本地化 Token</label>
                        <label><input class="force-icon-report" type="checkbox"> 报表将已有图标也列为重新上传（强制覆盖）</label>
                    </div>
                    <div class="warning"></div>
                    <div class="actions">
                        <button class="action analyze" type="button">分析差异</button>
                        <button class="action primary sync" type="button" disabled>执行成就同步</button>
                        <button class="action copy-report" type="button" disabled>复制图片操作报表</button>
                    </div>
                    <section class="summary">
                        <div class="summary-grid"></div>
                        <details>
                            <summary>操作计划</summary>
                            <ol class="plan-list"></ol>
                        </details>
                    </section>
                    <section class="report-wrap">
                        <div class="report-title">图片操作报表（交给 Codex / Luna）</div>
                        <textarea class="icon-report" readonly></textarea>
                    </section>
                    <pre class="log"></pre>
                </main>
            </section>
        `;

        const elements = {
            host,
            root,
            close: root.querySelector(".close"),
            configInput: root.querySelector(".config-file"),
            configStatus: root.querySelector(".config-status"),
            iconRoot: root.querySelector(".icon-root"),
            updateStructure: root.querySelector(".update-structure"),
            updateTokenText: root.querySelector(".update-token-text"),
            forceIconReport: root.querySelector(".force-icon-report"),
            warning: root.querySelector(".warning"),
            analyze: root.querySelector(".analyze"),
            sync: root.querySelector(".sync"),
            copyReport: root.querySelector(".copy-report"),
            summary: root.querySelector(".summary"),
            summaryGrid: root.querySelector(".summary-grid"),
            planList: root.querySelector(".plan-list"),
            reportWrap: root.querySelector(".report-wrap"),
            iconReport: root.querySelector(".icon-report"),
            log: root.querySelector(".log"),
        };

        elements.close.addEventListener("click", () => {
            if (!state.running || window.confirm("同步仍在执行。确定要关闭面板吗？")) {
                host.remove();
            }
        });
        elements.configInput.addEventListener("change", onConfigSelected);
        elements.analyze.addEventListener("click", analyze);
        elements.sync.addEventListener("click", synchronize);
        elements.copyReport.addEventListener("click", copyIconReport);
        elements.updateStructure.addEventListener("change", invalidatePlan);
        elements.updateTokenText.addEventListener("change", invalidatePlan);
        elements.forceIconReport.addEventListener("change", invalidatePlan);
        elements.iconRoot.addEventListener("input", invalidatePlan);

        return elements;
    }

    async function onConfigSelected(event) {
        invalidatePlan();
        const file = event.target.files?.[0];
        if (!file) {
            state.configFileName = "";
            state.achievements = [];
            panel.configStatus.textContent = "尚未载入配置。";
            return;
        }

        try {
            const parsed = JSON.parse(await file.text());
            const achievements = normalizeConfig(parsed);
            state.configFileName = file.name;
            state.achievements = achievements;
            panel.configStatus.textContent = `${file.name}：${achievements.length} 个成就。`;
            log(`已从 ${file.name} 载入 ${achievements.length} 个成就。`);
        } catch (error) {
            state.configFileName = "";
            state.achievements = [];
            panel.configStatus.textContent = `配置无效：${error.message}`;
            log(`配置错误：${error.message}`, "错误");
        }
    }

    function normalizeConfig(parsed) {
        const rows = Array.isArray(parsed) ? parsed : parsed?.achievements;
        if (!Array.isArray(rows) || rows.length === 0) {
            throw new Error("JSON 应为非空数组，或包含 achievements 数组的对象。");
        }

        const seen = new Set();
        return rows.map((row, index) => normalizeAchievement(row, index, seen));
    }

    function normalizeAchievement(row, index, seen) {
        if (!row || typeof row !== "object" || Array.isArray(row)) {
            throw new Error(`第 ${index + 1} 项必须是对象。`);
        }

        const apiName = requiredString(row.apiName, `Entry ${index + 1}.apiName`);
        if (!/^[A-Za-z0-9_]+$/.test(apiName)) {
            throw new Error(`${apiName}：apiName 只能包含字母、数字和下划线。`);
        }
        if (seen.has(apiName)) {
            throw new Error(`apiName 重复：${apiName}`);
        }
        seen.add(apiName);

        const nameToken = requiredToken(
            row.steamTokens?.name ?? row.nameToken ?? `${apiName}_NAME`,
            `${apiName}.steamTokens.name`,
        );
        const descriptionToken = requiredToken(
            row.steamTokens?.description ?? row.descriptionToken ?? `${apiName}_DESC`,
            `${apiName}.steamTokens.description`,
        );

        const permission = row.permission == null ? 0 : Number(row.permission);
        if (![0, 1, 2].includes(permission)) {
            throw new Error(`${apiName}.permission 必须是 0、1 或 2。`);
        }

        const progressStat = row.progressStat == null ? "-1" : String(row.progressStat);
        const minValue = row.minValue == null ? "0" : String(row.minValue);
        const maxValue = row.maxValue == null ? "0" : String(row.maxValue);

        return {
            apiName,
            nameToken,
            descriptionToken,
            permission: String(permission),
            hidden: Boolean(row.hidden ?? row.isHidden ?? false),
            progressStat,
            minValue,
            maxValue,
            achievedIcon: optionalString(row.achievedIcon),
            unachievedIcon: optionalString(row.unachievedIcon),
        };
    }

    function requiredString(value, fieldName) {
        if (typeof value !== "string" || !value.trim()) {
            throw new Error(`${fieldName} 为必填字段。`);
        }
        return value.trim();
    }

    function requiredToken(value, fieldName) {
        const token = requiredString(value, fieldName);
        if (!/^[A-Za-z][A-Za-z0-9_]*$/.test(token)) {
            throw new Error(`${fieldName} 只能包含字母、数字和下划线，且必须以字母开头。`);
        }
        return token;
    }

    function optionalString(value) {
        return typeof value === "string" && value.trim() ? normalizePath(value.trim()) : "";
    }

    function optionalTrimmedString(value) {
        return typeof value === "string" && value.trim() ? value.trim() : "";
    }

    function normalizePath(value) {
        return value.replaceAll("\\", "/").replace(/^\.\//, "").replace(/^\/+/, "");
    }

    function invalidatePlan() {
        state.plan = [];
        state.iconReport = "";
        panel.summary.classList.remove("visible");
        panel.reportWrap.classList.remove("visible");
        panel.iconReport.value = "";
        panel.sync.disabled = true;
        panel.copyReport.disabled = true;
        clearWarning();
    }

    function analyze() {
        if (state.running) {
            return;
        }
        clearWarning();

        try {
            assertReadyForAnalysis();
            const existing = readExistingRows();
            state.plan = state.achievements.map(achievement => buildPlanItem(achievement, existing));
            state.iconReport = buildIconReport();
            renderPlan();
            renderIconReport();
            panel.sync.disabled = false;
            const iconCounts = countIconReportTasks();
            log(`差异分析完成：配置中共有 ${state.plan.length} 个成就，报表要求上传 ${iconCounts.files} 张图标。`);
        } catch (error) {
            showWarning(error.message);
            log(`差异分析已停止：${error.message}`, "错误");
        }
    }

    function assertReadyForAnalysis() {
        if (!state.achievements.length) {
            throw new Error("请先载入有效的成就 JSON 文件。");
        }
        const table = document.getElementById("achievementTable");
        if (!table) {
            throw new Error("找不到 Steamworks 成就表格。请刷新页面后重试。");
        }
        const draft = table.querySelector("tr.selected");
        if (draft) {
            throw new Error(
                "Steamworks 当前有尚未保存的编辑行。请先保存或取消该行，再执行分析。",
            );
        }
        assertLocalizationTokenView(table);
    }

    function readExistingRows() {
        const map = new Map();
        const table = document.getElementById("achievementTable");
        for (const row of table.querySelectorAll("tr[id^='a']")) {
            if (row.classList.contains("selected")) {
                continue;
            }
            const apiName = row.cells?.[0]?.textContent?.trim();
            if (!apiName || !/^[A-Za-z0-9_]+$/.test(apiName)) {
                continue;
            }
            const textCell = row.cells?.[1];
            const lines = (textCell?.innerText || "")
                .split(/\r?\n/)
                .map(value => value.trim())
                .filter(Boolean);
            map.set(apiName, {
                apiName,
                row,
                rowId: row.id,
                visibleDisplayName: lines[0] || "",
                visibleDescription: lines.slice(1).join("\n"),
                achievedIconUrl: readSteamIconUrl(row.cells?.[4]),
                unachievedIconUrl: readSteamIconUrl(row.cells?.[5]),
            });
        }
        return map;
    }

    function readSteamIconUrl(cell) {
        const image = cell?.querySelector("img");
        return image?.currentSrc || image?.src || "";
    }

    function buildPlanItem(achievement, existing) {
        const current = existing.get(achievement.apiName);
        if (!current) {
            return {
                type: "create",
                achievement,
                message: "Steamworks 中缺失，将新建；图标需首次保存后下次同步上传",
            };
        }

        const visibleTextDiffers = panel.updateTokenText.checked && (
            current.visibleDisplayName !== achievement.nameToken ||
            current.visibleDescription !== achievement.descriptionToken
        );

        if (!panel.updateStructure.checked && !panel.updateTokenText.checked) {
            return {
                type: "same",
                achievement,
                current,
                message: "已存在；所有更新选项均已关闭",
            };
        }

        return {
            type: "inspect",
            achievement,
            current,
            message: visibleTextDiffers
                ? "已存在；本地化 Token 不同"
                : "已存在；将精确检查所选字段",
        };
    }

    function buildIconReportRows() {
        const forceReplace = panel.forceIconReport.checked;
        return state.plan.map(item => {
            const current = item.current || null;
            return {
                apiName: item.achievement.apiName,
                exists: Boolean(current),
                achieved: buildIconReportSlot(
                    item.achievement.achievedIcon,
                    current?.achievedIconUrl || "",
                    forceReplace,
                    Boolean(current),
                ),
                unachieved: buildIconReportSlot(
                    item.achievement.unachievedIcon,
                    current?.unachievedIconUrl || "",
                    forceReplace,
                    Boolean(current),
                ),
            };
        });
    }

    function buildIconReportSlot(configuredPath, currentUrl, forceReplace, achievementExists) {
        if (!configuredPath) {
            return { action: "NOT_CONFIGURED", path: "", currentUrl };
        }
        if (!achievementExists) {
            return { action: "BLOCKED", path: configuredPath, currentUrl: "" };
        }
        if (forceReplace || !currentUrl) {
            return {
                action: "UPLOAD",
                path: configuredPath,
                currentUrl,
                reason: currentUrl ? "强制覆盖模式" : "Steamworks 当前缺失",
            };
        }
        return { action: "KEEP", path: configuredPath, currentUrl };
    }

    function countIconReportTasks(rows = buildIconReportRows()) {
        const counts = { achievements: 0, files: 0, keep: 0, blocked: 0 };
        for (const row of rows) {
            const slots = [row.achieved, row.unachieved];
            const uploadCount = slots.filter(slot => slot.action === "UPLOAD").length;
            if (uploadCount > 0) {
                counts.achievements += 1;
                counts.files += uploadCount;
            }
            counts.keep += slots.filter(slot => slot.action === "KEEP").length;
            counts.blocked += slots.filter(slot => slot.action === "BLOCKED").length;
        }
        return counts;
    }

    function buildIconReport() {
        const rows = buildIconReportRows();
        const counts = countIconReportTasks(rows);
        const root = normalizeIconRoot(panel.iconRoot.value);
        panel.iconRoot.value = root;
        const mode = panel.forceIconReport.checked
            ? "FORCE_REPLACE_CONFIGURED_ICONS"
            : "UPLOAD_MISSING_ICONS_ONLY";
        const lines = [
            "# Steamworks Achievement Icon Handoff Report",
            "REPORT_VERSION: 1",
            `APP: ${appName}`,
            `APP_ID: ${appId}`,
            `SOURCE_JSON: ${state.configFileName || "unknown"}`,
            `MODE: ${mode}`,
            `ICON_ROOT: ${root || "[NOT_SET]"}`,
            `TASK_ACHIEVEMENTS: ${counts.achievements}`,
            `TASK_FILES: ${counts.files}`,
            `KEEP_EXISTING_FILES: ${counts.keep}`,
            `BLOCKED_FILES: ${counts.blocked}`,
            "",
            "## LUNA 操作约束",
            "1. 只处理下面 [UPLOAD TASKS] 中列出的 API 名称，不自行增加、删除或猜测条目。",
            "2. 必须按 API 名称定位成就行；不要依赖页面顺序。",
            "3. 不得修改 API 名称、进度、隐藏状态、英文名称或描述。",
            "4. 每条成就进入 Edit 后，只上传标为 [UPLOAD] 的文件；标为 [KEEP] 的图标不得替换。",
            "5. 等待页面明确显示图片上传成功后，再对该成就点击一次 Save。",
            "6. 遇到缺少成就行、缺少上传控件、文件不存在或 Steamworks 报错时立即停止并回报。",
            "7. 不得删除成就，不得进入发布页，不得发布 Steamworks 更改。",
            "",
            "## 判定说明",
            panel.forceIconReport.checked
                ? "已启用强制覆盖：JSON 中配置的现有图标全部列为 [UPLOAD]。"
                : "默认模式只把 Steamworks 当前没有 img 元素的图标列为 [UPLOAD]。已有图片无法与本地源文件可靠比对，因此列为 [KEEP]。",
            "",
            "## UPLOAD TASKS",
        ];

        const taskRows = rows.filter(row =>
            row.achieved.action === "UPLOAD" || row.unachieved.action === "UPLOAD",
        );
        if (!taskRows.length) {
            lines.push("NONE");
        }
        taskRows.forEach((row, index) => {
            lines.push(`${index + 1}. API_NAME: ${row.apiName}`);
            lines.push(formatIconReportSlot("ACHIEVED", row.achieved, root));
            lines.push(formatIconReportSlot("UNACHIEVED", row.unachieved, root));
            lines.push("   FINAL_ACTION: 两张图标处理完毕并确认无误后，点击该行 Save 一次。", "");
        });

        const blockedRows = rows.filter(row =>
            row.achieved.action === "BLOCKED" || row.unachieved.action === "BLOCKED",
        );
        if (blockedRows.length) {
            lines.push("## BLOCKED — 先创建并保存成就，Luna 不得处理");
            blockedRows.forEach(row => lines.push(`- ${row.apiName}`));
            lines.push("");
        }

        lines.push("## END OF REPORT");
        return lines.join("\n");
    }

    function formatIconReportSlot(label, slot, root) {
        const fullPath = slot.path ? joinWindowsPath(root, slot.path) : "[NOT_CONFIGURED]";
        if (slot.action === "UPLOAD") {
            return `   ${label}: [UPLOAD] ${fullPath} | REASON: ${slot.reason}`;
        }
        if (slot.action === "KEEP") {
            return `   ${label}: [KEEP] Steamworks 已有图片，不得替换。`;
        }
        if (slot.action === "BLOCKED") {
            return `   ${label}: [BLOCKED] 成就尚未创建；预期文件 ${fullPath}`;
        }
        return `   ${label}: [NOT_CONFIGURED] JSON 未配置图标路径。`;
    }

    function joinWindowsPath(root, relativePath) {
        const relative = normalizePath(relativePath).replaceAll("/", "\\");
        return root ? `${root}\\${relative}` : relative;
    }

    function normalizeIconRoot(value) {
        let normalized = String(value || "").trim();
        normalized = normalized.replace(/^["']+|["']+$/g, "").trim();
        normalized = normalized.replaceAll("/", "\\");

        if (normalized.startsWith("\\\\")) {
            normalized = `\\\\${normalized.slice(2).replace(/\\+/g, "\\")}`;
        } else {
            normalized = normalized.replace(/\\+/g, "\\");
        }

        return normalized.replace(/\\+$/, "");
    }

    function renderIconReport() {
        panel.iconReport.value = state.iconReport;
        panel.reportWrap.classList.add("visible");
        panel.copyReport.disabled = !state.iconReport;
    }

    async function copyIconReport() {
        if (!state.iconReport) {
            return;
        }
        try {
            await navigator.clipboard.writeText(state.iconReport);
        } catch {
            panel.iconReport.focus();
            panel.iconReport.select();
            if (!document.execCommand("copy")) {
                showWarning("自动复制失败，请在报表文本框中按 Ctrl+A、Ctrl+C 手动复制。");
                return;
            }
        }
        log("图片操作报表已复制到剪贴板。", "完成");
    }

    function renderPlan() {
        const counts = countPlanTypes();
        panel.summaryGrid.innerHTML = [
            metric("新建", counts.create),
            metric("检查", counts.inspect),
            metric("跳过", counts.same),
            metric("总计", state.plan.length),
        ].join("");
        panel.planList.innerHTML = state.plan.map(item =>
            `<li class="${item.type}"><strong>${escapeHtml(item.achievement.apiName)}</strong> — ${escapeHtml(item.message)}</li>`,
        ).join("");
        panel.summary.classList.add("visible");
    }

    function countPlanTypes() {
        const counts = { create: 0, inspect: 0, same: 0 };
        for (const item of state.plan) {
            counts[item.type] += 1;
        }
        return counts;
    }

    function metric(label, value) {
        return `<div class="metric"><strong>${value}</strong>${escapeHtml(label)}</div>`;
    }

    async function synchronize() {
        if (state.running || !state.plan.length) {
            return;
        }

        clearWarning();
        try {
            assertReadyForAnalysis();
        } catch (error) {
            showWarning(error.message);
            log(`同步被阻止：${error.message}`, "错误");
            return;
        }

        const counts = countPlanTypes();
        const confirmed = window.confirm(
            `确定同步 Steamworks 成就吗？\n\n` +
            `目标：${appName}（${appId}）\n` +
            `新建：${counts.create}\n` +
            `检查/更新：${counts.inspect}\n` +
            `跳过：${counts.same}\n\n` +
            `本次会批量填写结构字段/英文占位文本，并自动点击 Steamworks 的“保存”。` +
            `\n图片不会由本脚本上传；请使用分析后生成的图片操作报表。` +
            `\n\n不会删除成就，也不会发布 Steamworks 更改。`,
        );
        if (!confirmed) {
            log("已取消同步。");
            return;
        }

        setRunning(true);
        let completed = 0;
        let changed = 0;
        try {
            for (const item of state.plan) {
                if (item.type === "same") {
                    completed += 1;
                    log(`[${completed}/${state.plan.length}] 跳过 ${item.achievement.apiName}。`);
                    continue;
                }

                log(`[${completed + 1}/${state.plan.length}] ${item.type === "create" ? "新建" : "检查"} ${item.achievement.apiName}……`);
                const didChange = item.type === "create"
                    ? await createAchievement(item.achievement)
                    : await inspectAndUpdateAchievement(item.achievement);
                if (didChange) {
                    changed += 1;
                }
                completed += 1;
            }

            log(`同步完成：处理 ${completed} 个，改动 ${changed} 个。`, "完成");
            window.alert(
                `成就同步完成。\n\n` +
                `目标：${appName}（${appId}）\n` +
                `已处理：${completed}\n` +
                `有改动：${changed}\n\n` +
                `已自动保存成就条目。图片未上传。Steamworks 更改尚未发布，请人工复核页面。`,
            );
            state.plan = [];
            panel.sync.disabled = true;
            panel.summary.classList.remove("visible");
        } catch (error) {
            const message = `处理到 ${completed}/${state.plan.length} 时停止：${error.message}`;
            showWarning(message);
            log(message, "错误");
            window.alert(
                `${message}\n\n` +
                `请处理问题；如有需要先刷新页面，然后重新“分析差异”。\n` +
                `同步支持重复执行，已经保存的成就不会被重复新建。`,
            );
        } finally {
            setRunning(false);
        }
    }

    async function createAchievement(achievement) {
        assertNoOpenEditRow();
        const addButton = findVisibleNewAchievementButton();
        if (!addButton) {
            throw new Error("找不到可见的“新成就”按钮。");
        }

        addButton.click();
        const editRow = await waitFor(() => document.querySelector("#achievementTable tr.selected"));
        applyFields(editRow, achievement, {
            includeStructure: true,
            includeTokens: true,
            isCreate: true,
        });
        if (achievement.achievedIcon || achievement.unachievedIcon) {
            log(`${achievement.apiName}：先自动保存成就条目；图标请按分析报表另行上传。`, "提示");
        }
        await saveEditRow(editRow, achievement.apiName);
        return true;
    }

    async function inspectAndUpdateAchievement(achievement) {
        assertNoOpenEditRow();
        const current = readExistingRows().get(achievement.apiName);
        if (!current) {
            log(`${achievement.apiName} 在同步过程中消失，将改为新建。`, "警告");
            return createAchievement(achievement);
        }

        const editButton = findEditButton(current.row);
        if (!editButton) {
            throw new Error(`${achievement.apiName}：找不到编辑按钮。`);
        }
        editButton.click();

        const editRow = await waitFor(() => {
            const row = document.getElementById(current.rowId);
            return row?.classList.contains("selected") ? row : null;
        });

        const fieldChanges = applyFields(editRow, achievement, {
            includeStructure: panel.updateStructure.checked,
            includeTokens: panel.updateTokenText.checked,
            isCreate: false,
        });
        if (fieldChanges.length === 0) {
            cancelEditRow(editRow);
            await waitFor(() => !document.getElementById(current.rowId)?.classList.contains("selected"));
            log(`${achievement.apiName}：所选字段已经一致。`);
            return false;
        }

        await saveEditRow(editRow, achievement.apiName, fieldChanges.join("、"));
        return true;
    }

    function applyFields(editRow, achievement, options) {
        const {
            includeStructure,
            includeTokens,
            isCreate,
        } = options;
        const prefix = getAchievementControlPrefix(editRow);
        const changes = [];
        if (includeStructure) {
            setControlValue(document.getElementById(`${prefix}_apiname`), achievement.apiName, "API 名称", changes);
            setControlValue(document.getElementById(`${prefix}_progress`), achievement.progressStat, "进度统计", changes);
            setControlValue(document.getElementById(`${prefix}_minval`), achievement.minValue, "最小值", changes);
            setControlValue(document.getElementById(`${prefix}_maxval`), achievement.maxValue, "最大值", changes);
            setControlValue(document.getElementById(`${prefix}_permission`), achievement.permission, "设置权限", changes);

            const hidden = document.getElementById(`${prefix}_hidden`);
            if (!hidden) {
                throw new Error(`${achievement.apiName}：找不到隐藏复选框。`);
            }
            if (hidden.checked !== achievement.hidden) {
                hidden.checked = achievement.hidden;
                dispatchInputEvents(hidden);
                changes.push("隐藏状态");
            }
        }

        if (isCreate || includeTokens) {
            setControlValue(
                findLocalizationFieldControl(editRow, prefix, "displayname", "显示名称 Token"),
                achievement.nameToken,
                "显示名称 Token",
                changes,
            );
            setControlValue(
                findLocalizationFieldControl(editRow, prefix, "description", "描述 Token"),
                achievement.descriptionToken,
                "描述 Token",
                changes,
            );
        }
        return changes;
    }

    function assertLocalizationTokenView(table) {
        const selects = [...document.querySelectorAll("select")];
        const isTokenOption = option => /本地化字符串|locali[sz]ation strings?/i.test(option?.textContent || "");
        if (selects.some(select => isTokenOption(select.selectedOptions?.[0]))) {
            return;
        }

        const visibleTokens = [...table.querySelectorAll("tr[id^='a']")]
            .map(row => row.cells?.[1]?.innerText || "")
            .filter(Boolean)
            .flatMap(text => text.split(/\r?\n/).map(value => value.trim()))
            .filter(Boolean);
        if (visibleTokens.some(value => /^[A-Za-z][A-Za-z0-9_]*_(?:NAME|DESC)$/.test(value))) {
            return;
        }
        throw new Error("请先把 Steamworks 页面语言切换为“[本地化字符串]”，再分析或同步。该脚本只填写 Token，不直接填写英文文案。");
    }

    function findLocalizationFieldControl(editRow, prefix, fieldName, label) {
        const field = document.getElementById(`${prefix}_${fieldName}`);
        const controls = [...(field?.querySelectorAll('input:not([type="hidden"]):not([disabled]), textarea:not([disabled])') || [])]
            .filter(control => control.offsetParent !== null);
        if (controls.length === 1) {
            return controls[0];
        }
        if (controls.length === 0) {
            throw new Error(`${label}：当前编辑行没有可填写的本地化字符串控件。请确认页面仍处于“[本地化字符串]”视图。`);
        }
        throw new Error(`${label}：找到 ${controls.length} 个可填写控件，无法安全判断目标。请报告此页 DOM 结构后再继续。`);
    }

    function getAchievementControlPrefix(editRow) {
        const apiInput = editRow.querySelector('input[id^="ach"][id$="_apiname"]');
        if (!apiInput) {
            throw new Error("在编辑行中找不到成就 API 名称字段。");
        }
        return apiInput.id.slice(0, -"_apiname".length);
    }

    function setControlValue(control, desiredValue, label, changes) {
        if (!control) {
            throw new Error(`找不到“${label}”控件。`);
        }
        const desired = String(desiredValue);
        if (control instanceof HTMLSelectElement) {
            const optionExists = [...control.options].some(option => option.value === desired);
            if (!optionExists) {
                throw new Error(`当前 AppID 没有“${label}”选项 ${desired}。`);
            }
        }
        if (control.value !== desired) {
            control.value = desired;
            dispatchInputEvents(control);
            changes.push(label);
        }
    }

    function dispatchInputEvents(control) {
        control.dispatchEvent(new Event("input", { bubbles: true }));
        control.dispatchEvent(new Event("change", { bubbles: true }));
    }

    async function saveEditRow(editRow, expectedApiName, changeSummary = "") {
        const actionButtons = getRowActionButtons(editRow);
        const saveButton = actionButtons.find(button => button.value !== "Cancel") || actionButtons[0];
        if (!saveButton) {
            throw new Error(`${expectedApiName}：找不到保存按钮。`);
        }

        saveButton.click();
        await waitFor(() => {
            if (document.getElementById(editRow.id)?.classList.contains("selected")) {
                return null;
            }
            return readExistingRows().get(expectedApiName) || null;
        });
        log(`${expectedApiName}：已自动保存${changeSummary ? `（${changeSummary}）` : ""}。`);
    }

    function cancelEditRow(editRow) {
        const actionButtons = getRowActionButtons(editRow);
        const cancel = actionButtons.find(button => button.value === "Cancel") || actionButtons[0];
        if (!cancel) {
            throw new Error("找不到取消按钮。");
        }
        cancel.click();
    }

    function getRowActionButtons(row) {
        const lastCell = row.cells?.[row.cells.length - 1];
        return lastCell ? [...lastCell.querySelectorAll('input[type="submit"],button')] : [];
    }

    function findEditButton(row) {
        const actionButtons = getRowActionButtons(row);
        return actionButtons.find(button => button.value === "Edit") || actionButtons[0] || null;
    }

    function findVisibleNewAchievementButton() {
        return [...document.querySelectorAll('#achievementTable input[onclick*="PerformNewAchievement"]')]
            .find(isVisible) || null;
    }

    function isVisible(element) {
        const style = window.getComputedStyle(element);
        return style.display !== "none" && style.visibility !== "hidden" && element.getClientRects().length > 0;
    }

    function assertNoOpenEditRow() {
        if (document.querySelector("#achievementTable tr.selected")) {
            throw new Error("Steamworks 当前已有尚未保存的编辑行。");
        }
    }

    function setRunning(running) {
        state.running = running;
        panel.analyze.disabled = running;
        panel.sync.disabled = running || state.plan.length === 0;
        panel.configInput.disabled = running;
        panel.iconRoot.disabled = running;
        panel.updateStructure.disabled = running;
        panel.updateTokenText.disabled = running;
        panel.forceIconReport.disabled = running;
        panel.copyReport.disabled = running || !state.iconReport;
    }

    function showWarning(message) {
        panel.warning.textContent = message;
        panel.warning.classList.add("visible");
    }

    function clearWarning() {
        panel.warning.textContent = "";
        panel.warning.classList.remove("visible");
    }

    function log(message, level = "信息") {
        const timestamp = new Date().toLocaleTimeString();
        panel.log.textContent += `[${timestamp}] [${level}] ${message}\n`;
        panel.log.scrollTop = panel.log.scrollHeight;
        console.log(`[Achievement Sync] [${level}] ${message}`);
    }

    function wait(ms) {
        return new Promise(resolve => window.setTimeout(resolve, ms));
    }

    async function waitFor(predicate, timeoutMs = DEFAULT_TIMEOUT_MS) {
        const started = Date.now();
        while (Date.now() - started < timeoutMs) {
            const value = predicate();
            if (value) {
                return value;
            }
            await wait(POLL_INTERVAL_MS);
        }
        throw new Error(`等待 Steamworks ${Math.round(timeoutMs / 1000)} 秒后超时。`);
    }

    function cssEscape(value) {
        if (window.CSS?.escape) {
            return window.CSS.escape(value);
        }
        return value.replace(/([ #;?%&,.+*~':"!^$[\]()=>|/@])/g, "\\$1");
    }

    function escapeHtml(value) {
        return String(value)
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }
})();
