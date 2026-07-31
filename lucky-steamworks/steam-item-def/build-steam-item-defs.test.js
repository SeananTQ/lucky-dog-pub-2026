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
        SteamUseDropLimit: false,
        SteamDropLimit: 0,
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
        ItemRarity: 4,
        AcquisitionType: 2,
        StandardBoxWeight: 1,
        NewbieBoxWeight: 1,
        RefreshmentBoxWeight: 0,
        EventBoxWeight: 0,
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
        BoxType: 1,
        IsPlatformInventoryRequired: true,
        IsEnabled: true,
        SteamOpenCostItemDefId: 402001,
        SteamExchangeTargetItemDefId: 403001,
        ...overrides,
    };
}

function rarityRate(overrides = {}) {
    return {
        Id: 400104,
        BlindBoxId: 4001,
        Rarity: 4,
        Weight: 100,
        IsEnabled: true,
        ...overrides,
    };
}

function schedule(overrides = {}) {
    return {
        Id: 1001,
        BlindBoxId: 4001,
        IsLoopTrack: false,
        StartSeconds: 30,
        IntervalSeconds: 30,
        EndSeconds: 30,
        MaxGrantCount: 1,
        IsEnabled: true,
        SteamPlaytimeGeneratorItemDefId: 404001,
        SteamDropWindowSeconds: 0,
        SteamDropMaxPerWindow: 0,
        ...overrides,
    };
}

