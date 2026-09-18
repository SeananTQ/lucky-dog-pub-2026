"use strict";
const fs = require("node:fs");
const path = require("node:path");
const crypto = require("node:crypto");

function canonical(value) {
    if (Array.isArray(value)) return value.map(canonical);
    if (value && typeof value === "object") return Object.fromEntries(
        Object.keys(value).sort().map(key => [key, canonical(value[key])]));
    return value;
}
function hash(value) {
    return crypto.createHash("sha256").update(JSON.stringify(canonical(value))).digest("hex");
}
function read(file) { return JSON.parse(fs.readFileSync(file, "utf8")); }
function archive(root, names) {
    const files = names.filter(name => fs.existsSync(path.join(root, name)));
    if (!files.length) return null;
    const contents = files.map(name => [name, fs.readFileSync(path.join(root, name))]);
    const digest = crypto.createHash("sha256");
    for (const [name, content] of contents) digest.update(name).update(content);
    const id = digest.digest("hex");
    const folder = path.join(root, "history", id);
    fs.mkdirSync(folder, { recursive: true });
    for (const [name, content] of contents) {
        const target = path.join(folder, name);
        if (!fs.existsSync(target)) fs.writeFileSync(target, content, { flag: "wx" });
    }
    const manifest = path.join(folder, "snapshot.json");
    if (!fs.existsSync(manifest)) fs.writeFileSync(manifest, JSON.stringify({ id,
        createdAt: new Date().toISOString(), files }, null, 2), { flag: "wx" });
    return id;
}
function predict(schema, baseline) {
    if (!baseline) return { known: false, total: schema.items.length };
    if (baseline.appid !== schema.appid) throw new Error("上传基线的 AppID 不匹配。");
    const old = new Map(baseline.items.map(item => [item.itemdefid, item]));
    const added = [], changed = [], unchanged = [];
    for (const item of schema.items) {
        if (!old.has(item.itemdefid)) added.push(item.itemdefid);
        else if (hash(old.get(item.itemdefid)) !== hash(item)) changed.push(item.itemdefid);
        else unchanged.push(item.itemdefid);
        old.delete(item.itemdefid);
    }
    const modified = added.length + changed.length;
    return { known: true, total: schema.items.length, modified, added, changed, unchanged,
        omitted: [...old.keys()],
        message: `Modified ${modified}/${schema.items.length} item definitions. Flushed Econ caches: [由 Steam 决定]` };
}
function status(root, fileName, schema) {
    const baselinePath = path.join(root, "uploaded", fileName);
    const baseline = fs.existsSync(baselinePath) ? read(baselinePath) : null;
    const outputPath = path.join(root, fileName);
    return { prediction: predict(schema, baseline),
        baselineHash: baseline ? hash(baseline) : null,
        generatedHash: fs.existsSync(outputPath) ? hash(read(outputPath)) : null };
}
function confirmUploaded(root, fileName, appid, expectedHash) {
    const schema = read(path.join(root, fileName));
    if (schema.appid !== appid || hash(schema) !== expectedHash)
        throw new Error("生成文件已变化或 AppID 不匹配，请重新读取后确认实际上传的文件。");
    archive(root, [fileName]);
    const folder = path.join(root, "uploaded");
    fs.mkdirSync(folder, { recursive: true });
    // Preserve the previous confirmed baseline independently of generation history.
    archive(folder, [fileName]);
    fs.writeFileSync(path.join(folder, fileName), `${JSON.stringify(schema, null, 2)}\n`);
}
function list(root) {
    const folder = path.join(root, "history");
    if (!fs.existsSync(folder)) return [];
    return fs.readdirSync(folder).map(id => read(path.join(folder, id, "snapshot.json")))
        .sort((a, b) => b.createdAt.localeCompare(a.createdAt));
}
module.exports = { archive, predict, status, confirmUploaded, list, hash };
