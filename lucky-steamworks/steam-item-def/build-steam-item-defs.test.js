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
        ...overrides,
    };
}

function gameItem(overrides = {}) {
    return {
        Id: 1002,
        Name: "Black and Tan Shiba Inu",
        SteamItemDefId: 101002,
        SteamItemDefType: 1,
        SteamDescription: "",
        SteamGameOnly: true,
        SteamTradable: false,
        SteamMarketable: false,
        SteamAutoStack: false,
        SteamHidden: false,
        SteamDisplayType: "",
        SteamTags: "",
        SteamIconUrl: "",
        SteamIconUrlLarge: "",
        ...overrides,
    };
}

function blindBox(overrides = {}) {
    return {
        Id: 4001,
        Name: "盲盒券",
        IsPlatformInventoryRequired: true,
        IsEnabled: true,
        SteamOpenCostItemDefId: 402001,
        SteamExchangeTargetItemDefId: 403001,
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

test("merges game items and derives a blind box exchange", () => {
    const voucher = receipt({
        Id: 402001,
        Key: "DecorationBlindBoxVoucher",
        Name: "Decoration Blind Box Voucher",
        AutoStack: true,
    });
    const generator = receipt({
        Id: 403001,
        Key: "DecorationBlindBoxTestV1Generator",
        Type: 3,
        Name: "Decoration Blind Box Test V1 Generator",
        PromoRule: "",
        GrantedManually: false,
        Bundle: "101002x1;101003x1",
    });
    const result = buildArtifacts(
        [voucher, generator],
        [],
        [gameItem(), gameItem({ Id: 1003, Name: "Cream Shiba Inu", SteamItemDefId: 101003 })],
        [blindBox()],
    );

    assert.deepEqual(result.errors, []);
    assert.equal(result.items.length, 4);
    assert.equal(result.checkedBlindBoxReferenceCount, 1);
    assert.equal(result.items.find(item => item.itemdefid === 402001).container_contents_generator, 403001);
    assert.equal(result.items.find(item => item.itemdefid === 403001).exchange, "402001x1");
    assert.equal(result.items.find(item => item.itemdefid === 101002).name, "Black and Tan Shiba Inu");
});

test("rejects duplicate ItemDef ids across SteamItemDef and Item", () => {
    const result = buildArtifacts([receipt()], [], [gameItem({ SteamItemDefId: 401001 })], []);

    assert.ok(result.errors.some(error => error.includes("与 SteamItemDef") && error.includes("重复")));
});

test("rejects an incomplete blind box Steam mapping", () => {
    const result = buildArtifacts([], [], [], [blindBox({ SteamExchangeTargetItemDefId: 0 })]);

    assert.ok(result.errors.some(error => error.includes("必须同时填写")));
});

test("warns when a platform blind box has no Steam mapping yet", () => {
    const result = buildArtifacts(
        [],
        [],
        [],
        [blindBox({ SteamOpenCostItemDefId: 0, SteamExchangeTargetItemDefId: 0 })],
    );

    assert.deepEqual(result.errors, []);
    assert.ok(result.warnings.some(warning => warning.includes("本次跳过")));
});
