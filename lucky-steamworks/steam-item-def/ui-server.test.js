"use strict";

const assert = require("node:assert/strict");
const test = require("node:test");

const { DEFAULT_IDLE_TIMEOUT_MS, createServer, loadPreview } = require("./ui-server");

test("uses a ten minute idle timeout", () => {
    assert.equal(DEFAULT_IDLE_TIMEOUT_MS, 10 * 60 * 1000);
});

test("loads the real SteamItemDef and LinkTree preview", () => {
    const preview = loadPreview();

    assert.equal(preview.ok, true);
    assert.equal(preview.stats.exportedItemDefs, preview.rows.length);
    assert.equal(preview.stats.linkTreeReferences, 8);
    assert.equal(preview.stats.blindBoxReferences, 3);
    assert.equal(preview.stats.playtimeReferences, 9);
    assert.equal(preview.channels.length, 2);
    const playtest = preview.channels.find(channel => channel.name === "playtest");
    const release = preview.channels.find(channel => channel.name === "release");
    assert.ok(playtest.itemDefIds.includes(401001));
    assert.ok(!release.itemDefIds.includes(401001));
    assert.ok(release.itemDefIds.includes(501001));
    assert.equal(preview.rows.find(row => row.id === 401001).linkTrees.length, 1);
    assert.equal(preview.sources.item.exists, true);
    assert.equal(preview.sources.blindBox.exists, true);
    assert.equal(preview.sources.blindBoxSchedule.exists, true);
    assert.equal(preview.sources.blindBoxRarityRate.exists, true);
    assert.equal(preview.sources.gameDevelopConfig.exists, true);
    assert.equal(preview.sources.steamItemDefIdRange.exists, true);
    assert.equal(preview.rows.find(row => row.id === 101002).source, "Item");
    assert.ok(preview.rows.some(row => row.id === 201001));
    assert.equal(preview.rows.find(row => row.id === 301001).exchange, "201001x1;204001x1");
    assert.ok(preview.rows.find(row => row.id === 301001).bundle.length > 0);
    assert.equal(preview.rows.find(row => row.id === 700001).dropInterval, 1);
    assert.equal(preview.rows.find(row => row.id === 700001).dropLimit, 1);
    assert.equal(preview.rows.find(row => row.id === 700001).playtimeSchedules.length, 1);
});

test("serves preview data and rejects untrusted POST requests", async () => {
    const server = createServer();
    await new Promise((resolve, reject) => {
        server.once("error", reject);
        server.listen(0, "127.0.0.1", resolve);
    });

    try {
        const address = server.address();
        const baseUrl = `http://127.0.0.1:${address.port}`;
        const previewResponse = await fetch(`${baseUrl}/api/preview`);
        const preview = await previewResponse.json();
        assert.equal(previewResponse.status, 200);
        assert.equal(preview.ok, true);

        const heartbeatResponse = await fetch(`${baseUrl}/api/heartbeat`);
        const heartbeat = await heartbeatResponse.json();
        assert.equal(heartbeat.idleTimeoutSeconds, 600);

        const rejectedResponse = await fetch(`${baseUrl}/api/generate`, { method: "POST" });
        assert.equal(rejectedResponse.status, 403);

        const acceptedResponse = await fetch(`${baseUrl}/api/not-found`, {
            method: "POST",
            headers: { "X-Steam-ItemDef-Tool": "1" },
        });
        assert.equal(acceptedResponse.status, 404);
    } finally {
        await new Promise(resolve => server.close(resolve));
    }
});

test("shuts down through the trusted UI endpoint", async () => {
    const server = createServer();
    await new Promise((resolve, reject) => {
        server.once("error", reject);
        server.listen(0, "127.0.0.1", resolve);
    });

    const address = server.address();
    const closed = new Promise(resolve => server.once("close", resolve));
    const response = await fetch(`http://127.0.0.1:${address.port}/api/shutdown`, {
        method: "POST",
        headers: { "X-Steam-ItemDef-Tool": "1" },
    });
    assert.equal(response.status, 200);
    await closed;
});
