"use strict";

const state = {
    preview: null,
    channel: "playtest",
    selectedItemDefId: null,
    toastTimer: null,
};

const elements = {
    healthBadge: document.querySelector("#health-badge"),
    reloadButton: document.querySelector("#reload-button"),
    openOutputButton: document.querySelector("#open-output-button"),
    generateButton: document.querySelector("#generate-button"),
    shutdownButton: document.querySelector("#shutdown-button"),
    itemDefSource: document.querySelector("#item-def-source"),
    linkTreeSource: document.querySelector("#link-tree-source"),
    itemSource: document.querySelector("#item-source"),
    blindBoxSource: document.querySelector("#blind-box-source"),
    loadedAt: document.querySelector("#loaded-at"),
    metricDefinitions: document.querySelector("#metric-definitions"),
    metricReferences: document.querySelector("#metric-references"),
    metricErrors: document.querySelector("#metric-errors"),
    metricWarnings: document.querySelector("#metric-warnings"),
    generationMessage: document.querySelector("#generation-message"),
    channelTabs: document.querySelector("#channel-tabs"),
    channelAppid: document.querySelector("#channel-appid"),
    outputState: document.querySelector("#output-state"),
    visibleCount: document.querySelector("#visible-count"),
    tableBody: document.querySelector("#item-table-body"),
    detailTitle: document.querySelector("#detail-title"),
    detailSubtitle: document.querySelector("#detail-subtitle"),
    rewardSummary: document.querySelector("#reward-summary"),
    jsonPreview: document.querySelector("#json-preview"),
    copyJsonButton: document.querySelector("#copy-json-button"),
    validationSummary: document.querySelector("#validation-summary"),
    validationList: document.querySelector("#validation-list"),
    toast: document.querySelector("#toast"),
    closedOverlay: document.querySelector("#closed-overlay"),
};

function formatTime(isoValue) {
    if (!isoValue) return "-";
    return new Intl.DateTimeFormat("zh-CN", {
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit",
        hour12: false,
    }).format(new Date(isoValue));
}

function showToast(message) {
    clearTimeout(state.toastTimer);
    elements.toast.textContent = message;
    elements.toast.classList.add("visible");
    state.toastTimer = setTimeout(() => elements.toast.classList.remove("visible"), 2600);
}

async function request(url, options) {
    const requestOptions = { ...(options || {}) };
    if (requestOptions.method === "POST") {
        requestOptions.headers = {
            ...(requestOptions.headers || {}),
            "X-Steam-ItemDef-Tool": "1",
        };
    }
    const response = await fetch(url, requestOptions);
    const payload = await response.json();
    if (!response.ok) throw new Error(payload.message || `请求失败：${response.status}`);
    return payload;
}

function setBusy(isBusy) {
    elements.reloadButton.disabled = isBusy;
    elements.openOutputButton.disabled = isBusy;
    elements.generateButton.disabled = isBusy || !state.preview?.ok;
}

async function loadPreview({ announce = false } = {}) {
    setBusy(true);
    elements.healthBadge.className = "status-badge status-loading";
    elements.healthBadge.textContent = "读取中";
    elements.generationMessage.textContent = "";
    try {
        state.preview = await request("/api/preview");
        if (!state.preview.channels.some(channel => channel.name === state.channel)) {
            state.channel = state.preview.channels[0]?.name || "playtest";
        }
        ensureSelectedRow();
        render();
        if (announce) showToast("已重新读取 Luban 数据");
    } catch (error) {
        elements.healthBadge.className = "status-badge status-error";
        elements.healthBadge.textContent = "读取失败";
        elements.validationSummary.textContent = error.message;
        elements.validationList.replaceChildren();
        const item = document.createElement("div");
        item.className = "validation-item validation-error";
        item.textContent = error.message;
        elements.validationList.append(item);
    } finally {
        setBusy(false);
    }
}

function render() {
    renderSummary();
    renderChannels();
    renderTable();
    renderDetail();
    renderValidation();
}

