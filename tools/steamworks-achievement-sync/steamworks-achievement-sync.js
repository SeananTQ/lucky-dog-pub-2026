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
 * - Icon staging uploads images into multiple existing edit rows, but never saves those rows.
 * - Refuses to run while an unsaved Steamworks edit row is open.
 */

(() => {
    "use strict";

    const TOOL_ID = "lucky-dog-steamworks-achievement-sync";
    const TOOL_VERSION = "0.5.1";
    const ALLOWED_APPS = new Map([
        [4972240, "Lucky Dog Rise Playtest"],
        [2583700, "Lucky Dog Rise"],
    ]);

    const DEFAULT_TIMEOUT_MS = 20_000;
    const UPLOAD_TIMEOUT_MS = 90_000;
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
        imageFiles: new Map(),
        plan: [],
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
                        <label class="title">2. 图标文件夹</label>
                        <input class="icon-folder" type="file" accept="image/*" multiple webkitdirectory>
                        <div class="hint icon-status">只有需要上传图标时才必选。</div>
                    </div>
                    <div class="checks">
                        <label><input class="update-structure" type="checkbox" checked> 检查并更新已有成就的结构字段</label>
                        <label><input class="update-english-text" type="checkbox"> 更新已有成就的英语名称与描述</label>
                        <label><input class="replace-icons" type="checkbox"> 批量暂存已有成就的图标（上传后不自动保存）</label>
                    </div>
                    <div class="warning"></div>
                    <div class="actions">
                        <button class="action analyze" type="button">分析差异</button>
                        <button class="action primary sync" type="button" disabled>执行同步</button>
                    </div>
                    <section class="summary">
                        <div class="summary-grid"></div>
                        <details>
                            <summary>操作计划</summary>
                            <ol class="plan-list"></ol>
                        </details>
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
            iconInput: root.querySelector(".icon-folder"),
            iconStatus: root.querySelector(".icon-status"),
            updateStructure: root.querySelector(".update-structure"),
            updateEnglishText: root.querySelector(".update-english-text"),
            replaceIcons: root.querySelector(".replace-icons"),
            warning: root.querySelector(".warning"),
            analyze: root.querySelector(".analyze"),
            sync: root.querySelector(".sync"),
            summary: root.querySelector(".summary"),
            summaryGrid: root.querySelector(".summary-grid"),
            planList: root.querySelector(".plan-list"),
            log: root.querySelector(".log"),
        };

        elements.close.addEventListener("click", () => {
            if (!state.running || window.confirm("同步仍在执行。确定要关闭面板吗？")) {
                host.remove();
            }
        });
        elements.configInput.addEventListener("change", onConfigSelected);
        elements.iconInput.addEventListener("change", onIconFolderSelected);
        elements.analyze.addEventListener("click", analyze);
        elements.sync.addEventListener("click", synchronize);
        elements.updateStructure.addEventListener("change", invalidatePlan);
        elements.updateEnglishText.addEventListener("change", invalidatePlan);
        elements.replaceIcons.addEventListener("change", invalidatePlan);

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

    function onIconFolderSelected(event) {
        invalidatePlan();
        const files = [...(event.target.files || [])];
        state.imageFiles = buildImageFileMap(files);
        panel.iconStatus.textContent = files.length
            ? `已索引 ${files.length} 个图像文件。`
            : "只有需要上传图标时才必选。";
        if (files.length) {
            log(`已索引 ${files.length} 个图像文件。`);
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

        const english = row.localizations?.english || {};
        const displayName = optionalTrimmedString(row.displayName ?? row.name ?? english.name);
        const description = optionalTrimmedString(row.description ?? row.desc ?? english.description);

        const permission = row.permission == null ? 0 : Number(row.permission);
        if (![0, 1, 2].includes(permission)) {
            throw new Error(`${apiName}.permission 必须是 0、1 或 2。`);
        }

        const progressStat = row.progressStat == null ? "-1" : String(row.progressStat);
        const minValue = row.minValue == null ? "0" : String(row.minValue);
        const maxValue = row.maxValue == null ? "0" : String(row.maxValue);

        return {
            apiName,
            displayName,
            description,
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

    function optionalString(value) {
        return typeof value === "string" && value.trim() ? normalizePath(value.trim()) : "";
    }

    function optionalTrimmedString(value) {
        return typeof value === "string" && value.trim() ? value.trim() : "";
    }

    function buildImageFileMap(files) {
        const map = new Map();
        for (const file of files) {
            if (!file.type.startsWith("image/")) {
                continue;
            }
            const relativePath = normalizePath(file.webkitRelativePath || file.name);
            const pathWithoutRoot = relativePath.includes("/")
                ? relativePath.slice(relativePath.indexOf("/") + 1)
                : relativePath;
            addImageAlias(map, relativePath, file);
            addImageAlias(map, pathWithoutRoot, file);
            addImageAlias(map, file.name, file);
        }
        return map;
    }

    function addImageAlias(map, key, file) {
        const normalized = normalizePath(key).toLowerCase();
        if (!map.has(normalized)) {
            map.set(normalized, file);
        }
    }

    function normalizePath(value) {
        return value.replaceAll("\\", "/").replace(/^\.\//, "").replace(/^\/+/, "");
    }

    function invalidatePlan() {
        state.plan = [];
        panel.summary.classList.remove("visible");
        panel.sync.disabled = true;
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
            renderPlan();
            panel.sync.disabled = false;
            log(`差异分析完成：配置中共有 ${state.plan.length} 个成就。`);
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
            });
        }
        return map;
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

        const visibleTextDiffers = panel.updateEnglishText.checked && (
            (achievement.displayName && current.visibleDisplayName !== achievement.displayName) ||
            (achievement.description && current.visibleDescription !== achievement.description)
        );

        if (!panel.updateStructure.checked && !panel.updateEnglishText.checked && !panel.replaceIcons.checked) {
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
                ? "已存在；英语文案不同"
                : "已存在；将精确检查所选字段",
        };
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
            validateRequiredImages();
        } catch (error) {
            showWarning(error.message);
            log(`同步被阻止：${error.message}`, "错误");
            return;
        }

        const counts = countPlanTypes();
        const isIconStaging = panel.replaceIcons.checked;
        if (isIconStaging && counts.create > 0) {
            const message =
                `图标暂存前仍有 ${counts.create} 个成就尚未创建。请先取消勾选“批量暂存已有成就的图标”，` +
                `执行一次常规同步；确认新成就已保存后，再重新分析并暂存图标。`;
            showWarning(message);
            log(`同步被阻止：${message}`, "错误");
            return;
        }

        const iconTargets = state.plan.filter(item =>
            item.type === "inspect" && (item.achievement.achievedIcon || item.achievement.unachievedIcon),
        );
        const confirmed = window.confirm(
            `确定同步 Steamworks 成就吗？\n\n` +
            `目标：${appName}（${appId}）\n` +
            `新建：${counts.create}\n` +
            `检查/更新：${counts.inspect}\n` +
            `跳过：${counts.same}\n\n` +
            (isIconStaging
                ? `本次将打开 ${iconTargets.length} 条已有成就的编辑行，上传图标，但绝不点击“保存”。\n` +
                  `上传后请逐条检查并手动点击 Steamworks 的“保存”。`
                : `本次会批量填写结构字段/英文占位文本，并自动点击 Steamworks 的“保存”。`) +
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
            if (isIconStaging) {
                changed = await stageIconBatch(iconTargets);
                completed = iconTargets.length;
                const message =
                    `已向 ${changed} 条成就暂存图标；这些成就仍处于 Steamworks 编辑状态。` +
                    `请逐条核对图标，然后手动点击各行的“保存”。`;
                showWarning(message);
                log(message, "完成");
                window.alert(
                    `图标已上传，但尚未保存。\n\n` +
                    `已打开并暂存：${changed} 条成就。\n` +
                    `请检查每条编辑行的已达成/未达成图标，并手动点击 Steamworks 的“保存”。\n\n` +
                    `在你保存或取消这些编辑行之前，不要再次执行同步。`,
                );
            } else {
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
                    `已自动保存成就条目。Steamworks 更改尚未发布，请人工复核页面。`,
                );
            }
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

    function validateRequiredImages() {
        const paths = [];
        for (const item of state.plan) {
            const needsReplacementImages = item.type === "inspect" && panel.replaceIcons.checked;
            if (!needsReplacementImages) {
                continue;
            }
            if (item.achievement.achievedIcon) {
                paths.push(item.achievement.achievedIcon);
            }
            if (item.achievement.unachievedIcon) {
                paths.push(item.achievement.unachievedIcon);
            }
        }

        const missing = paths.filter(path => !findImageFile(path));
        if (missing.length) {
            throw new Error(
                `所选图标文件夹缺少 ${missing.length} 个已配置图像：` +
                missing.slice(0, 5).join(", ") +
                (missing.length > 5 ? `，以及另外 ${missing.length - 5} 个` : ""),
            );
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
            includeEnglishText: true,
            isCreate: true,
        });
        if (achievement.achievedIcon || achievement.unachievedIcon) {
            log(`${achievement.apiName}：先自动保存成就条目；图标将在下一次“暂存图标”同步时上传。`, "提示");
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
            includeEnglishText: panel.updateEnglishText.checked,
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

    async function stageIconBatch(items) {
        if (!items.length) {
            log("没有配置图标路径的已有成就；无需暂存图标。", "提示");
            return 0;
        }

        assertNoOpenEditRow();
        const openedRows = [];
        // Steamworks 页面仍有部分旧脚本环境；不用 Array#entries 和 for...of 解构，
        // 避免被其兼容层改写后出现 “.for is not iterable”。
        for (let index = 0; index < items.length; index += 1) {
            const item = items[index];
            const currentRow = document.getElementById(item.current.rowId);
            const editButton = currentRow && findEditButton(currentRow);
            if (!editButton) {
                throw new Error(`${item.achievement.apiName}：找不到编辑按钮；尚未上传任何图标。`);
            }

            log(`[准备 ${index + 1}/${items.length}] 打开 ${item.achievement.apiName} 的编辑行……`);
            editButton.click();
            const editRow = await waitFor(() => {
                const row = document.getElementById(item.current.rowId);
                return row?.classList.contains("selected") ? row : null;
            });
            openedRows.push({ item, editRow });

            const allStillOpen = openedRows.every(entry =>
                document.getElementById(entry.editRow.id)?.classList.contains("selected"),
            );
            if (!allStillOpen) {
                throw new Error(
                    "Steamworks 没有同时保留所有编辑行，已停止，尚未上传图标。" +
                    "请先手动保存或取消当前编辑行，然后再试。",
                );
            }
        }

        let staged = 0;
        for (let index = 0; index < openedRows.length; index += 1) {
            const entry = openedRows[index];
            log(`[上传 ${index + 1}/${openedRows.length}] ${entry.item.achievement.apiName} 的图标……`);
            const uploaded = await uploadConfiguredIcons(entry.editRow, entry.item.achievement);
            if (uploaded === 0) {
                throw new Error(`${entry.item.achievement.apiName}：没有可上传的图标。`);
            }
            staged += 1;
            log(`${entry.item.achievement.apiName}：已暂存 ${uploaded} 个图标，未自动保存。`);
        }
        return staged;
    }

    function applyFields(editRow, achievement, options) {
        const {
            includeStructure,
            includeEnglishText,
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

        if (isCreate || includeEnglishText) {
            const desiredDisplayName = achievement.displayName || (isCreate ? achievement.apiName : "");
            const desiredDescription = achievement.description || (isCreate ? "Pending localization." : "");
            if (desiredDisplayName) {
                setControlValue(
                    document.querySelector(`#${cssEscape(`${prefix}_displayname`)} input[name="english"]`),
                    desiredDisplayName,
                    "英语显示名称",
                    changes,
                );
            }
            if (desiredDescription) {
                setControlValue(
                    document.querySelector(`#${cssEscape(`${prefix}_description`)} input[name="english"]`),
                    desiredDescription,
                    "英语描述",
                    changes,
                );
            }
        }
        return changes;
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

    async function uploadConfiguredIcons(editRow, achievement) {
        let uploaded = 0;
        const uploads = [
            ["achievement", achievement.achievedIcon, "achieved"],
            ["achievement_gray", achievement.unachievedIcon, "unachieved"],
        ];

        for (let uploadIndex = 0; uploadIndex < uploads.length; uploadIndex += 1) {
            const upload = uploads[uploadIndex];
            const requestType = upload[0];
            const path = upload[1];
            const label = upload[2];
            if (!path) {
                continue;
            }
            const file = findImageFile(path);
            if (!file) {
                throw new Error(`${achievement.apiName}：在所选文件夹中找不到图像 ${path}`);
            }
            const form = findUploadForm(editRow, requestType);
            if (!form) {
                throw new Error(`${achievement.apiName}：找不到${label === "achieved" ? "已达成" : "未达成"}图标上传表单。`);
            }
            await uploadImage(form, file, `${achievement.apiName} ${label}`);
            uploaded += 1;
        }
        return uploaded;
    }

    function findImageFile(path) {
        const normalized = normalizePath(path).toLowerCase();
        return state.imageFiles.get(normalized) || state.imageFiles.get(normalized.split("/").pop());
    }

    function findUploadForm(editRow, requestType) {
        return [...editRow.querySelectorAll('form[action*="/images/uploadachievement"]')]
            .find(form => form.querySelector('input[name="requestType"]')?.value === requestType);
    }

    async function uploadImage(form, file, label) {
        const fileInput = form.querySelector('input[type="file"][name="image"]');
        const uploadButton = form.querySelector('input[type="submit"]');
        if (!fileInput || !uploadButton) {
            throw new Error(`${label}：找不到上传控件。`);
        }

        const transfer = new DataTransfer();
        transfer.items.add(file);
        fileInput.files = transfer.files;
        fileInput.dispatchEvent(new Event("change", { bubbles: true }));

        const previewCell = form.closest("td");
        const previewBefore = readImagePreviewSignature(previewCell);
        const iframe = findUploadIframe(form);
        let iframeLoaded = false;
        const onIframeLoad = () => {
            iframeLoaded = true;
        };
        iframe?.addEventListener("load", onIframeLoad, { once: true });

        uploadButton.click();
        try {
            await waitFor(() => {
                const previewAfter = readImagePreviewSignature(previewCell);
                const previewChanged = Boolean(previewAfter) && previewAfter !== previewBefore;
                return previewChanged || iframeLoaded || !document.contains(form);
            }, UPLOAD_TIMEOUT_MS);
            // Promise continuation runs after all listeners for the same load event,
            // allowing Steamworks' upload callback to update the preview first.
            await wait(250);
        } finally {
            iframe?.removeEventListener("load", onIframeLoad);
        }
        log(`${label} 图标已上传：${file.name}。`);
    }

    function readImagePreviewSignature(cell) {
        if (!cell) {
            return "";
        }
        return [...cell.querySelectorAll("img")]
            .map(image => image.currentSrc || image.src || "")
            .filter(Boolean)
            .join("|");
    }

    function findUploadIframe(form) {
        const direct = form.querySelector("iframe");
        if (direct) {
            return direct;
        }
        let sibling = form.nextElementSibling;
        while (sibling && sibling.tagName !== "FORM") {
            if (sibling.tagName === "IFRAME") {
                return sibling;
            }
            sibling = sibling.nextElementSibling;
        }
        return null;
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
        panel.iconInput.disabled = running;
        panel.updateStructure.disabled = running;
        panel.updateEnglishText.disabled = running;
        panel.replaceIcons.disabled = running;
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
