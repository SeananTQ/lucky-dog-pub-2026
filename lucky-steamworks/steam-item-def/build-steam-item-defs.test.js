"use strict";

const assert = require("node:assert/strict");
const test = require("node:test");

const { buildArtifacts, buildChannelArtifact } = require("./build-steam-item-defs");

function idRanges() {
    const ranges = {
        1001: [100000, 199999],
        1003: [201000, 229999],
        1006: [301000, 301999],
        1009: [400000, 499999],
        1013: [501000, 501999],
        1033: [500001, 500999],
        1016: [600000, 699999],
        1017: [601000, 601999],
        1021: [700000, 799999],
        1023: [700000, 700999],
        1024: [701000, 701999],
        1028: [1, 99999],
        1030: [700100, 700999],
        1032: [701100, 701999],
    };
    return Object.entries(ranges).map(([Id, [StartItemDefId, EndItemDefId]]) => ({
        Id: Number(Id),
        StartItemDefId,
        EndItemDefId,
        Description: "test",
        Purpose: "test",
        Example: "test",
    }));
}

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
        BuildChannelMask: 14,
        RewardType: 1,
        RewardItemId: 1002,
        RewardChips: 0,
        RewardBlindBoxId: 0,
        SteamReceiptItemDefId: 401001,
        SteamClaimBundleItemDefId: 406001,
        ...overrides,
    };
}

function claimBundle(overrides = {}) {
    return receipt({
        Id: 406001,
        Key: "LinkTreeTwitterFollowRewardBundle",
        Type: 2,
        Name: "LinkTree Reward Bundle - Twitter Follow",
        Bundle: "401001x1;101002x1",
        ...overrides,
    });
}

function gameItem(overrides = {}) {
    return {
        Id: 1002,
        Name: "Black and Tan Shiba Inu",
        ItemRarity: 4,
        AcquisitionType: 2,
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
        Name: "标准盲盒",
        BoxType: 1,
        IsPlatformInventoryRequired: true,
        IsEnabled: true,
        ...overrides,
    };
}

