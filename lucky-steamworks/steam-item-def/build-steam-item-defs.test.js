"use strict";

const assert = require("node:assert/strict");
const test = require("node:test");

const { buildArtifacts } = require("./build-steam-item-defs");

function receipt(overrides = {}) {
    return {
        Id: 401001,
        Key: "LinkTreeTwitterFollowClaim",
        Type: 1,
        Name: "LinkTree Claim - Twitter Follow",
        Description: "Permanent receipt.",
        PromoRule: "manual",
        GrantedManually: true,
        Tradable: false,
        Marketable: false,
        GameOnly: true,
        StoreHidden: true,
        AutoStack: false,
        Bundle: "",
        IsEnabled: true,
        ...overrides,
    };
}

function linkTree(overrides = {}) {
    return {
        Id: 1001,
        Key: "TwitterFollow",
        IsEnabled: true,
        SteamPromoItemDefId: 401001,
        ClaimLimit: 1,
        ...overrides,
    };
}

test("builds a Steam schema item from a valid permanent receipt", () => {
    const result = buildArtifacts([receipt()], [linkTree()]);

    assert.deepEqual(result.errors, []);
    assert.equal(result.checkedReferenceCount, 1);
    assert.deepEqual(result.items[0], {
        itemdefid: 401001,
        type: "item",
        name: "LinkTree Claim - Twitter Follow",
        description: "Permanent receipt.",
        promo: "manual",
        granted_manually: true,
        tradable: false,
        marketable: false,
        game_only: true,
        store_hidden: true,
        auto_stack: false,
    });
});

test("rejects a missing LinkTree ItemDef reference", () => {
    const result = buildArtifacts([receipt()], [linkTree({ SteamPromoItemDefId: 401999 })]);

    assert.ok(result.errors.some(error => error.includes("401999") && error.includes("不存在")));
});

test("rejects a receipt shared by multiple LinkTree entries", () => {
    const result = buildArtifacts(
        [receipt()],
        [linkTree(), linkTree({ Id: 1002, Key: "SteamCommunity" })],
    );

    assert.ok(result.errors.some(error => error.includes("每个永久回执只能对应一个入口")));
});

test("rejects an unsafe LinkTree receipt configuration", () => {
    const result = buildArtifacts(
        [receipt({ Tradable: true, PromoRule: "owns:2583700" })],
        [linkTree()],
    );

    assert.ok(result.errors.some(error => error.includes("PromoRule=manual")));
    assert.ok(result.errors.some(error => error.includes("不可交易")));
});

test("requires bundle content for bundle and generator definitions", () => {
    const result = buildArtifacts(
        [receipt({ Id: 500001, Key: "EmptyBundle", Type: 2, PromoRule: "", GrantedManually: false })],
        [],
    );

    assert.ok(result.errors.some(error => error.includes("必须填写 Bundle")));
});