function config(overrides = {}) {
    return {
        BlindBoxWaitDurationMultiplier: 4,
        SteamPlaytimeDropLeadSeconds: 60,
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

test("generates an AUTO blind box bundle with equivalent two-stage probabilities", () => {
    const voucher = receipt({
        Id: 402001,
        Key: "DecorationBlindBoxVoucher",
        PromoRule: "",
        GrantedManually: false,
    });
    const generator = receipt({
        Id: 403001,
        Key: "DecorationBlindBoxGenerator",
        Type: 3,
        PromoRule: "",
        GrantedManually: false,
        Bundle: "@AUTO",
    });
    const items = [
        gameItem({ Id: 1001, SteamItemDefId: 101001, ItemRarity: 4, StandardBoxWeight: 1 }),
        gameItem({ Id: 1002, SteamItemDefId: 101002, ItemRarity: 4, StandardBoxWeight: 3 }),
        gameItem({ Id: 1003, SteamItemDefId: 101003, ItemRarity: 3, StandardBoxWeight: 1 }),
    ];
    const rates = [
        rarityRate({ Rarity: 4, Weight: 75 }),
        rarityRate({ Id: 400103, Rarity: 3, Weight: 25 }),
    ];
    const result = buildArtifacts(
        [voucher, generator],
        [],
        items,
        [blindBox()],
        [],
        rates,
    );

    assert.deepEqual(result.errors, []);
    assert.equal(result.items.find(item => item.itemdefid === 403001).bundle, "101001x3;101002x9;101003x4");
});

test("adds the rarity tag and merges custom Steam tags", () => {
    const result = buildArtifacts([], [], [gameItem({ SteamTags: "color:black" })]);

    assert.deepEqual(result.errors, []);
    assert.equal(result.items[0].tags, "rarity:rare;color:black");
});

test("rejects an AUTO blind box candidate without a Steam ItemDef mapping", () => {
    const voucher = receipt({ Id: 402001, Key: "Voucher", PromoRule: "", GrantedManually: false });
    const generator = receipt({
        Id: 403001,
        Key: "Generator",
        Type: 3,
        PromoRule: "",
        GrantedManually: false,
        Bundle: "@AUTO",
    });
    const result = buildArtifacts(
        [voucher, generator],
        [],
        [gameItem({ SteamItemDefId: 0 })],
        [blindBox()],
        [],
        [rarityRate()],
    );

    assert.ok(result.errors.some(error => error.includes("没有配置 SteamItemDefId")));
});

test("derives playtime drop timing and limits from schedule and config", () => {
    const voucher = receipt({ Id: 402001, Key: "Voucher", PromoRule: "", GrantedManually: false });
    const generator = receipt({
        Id: 403001,
        Key: "Generator",
        Type: 3,
        PromoRule: "",
        GrantedManually: false,
        Bundle: "101002x1",
    });
    const playtime = receipt({
        Id: 404001,
        Key: "Schedule1001PlaytimeDrop",
        Type: 4,
        PromoRule: "",
        GrantedManually: false,
        Bundle: "402001x1",
    });
    const result = buildArtifacts(
        [voucher, generator, playtime],
        [],
        [gameItem()],
        [blindBox()],
        [schedule()],
        [],
        [config()],
    );

    assert.deepEqual(result.errors, []);
    const output = result.items.find(item => item.itemdefid === 404001);
    assert.equal(output.drop_interval, 1);
    assert.equal(output.use_drop_limit, true);
    assert.equal(output.drop_limit, 1);
});

test("derives recurring playtime interval and drop window without a total drop limit", () => {
    const voucher = receipt({ Id: 402001, Key: "Voucher", PromoRule: "", GrantedManually: false });
    const generator = receipt({
        Id: 403001,
        Key: "Generator",
        Type: 3,
        PromoRule: "",
        GrantedManually: false,
        Bundle: "101002x1",
    });
    const playtime = receipt({
        Id: 404001,
        Key: "RecurringDrop",
        Type: 4,
        PromoRule: "",
        GrantedManually: false,
        Bundle: "402001x1",
    });
    const result = buildArtifacts(
        [voucher, generator, playtime],
        [],
        [gameItem()],
        [blindBox()],
        [schedule({
            IsLoopTrack: true,
            StartSeconds: 780,
            IntervalSeconds: 180,
            EndSeconds: -1,
            MaxGrantCount: -1,
            SteamDropWindowSeconds: 360,
            SteamDropMaxPerWindow: 2,
        })],
        [],
        [config({ BlindBoxWaitDurationMultiplier: 10 })],
    );

    assert.deepEqual(result.errors, []);
    const output = result.items.find(item => item.itemdefid === 404001);
    assert.equal(output.drop_interval, 30);
    assert.equal(output.use_drop_window, true);
    assert.equal(output.drop_window, 60);
    assert.equal(output.drop_max_per_window, 2);
    assert.equal(output.use_drop_limit, false);
    assert.equal(Object.hasOwn(output, "drop_limit"), false);
});

test("keeps a retired playtime generator and disables future drops", () => {
    const voucher = receipt({ Id: 402002, Key: "Voucher", PromoRule: "", GrantedManually: false });
    const retired = receipt({
        Id: 404013,
        Key: "LegacyRecurringDrop",
        Type: 4,
        PromoRule: "",
        GrantedManually: false,
        Bundle: "402002x1",
        SteamUseDropLimit: true,
        SteamDropLimit: 0,
    });
    const result = buildArtifacts([voucher, retired], []);

    assert.deepEqual(result.errors, []);
    const output = result.items.find(item => item.itemdefid === 404013);
    assert.equal(output.use_drop_limit, true);
    assert.equal(output.drop_limit, 0);
});

test("rejects an invalid recurring drop window", () => {
    const voucher = receipt({ Id: 402001, Key: "Voucher", PromoRule: "", GrantedManually: false });
    const generator = receipt({
        Id: 405001,
        Key: "RecurringDrop",
        Type: 4,
        PromoRule: "",
        GrantedManually: false,
        Bundle: "402001x1",
    });
    const result = buildArtifacts(
        [voucher, generator],
        [],
        [gameItem()],
        [blindBox()],
        [schedule({
            Id: 2001,
            IsLoopTrack: true,
            EndSeconds: -1,
            MaxGrantCount: -1,
            SteamPlaytimeGeneratorItemDefId: 405001,
            SteamDropWindowSeconds: 360,
            SteamDropMaxPerWindow: 0,
        })],
        [],
        [config()],
    );

    assert.ok(result.errors.some(error => error.includes("SteamDropMaxPerWindow 必须大于 0")));
});

test("rejects a playtime generator that grants the wrong blind box item", () => {
    const voucher = receipt({ Id: 402001, Key: "Voucher", PromoRule: "", GrantedManually: false });
    const wrongVoucher = receipt({ Id: 402002, Key: "WrongVoucher", PromoRule: "", GrantedManually: false });
    const generator = receipt({
        Id: 403001,
        Key: "Generator",
        Type: 3,
        PromoRule: "",
        GrantedManually: false,
        Bundle: "101002x1",
    });
    const playtime = receipt({
        Id: 404001,
        Key: "Schedule1001PlaytimeDrop",
        Type: 4,
        PromoRule: "",
        GrantedManually: false,
        Bundle: "402002x1",
    });
    const result = buildArtifacts(
        [voucher, wrongVoucher, generator, playtime],
        [],
        [gameItem()],
        [blindBox()],
        [schedule()],
        [],
        [config()],
    );

    assert.ok(result.errors.some(error => error.includes("应当发放 402001x1")));
});