function fallbackBlindBox(overrides = {}) {
    return blindBox({
        Id: 4002,
        Name: "消耗品盲盒",
        BoxType: 3,
        IsPlatformInventoryRequired: false,
        ...overrides,
    });
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

function itemWeight(overrides = {}) {
    return {
        Id: 1,
        BlindBoxId: 4001,
        ItemId: 1002,
        Weight: 1,
        IsEnabled: true,
        ...overrides,
    };
}

function schedule(overrides = {}) {
    return {
        Id: 1001,
        BlindBoxId: 4001,
        FallbackBlindBoxId: 4002,
        IsLoopTrack: false,
        StartSeconds: 30,
        IntervalSeconds: 30,
        EndSeconds: 30,
        MaxGrantCount: 1,
        IsEnabled: true,
        SteamPlaytimeGeneratorItemDefId: 404001,
        SteamDropIntervalSeconds: 120,
        SteamCompletionReceiptItemDefId: 0,
        SteamDropWindowSeconds: 0,
        SteamDropMaxPerWindow: 0,
        CostChipsOverride: 0,
        ...overrides,
    };
}

function config(overrides = {}) {
    return {
        SteamPlaytimeEligibilityLeadSeconds: 60,
        ...overrides,
    };
}

function rewardGenerator(overrides = {}) {
    return receipt({
        Id: 403001,
        Key: "DecorationRewardPool",
        Type: 3,
        Name: "Decoration Reward Pool",
        PromoRule: "",
        GrantedManually: false,
        Bundle: "101002x1",
        ...overrides,
    });
}

function playtimeGenerator(overrides = {}) {
    return receipt({
        Id: 404001,
        Key: "Schedule1001DirectReward",
        Type: 4,
        Name: "Schedule 1001 Direct Reward",
        PromoRule: "",
        GrantedManually: false,
        Bundle: "403001x1",
        ...overrides,
    });
}

test("builds a Steam schema item from a valid permanent receipt", () => {
    const result = buildArtifacts([receipt(), claimBundle()], [linkTree()], [gameItem()]);

    assert.deepEqual(result.errors, []);
    assert.equal(result.checkedReferenceCount, 1);
    assert.deepEqual(result.items.find(item => item.itemdefid === 401001), {
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
    const result = buildArtifacts(
        [receipt(), claimBundle()],
        [linkTree({ SteamReceiptItemDefId: 401999 })],
        [gameItem()],
    );

    assert.ok(result.errors.some(error => error.includes("401999") && error.includes("不存在")));
});

test("rejects a receipt shared by multiple LinkTree entries", () => {
    const result = buildArtifacts(
        [receipt()],
        [linkTree(), linkTree({ Id: 1002, Key: "SteamCommunity", SteamClaimBundleItemDefId: 0 })],
    );

    assert.ok(result.errors.some(error => error.includes("每个永久回执只能对应一个入口")));
});

test("rejects an unsafe LinkTree receipt configuration", () => {
    const result = buildArtifacts(
        [receipt({ Tradable: true, PromoRule: "owns:2583700" }), claimBundle()],
        [linkTree()],
        [gameItem()],
    );

    assert.ok(result.errors.some(error => error.includes("PromoRule=manual")));
    assert.ok(result.errors.some(error => error.includes("不可交易")));
});

test("validates a fixed chips LinkTree bundle containing only its receipt", () => {
    const result = buildArtifacts(
        [receipt(), claimBundle({ Bundle: "401001x1" })],
        [linkTree({ RewardType: 2, RewardItemId: 0, RewardChips: 2500 })],
    );

    assert.deepEqual(result.errors, []);
});

test("rejects a LinkTree bundle with the wrong fixed item reward", () => {
    const result = buildArtifacts(
        [receipt(), claimBundle({ Bundle: "401001x1;101003x1" })],
        [linkTree()],
        [gameItem()],
    );

    assert.ok(result.errors.some(error => error.includes("必须为 401001x1;101002x1")));
});

test("rejects LinkTree blind box rewards until the direct-reward design exists", () => {
    const result = buildArtifacts(
        [
            receipt(),
            claimBundle({ Bundle: "401001x1" }),
        ],
        [linkTree({ RewardType: 4, RewardItemId: 0, RewardBlindBoxId: 4001 })],
        [gameItem()],
        [blindBox(), fallbackBlindBox()],
    );

    assert.ok(result.errors.some(error => error.includes("尚未迁移到直接奖励架构")));
});

test("requires bundle content for bundle and generator definitions", () => {
    const result = buildArtifacts(
        [receipt({ Id: 500001, Key: "EmptyBundle", Type: 2, PromoRule: "", GrantedManually: false })],
        [],
    );

    assert.ok(result.errors.some(error => error.includes("必须填写 Bundle")));
});

test("maps a blind box reward pool through its direct-reward PlaytimeGenerator", () => {
    const result = buildArtifacts(
        [rewardGenerator({ Bundle: "101002x1;101003x1" }), playtimeGenerator()],
        [],
        [gameItem(), gameItem({ Id: 1003, Name: "Cream Shiba Inu", SteamItemDefId: 101003 })],
        [blindBox(), fallbackBlindBox()],
        [schedule()],
        [],
        [config()],
    );

    assert.deepEqual(result.errors, []);
    assert.equal(result.items.length, 4);
    assert.equal(result.checkedBlindBoxReferenceCount, 1);
    assert.equal(result.items.find(item => item.itemdefid === 404001).bundle, "403001x1");
    assert.equal(Object.hasOwn(result.items.find(item => item.itemdefid === 403001), "exchange"), false);
    assert.equal(result.items.find(item => item.itemdefid === 101002).name, "Black and Tan Shiba Inu");
});

test("rejects duplicate ItemDef ids across SteamItemDef and Item", () => {
    const result = buildArtifacts([receipt()], [], [gameItem({ SteamItemDefId: 401001 })], []);

    assert.ok(result.errors.some(error => error.includes("与 SteamItemDef") && error.includes("重复")));
});

test("rejects a direct-reward PlaytimeGenerator that does not target a Generator", () => {
    const result = buildArtifacts(
        [playtimeGenerator({ Bundle: "401001x1" }), receipt()],
        [],
        [],
        [blindBox(), fallbackBlindBox()],
        [schedule()],
        [],
        [config()],
    );

    assert.ok(result.errors.some(error => error.includes("必须是 Type=Generator")));
});

test("rejects a non-integer CostChipsOverride", () => {
    const result = buildArtifacts(
        [],
        [],
        [],
        [blindBox(), fallbackBlindBox()],
        [schedule({ SteamPlaytimeGeneratorItemDefId: 0, CostChipsOverride: null })],
    );

    assert.ok(result.errors.some(error => error.includes("CostChipsOverride 必须是整数")));
});

test("generates an AUTO blind box bundle with equivalent two-stage probabilities", () => {
    const generator = rewardGenerator({ Bundle: "@AUTO" });
    const items = [
        gameItem({ Id: 1001, SteamItemDefId: 101001, ItemRarity: 4 }),
        gameItem({ Id: 1002, SteamItemDefId: 101002, ItemRarity: 4 }),
        gameItem({ Id: 1003, SteamItemDefId: 101003, ItemRarity: 3 }),
    ];
    const rates = [
        rarityRate({ Rarity: 4, Weight: 75 }),
        rarityRate({ Id: 400103, Rarity: 3, Weight: 25 }),
    ];
    const result = buildArtifacts(
        [generator, playtimeGenerator()],
        [],
        items,
        [blindBox(), fallbackBlindBox()],
        [schedule()],
        rates,
        [config()],
        [],
        [
            itemWeight({ Id: 1, ItemId: 1001, Weight: 1 }),
            itemWeight({ Id: 2, ItemId: 1002, Weight: 3 }),
            itemWeight({ Id: 3, ItemId: 1003, Weight: 1 }),
        ],
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
    const generator = rewardGenerator({ Bundle: "@AUTO" });
    const result = buildArtifacts(
        [generator, playtimeGenerator()],
        [],
        [gameItem({ SteamItemDefId: 0 })],
        [blindBox()],
        [schedule()],
        [rarityRate()],
        [config()],
        [],
        [itemWeight()],
    );

    assert.ok(result.errors.some(error => error.includes("没有配置 SteamItemDefId")));
});

test("rejects duplicate BlindBoxItemWeight rows", () => {
    const result = buildArtifacts(
        [], [], [gameItem()], [blindBox()], [], [], [], [],
        [itemWeight(), itemWeight({ Id: 2 })],
    );

    assert.ok(result.errors.some(error => error.includes("存在重复行")));
});

test("rejects a BlindBoxItemWeight acquisition mismatch", () => {
    const result = buildArtifacts(
        [], [], [gameItem({ AcquisitionType: 3 })], [blindBox()], [], [], [], [],
        [itemWeight()],
    );

    assert.ok(result.errors.some(error => error.includes("AcquisitionType") && error.includes("不匹配")));
});

test("uses the schedule's explicit real-second playtime interval", () => {
    const result = buildArtifacts(
        [rewardGenerator(), playtimeGenerator()],
        [],
        [gameItem()],
        [blindBox(), fallbackBlindBox()],
        [schedule()],
        [],
        [config()],
    );

    assert.deepEqual(result.errors, []);
    const output = result.items.find(item => item.itemdefid === 404001);
    assert.equal(output.drop_interval, 2);
    assert.equal(output.use_drop_limit, true);
    assert.equal(output.drop_limit, 1);
});

test("keeps one-time generator intervals independent of schedule position", () => {
    const result = buildArtifacts(
        [
            rewardGenerator(),
            playtimeGenerator(),
            playtimeGenerator({ Id: 404002, Key: "Schedule1002DirectReward" }),
        ],
        [],
        [gameItem()],
        [blindBox(), fallbackBlindBox()],
        [
            schedule({ StartSeconds: 600, IntervalSeconds: 600, SteamDropIntervalSeconds: 120 }),
            schedule({
                Id: 1002,
                StartSeconds: 1200,
                IntervalSeconds: 600,
                SteamPlaytimeGeneratorItemDefId: 404002,
                SteamDropIntervalSeconds: 300,
            }),
        ],
        [],
        [config()],
    );

    assert.deepEqual(result.errors, []);
    assert.equal(result.items.find(item => item.itemdefid === 404001).drop_interval, 2);
    assert.equal(result.items.find(item => item.itemdefid === 404002).drop_interval, 5);
    assert.equal(Object.hasOwn(result.playtimeReferences[0], "activationBudgetSeconds"), false);
    assert.equal(Object.hasOwn(result.playtimeReferences[1], "activationBudgetSeconds"), false);
});

test("derives recurring playtime interval and drop window without a total drop limit", () => {
    const result = buildArtifacts(
        [rewardGenerator(), playtimeGenerator({ Key: "RecurringDrop" })],
        [],
        [gameItem()],
        [blindBox(), fallbackBlindBox()],
        [schedule({
            IsLoopTrack: true,
            StartSeconds: 0,
            IntervalSeconds: 450,
            MaxGrantCount: -1,
            SteamDropIntervalSeconds: 900,
            SteamDropWindowSeconds: 1800,
            SteamDropMaxPerWindow: 2,
        })],
        [],
        [config()],
    );

    assert.deepEqual(result.errors, []);
    const output = result.items.find(item => item.itemdefid === 404001);
    assert.equal(output.drop_interval, 15);
    assert.equal(output.use_drop_window, true);
    assert.equal(output.drop_window, 30);
    assert.equal(output.drop_max_per_window, 2);
    assert.equal(output.use_drop_limit, false);
    assert.equal(Object.hasOwn(output, "drop_limit"), false);
});

test("rejects a recurring schedule without a Steam-specific drop interval", () => {
    const result = buildArtifacts(
        [rewardGenerator(), playtimeGenerator()],
        [],
        [gameItem()],
        [blindBox(), fallbackBlindBox()],
        [schedule({
            IsLoopTrack: true,
            StartSeconds: 0,
            IntervalSeconds: 90,
            MaxGrantCount: -1,
            SteamDropIntervalSeconds: 0,
            SteamDropWindowSeconds: 360,
            SteamDropMaxPerWindow: 2,
        })],
        [],
        [config()],
    );

    assert.ok(result.errors.some(error => error.includes("SteamDropIntervalSeconds")));
});

test("rejects a platform schedule without an explicit local Refreshment fallback", () => {
    const result = buildArtifacts(
        [rewardGenerator(), playtimeGenerator()],
        [],
        [gameItem()],
        [blindBox()],
        [schedule({ FallbackBlindBoxId: 0 })],
        [],
        [config()],
    );

    assert.ok(result.errors.some(error => error.includes("必须配置 FallbackBlindBoxId")));
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
    const playtime = receipt({
        Id: 405001,
        Key: "RecurringDrop",
        Type: 4,
        PromoRule: "",
        GrantedManually: false,
        Bundle: "403001x1",
    });
    const result = buildArtifacts(
        [rewardGenerator(), playtime],
        [],
        [gameItem()],
        [blindBox(), fallbackBlindBox()],
        [schedule({
            Id: 2001,
            IsLoopTrack: true,
            StartSeconds: 0,
            IntervalSeconds: 90,
            MaxGrantCount: -1,
            SteamPlaytimeGeneratorItemDefId: 405001,
            SteamDropIntervalSeconds: 180,
            SteamDropWindowSeconds: 360,
            SteamDropMaxPerWindow: 0,
        })],
        [],
        [config()],
    );

    assert.ok(result.errors.some(error => error.includes("SteamDropMaxPerWindow 必须大于 0")));
});

test("validates a blind-box Schedule completion receipt", () => {
    const completionReceipt = receipt({
        Id: 500005,
        Key: "NewbieBlindBoxCheckpoint05ReceiptOfficial",
        Name: "Newbie Blind Box Checkpoint 5 Receipt",
    });
    const result = buildArtifacts(
        [completionReceipt],
        [],
        [],
        [],
        [schedule({
            SteamPlaytimeGeneratorItemDefId: 0,
            SteamCompletionReceiptItemDefId: 500005,
        })],
        [],
        [],
        idRanges(),
    );

    assert.deepEqual(result.errors, []);
    assert.deepEqual(result.completionReceiptReferences, [{
        scheduleId: 1001,
        receiptItemDefId: 500005,
    }]);
    assert.ok(buildChannelArtifact(result, "release").items.some(item =>
        item.itemdefid === 500005));
});

test("rejects an unsafe blind-box Schedule completion receipt", () => {
    const completionReceipt = receipt({
        Id: 500005,
        Key: "UnsafeNewbieReceipt",
        AutoStack: true,
    });
    const result = buildArtifacts(
        [completionReceipt],
        [],
        [],
        [],
        [schedule({
            SteamPlaytimeGeneratorItemDefId: 0,
            SteamCompletionReceiptItemDefId: 500005,
        })],
    );

    assert.ok(result.errors.some(error => error.includes("AutoStack=false")));
});

test("rejects one blind-box completion receipt shared by multiple Schedules", () => {
    const completionReceipt = receipt({
        Id: 500005,
        Key: "NewbieCheckpointReceipt",
    });
    const result = buildArtifacts(
        [completionReceipt],
        [],
        [],
        [],
        [
            schedule({
                SteamPlaytimeGeneratorItemDefId: 0,
                SteamCompletionReceiptItemDefId: 500005,
            }),
            schedule({
                Id: 1005,
                SteamPlaytimeGeneratorItemDefId: 0,
                SteamCompletionReceiptItemDefId: 500005,
            }),
        ],
    );

    assert.ok(result.errors.some(error => error.includes("已被 BlindBoxSchedule 1001 使用")));
});

test("rejects a playtime generator that grants a concrete item directly", () => {
    const result = buildArtifacts(
        [rewardGenerator(), playtimeGenerator({ Bundle: "101002x1" })],
        [],
        [gameItem()],
        [blindBox()],
        [schedule()],
        [],
        [config()],
    );

    assert.ok(result.errors.some(error => error.includes("必须是 Type=Generator")));
});

test("allows blind boxes to share an AUTO generator when their generated pools match", () => {
    const generator = rewardGenerator({ Key: "SharedGenerator", Bundle: "@AUTO" });
    const result = buildArtifacts(
        [
            generator,
            playtimeGenerator(),
            playtimeGenerator({ Id: 404002, Key: "Schedule1002DirectReward" }),
        ],
        [],
        [gameItem()],
        [
            blindBox(),
            blindBox({ Id: 1001 }),
            fallbackBlindBox(),
        ],
        [
            schedule(),
            schedule({
                Id: 1002,
                BlindBoxId: 1001,
                SteamPlaytimeGeneratorItemDefId: 404002,
            }),
        ],
        [
            rarityRate(),
            rarityRate({ Id: 100104, BlindBoxId: 1001 }),
        ],
        [config()],
        [],
        [
            itemWeight(),
            itemWeight({ Id: 2, BlindBoxId: 1001 }),
        ],
    );

    assert.deepEqual(result.errors, []);
    assert.equal(result.items.find(item => item.itemdefid === 403001).bundle, "101002x1");
});

test("filters the Playtest-only range from Release output", () => {
    const result = buildArtifacts(
        [receipt()],
        [],
        [gameItem()],
        [],
        [],
        [],
        [],
        idRanges(),
    );

    assert.deepEqual(result.errors, []);
    assert.ok(buildChannelArtifact(result, "playtest").items.some(item => item.itemdefid === 401001));
    assert.ok(!buildChannelArtifact(result, "release").items.some(item => item.itemdefid === 401001));
});

test("rejects a Playtest-only business reference in Release", () => {
    const result = buildArtifacts(
        [receipt(), claimBundle()],
        [linkTree()],
        [gameItem()],
        [],
        [],
        [],
        [],
        idRanges(),
    );

    assert.ok(buildChannelArtifact(result, "release").errors.some(error =>
        error.includes("Playtest 专用 ItemDef")));
});

test("allows a Playtest-only LinkTree reference when its channel mask excludes Release", () => {
    const result = buildArtifacts(
        [receipt(), claimBundle()],
        [linkTree({ BuildChannelMask: 2 })],
        [gameItem()],
        [],
        [],
        [],
        [],
        idRanges(),
    );

    assert.deepEqual(result.errors, []);
    assert.deepEqual(buildChannelArtifact(result, "playtest").errors, []);
    assert.deepEqual(buildChannelArtifact(result, "release").errors, []);
});

test("rejects an enabled LinkTree entry without a build channel", () => {
    const result = buildArtifacts(
        [receipt(), claimBundle()],
        [linkTree({ BuildChannelMask: 0 })],
        [gameItem()],
    );

    assert.ok(result.errors.some(error => error.includes("至少配置一个 BuildChannelMask")));
});

test("validates the formal Item mapping from the exported ID plan", () => {
    const result = buildArtifacts(
        [],
        [],
        [gameItem({ SteamItemDefId: 101003 })],
        [],
        [],
        [],
        [],
        idRanges(),
    );

    assert.ok(result.errors.some(error => error.includes("100000 + Item.Id = 101002")));
});