function renderSummary() {
    const preview = state.preview;
    elements.healthBadge.className = `status-badge ${preview.ok ? "status-ok" : "status-error"}`;
    elements.healthBadge.textContent = preview.ok ? "校验通过" : "存在错误";
    elements.itemDefSource.textContent = preview.sources.steamItemDef.path;
    elements.itemDefSource.title = preview.sources.steamItemDef.path;
    elements.linkTreeSource.textContent = preview.sources.linkTree.path;
    elements.linkTreeSource.title = preview.sources.linkTree.path;
    elements.itemSource.textContent = preview.sources.item.path;
    elements.itemSource.title = preview.sources.item.path;
    elements.blindBoxSource.textContent = preview.sources.blindBox.path;
    elements.blindBoxSource.title = preview.sources.blindBox.path;
    elements.loadedAt.textContent = formatTime(preview.loadedAt);
    elements.metricDefinitions.textContent = preview.stats.exportedItemDefs;
    elements.metricReferences.textContent = preview.stats.linkTreeReferences
        + preview.stats.blindBoxReferences
        + preview.stats.playtimeReferences;
    elements.metricErrors.textContent = preview.stats.errors;
    elements.metricWarnings.textContent = preview.stats.warnings;
    elements.generateButton.disabled = !preview.ok;
}

function renderChannels() {
    elements.channelTabs.replaceChildren();
    for (const channel of state.preview.channels) {
        const button = document.createElement("button");
        button.type = "button";
        button.role = "tab";
        button.className = `segment ${channel.name === state.channel ? "active" : ""}`;
        button.ariaSelected = String(channel.name === state.channel);
        button.textContent = channel.name === "playtest" ? "Playtest" : "Release";
        button.addEventListener("click", () => {
            state.channel = channel.name;
            ensureSelectedRow();
            renderChannels();
            renderTable();
            renderDetail();
        });
        elements.channelTabs.append(button);
    }

    const channel = currentChannel();
    elements.channelAppid.textContent = channel?.appid ?? "-";
    elements.metricDefinitions.textContent = channel?.schema.items.length ?? 0;
    elements.outputState.className = "output-state";
    if (!channel?.output.exists) {
        elements.outputState.textContent = "尚未生成";
    } else if (channel.output.current) {
        elements.outputState.textContent = `已生成 · ${formatTime(channel.output.modifiedAt)}`;
        elements.outputState.classList.add("output-current");
    } else {
        elements.outputState.textContent = "源数据较新，请重新生成";
        elements.outputState.classList.add("output-stale");
    }
}

function policyTag(text, isGood = true) {
    const span = document.createElement("span");
    span.className = `policy-tag ${isGood ? "policy-good" : "policy-bad"}`;
    span.textContent = text;
    return span;
}

function renderTable() {
    const rows = currentRows();
    elements.tableBody.replaceChildren();
    elements.visibleCount.textContent = `${rows.length} 条`;
    for (const row of rows) {
        const tableRow = document.createElement("tr");
        if (row.id === state.selectedItemDefId) tableRow.classList.add("selected");
        tableRow.tabIndex = 0;
        tableRow.addEventListener("click", () => selectRow(row.id));
        tableRow.addEventListener("keydown", event => {
            if (event.key === "Enter" || event.key === " ") {
                event.preventDefault();
                selectRow(row.id);
            }
        });

        const idCell = document.createElement("td");
        const idCode = document.createElement("code");
        idCode.textContent = row.id;
        idCell.append(idCode);

        const keyCell = document.createElement("td");
        keyCell.className = "key-cell";
        keyCell.textContent = row.key;
        keyCell.title = row.key;

        const typeCell = document.createElement("td");
        typeCell.textContent = row.type;
        const sourceCell = document.createElement("td");
        sourceCell.textContent = row.source;
        const promoCell = document.createElement("td");
        promoCell.textContent = row.promoRule || "-";

        const linkCell = document.createElement("td");
        const references = [
            ...row.linkTrees.map(entry => `LinkTree ${entry.key}`),
            ...row.blindBoxes.map(entry => `BlindBox ${entry.blindBoxId} ${entry.role}`),
            ...row.playtimeSchedules.map(entry => `Schedule ${entry.scheduleId} 游玩掉落`),
        ];
        linkCell.textContent = references.join(", ") || "-";
        linkCell.title = linkCell.textContent;

        const policyCell = document.createElement("td");
        const policies = document.createElement("div");
        policies.className = "policy-list";
        policies.append(policyTag("内部", row.gameOnly));
        policies.append(policyTag("不可交易", !row.tradable));
        policies.append(policyTag("不可出售", !row.marketable));
        policyCell.append(policies);

        tableRow.append(idCell, keyCell, sourceCell, typeCell, promoCell, linkCell, policyCell);
        elements.tableBody.append(tableRow);
    }
}

function selectRow(id) {
    state.selectedItemDefId = id;
    renderTable();
    renderDetail();
}

function currentChannel() {
    return state.preview?.channels.find(channel => channel.name === state.channel) || null;
}

function currentRows() {
    const ids = new Set(currentChannel()?.itemDefIds || []);
    return (state.preview?.rows || []).filter(row => ids.has(row.id));
}

function ensureSelectedRow() {
    const rows = currentRows();
    if (!rows.some(row => row.id === state.selectedItemDefId)) {
        state.selectedItemDefId = rows[0]?.id ?? null;
    }
}

function selectedSchemaItem() {
    return currentChannel()?.schema.items.find(item => item.itemdefid === state.selectedItemDefId) || null;
}

function renderDetail() {
    const row = currentRows().find(entry => entry.id === state.selectedItemDefId);
    const item = selectedSchemaItem();
    if (!row || !item) {
        elements.detailTitle.textContent = "选择一条定义";
        elements.detailSubtitle.textContent = "";
        elements.rewardSummary.textContent = "";
        elements.jsonPreview.textContent = "{}";
        elements.copyJsonButton.disabled = true;
        return;
    }

    elements.detailTitle.textContent = row.name;
    elements.detailSubtitle.textContent = `${row.source} · ${row.key} · ${state.channel}`;
    elements.copyJsonButton.disabled = false;
    const rewards = row.linkTrees.map(entry => {
        if (entry.rewardChips) return `${entry.key}: +${entry.rewardChips} Chips`;
        if (entry.rewardItemId) return `${entry.key}: Item ${entry.rewardItemId}`;
        return `${entry.key}: 无本地奖励`;
    });
    const blindBoxes = row.blindBoxes.map(entry => `BlindBox ${entry.blindBoxId}: ${entry.role}`);
    elements.rewardSummary.textContent = [...rewards, ...blindBoxes].join(" · ") || "未被业务表引用";
    elements.jsonPreview.textContent = JSON.stringify(item, null, 2);
}

function renderValidation() {
    const { errors, warnings } = state.preview;
    elements.validationList.replaceChildren();
    elements.validationSummary.textContent = errors.length || warnings.length
        ? `${errors.length} 个错误，${warnings.length} 个警告`
        : "全部通过";

    if (!errors.length && !warnings.length) {
        const empty = document.createElement("div");
        empty.className = "validation-empty";
        empty.textContent = "未发现配置问题，可以生成 Steam schema。";
        elements.validationList.append(empty);
        return;
    }

    for (const [kind, messages] of [["error", errors], ["warning", warnings]]) {
        for (const message of messages) {
            const item = document.createElement("div");
            item.className = `validation-item validation-${kind}`;
            item.textContent = message;
            elements.validationList.append(item);
        }
    }
}

async function generate() {
    setBusy(true);
    elements.generationMessage.textContent = "正在生成…";
    try {
        state.preview = await request("/api/generate", { method: "POST" });
        elements.generationMessage.textContent = state.preview.message || "生成完成";
        render();
        showToast("Playtest 与 Release schema 已生成");
    } catch (error) {
        elements.generationMessage.textContent = "生成失败";
        showToast(error.message);
    } finally {
        setBusy(false);
    }
}

async function openOutput() {
    try {
        await request("/api/open-output", { method: "POST" });
    } catch (error) {
        showToast(error.message);
    }
}

async function copySelectedJson() {
    const item = selectedSchemaItem();
    if (!item) return;
    try {
        await navigator.clipboard.writeText(JSON.stringify(item, null, 2));
        showToast("已复制当前 ItemDef JSON");
    } catch {
        showToast("浏览器未允许写入剪贴板");
    }
}

async function heartbeat() {
    try {
        await request("/api/heartbeat");
    } catch {
        clearInterval(heartbeatTimer);
    }
}

async function shutdown() {
    elements.shutdownButton.disabled = true;
    try {
        await request("/api/shutdown", { method: "POST" });
        clearInterval(heartbeatTimer);
        elements.closedOverlay.hidden = false;
        document.title = "Steam ItemDef · 已关闭";
    } catch (error) {
        elements.shutdownButton.disabled = false;
        showToast(error.message);
    }
}

elements.reloadButton.addEventListener("click", () => loadPreview({ announce: true }));
elements.generateButton.addEventListener("click", generate);
elements.openOutputButton.addEventListener("click", openOutput);
elements.copyJsonButton.addEventListener("click", copySelectedJson);
elements.shutdownButton.addEventListener("click", shutdown);

loadPreview();
const heartbeatTimer = setInterval(heartbeat, 30000);
