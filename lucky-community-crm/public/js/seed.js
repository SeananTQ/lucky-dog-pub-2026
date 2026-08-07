// 种子关键词库（默认数据，可被合并/重置）

export function uid() {
  return crypto.randomUUID ? crypto.randomUUID() : Date.now().toString(36) + Math.random().toString(36).slice(2);
}

export const seedKeywords = [
{group:'Bongo Cat · 情感投入',q:'"Bongo Cat" obsessed',why:'寻找明确表达上瘾、喜爱的真实玩家。'},
{group:'Bongo Cat · 情感投入',q:'"Bongo Cat" addicted',why:'寻找高情感投入用户。'},
{group:'Bongo Cat · 情感投入',q:'"Bongo Cat" love',why:'寻找自然表达喜欢 Bongo Cat 的用户。'},
{group:'Bongo Cat · 收集',q:'"Bongo Cat" hats',why:'帽子收集与 Lucky Dog 装扮系统高度重合。'},
{group:'Bongo Cat · 收集',q:'"Bongo Cat" skins',why:'寻找在意皮肤和装扮的玩家。'},
{group:'Bongo Cat · 收集',q:'"Bongo Cat" legendary hat',why:'寻找深度收集与长期游玩用户。'},
{group:'Bongo Cat · 使用场景',q:'"Bongo Cat" "while working"',why:'验证工作陪伴需求。'},
{group:'Bongo Cat · 使用场景',q:'"Bongo Cat" "while studying"',why:'验证学习陪伴需求。'},
{group:'Bongo Cat · 使用场景',q:'"Bongo Cat" "during the day"',why:'寻找白天常驻与陪伴感表达。'},
{group:'Bongo Cat · 桌面常驻',q:'"Bongo Cat" taskbar',why:'寻找主动晒任务栏截图的人。'},
{group:'Bongo Cat · 桌面常驻',q:'"Bongo Cat" desktop',why:'寻找桌面常驻截图与使用体验。'},
{group:'Bongo Cat · 桌面常驻',q:'"Bongo Cat" "on my PC"',why:'寻找明确长期运行在电脑上的用户。'},
{group:'Bongo Cat · 社交传播',q:'"Bongo Cat" friends',why:'寻找会拉朋友一起安装、具有社交传播力的人。'},
{group:'Bongo Cat · 社交传播',q:'"Bongo Cat" Discord',why:'寻找会把房间代码分享到 Discord 的用户。'},
{group:'Bongo Cat · 社交传播',q:'"Bongo Cat" room code',why:'寻找多人房间和社群使用者。'},
{group:'Bongo Cat · 社交传播',q:'"Bongo Cat" screenshot',why:'寻找愿意公开分享使用画面的用户。'},
{group:'Bongo Cat · 活跃度',q:'"Bongo Cat" hours',why:'寻找长时间运行和深度使用者。'},
{group:'Bongo Cat · 活跃度',q:'"Bongo Cat" still playing',why:'寻找当前仍在使用的玩家。'},
{group:'其他桌面游戏',q:'"Cornerpond"',why:'Bongo Cat 相邻用户池。'},
{group:'其他桌面游戏',q:'"Rusty\'s Retirement" desktop',why:'寻找桌面常驻使用者。'},
{group:'其他桌面游戏',q:'"Rusty\'s Retirement" "while working"',why:'寻找边工作边运行桌面游戏的人。'},
{group:'其他桌面游戏',q:'"Rusty\'s Retirement" "while studying"',why:'寻找学习时常驻的玩家。'},
{group:'其他桌面游戏',q:'"Desktop Mate" screenshot',why:'寻找已晒桌面角色截图的人。'},
{group:'其他桌面游戏',q:'"Desktop Mate" desktop',why:'寻找桌面角色长期使用者。'},
{group:'其他桌面游戏',q:'"Desktop Mate" "while working"',why:'验证工作陪伴场景。'},
{group:'其他桌面游戏',q:'Shimeji desktop',why:'寻找熟悉桌面角色生态的人。'},
{group:'真实用户语言',q:'"I love idle games"',why:'寻找主动表达挂机游戏偏好的人。'},
{group:'真实用户语言',q:'"I love on-screen games"',why:'极高价值表达；不是开发者营销词。'},
{group:'真实用户语言',q:'"games that stay on my desktop"',why:'直接命中桌面常驻偏好。'},
{group:'真实用户语言',q:'"game that lives on my desktop"',why:'寻找把游戏视为桌面居民的用户。'},
{group:'真实用户语言',q:'"little guy on my taskbar"',why:'寻找把桌面角色人格化的用户。'},
{group:'真实用户语言',q:'"keeps me company while I work"',why:'寻找陪伴动机用户。'},
{group:'真实用户语言',q:'"someone there with you during the day"',why:'寻找明确强调白天陪伴感的用户。'},
{group:'真实用户语言',q:'"collecting little hats"',why:'寻找装扮收集动机。'},
{group:'真实用户语言',q:'"my desktop feels alive"',why:'寻找让电脑桌面更有生命感的需求。'},
{group:'真实用户语言',q:'"leave it open all day" game',why:'寻找全天常驻型使用者。'},
{group:'狗派精准需求',q:'"Bongo Cat" "dog version"',why:'最高精准度：明确想要狗版。'},
{group:'狗派精准需求',q:'"Bongo Cat" "dog person"',why:'寻找更偏爱狗的现有桌宠用户。'},
{group:'狗派精准需求',q:'"wish this was a dog"',why:'直接需求表达。'},
{group:'狗派精准需求',q:'"need a dog version"',why:'直接需求表达。'},
{group:'Steam Playtest 行为',q:'"requested access" Steam',why:'寻找会主动申请试玩资格的用户。'},
{group:'Steam Playtest 行为',q:'"requested the playtest"',why:'寻找公开表达申请 Playtest 的玩家。'},
{group:'Steam Playtest 行为',q:'"joined the playtest" Steam',why:'寻找实际参加过试玩的用户。'},
{group:'Steam Playtest 行为',q:'"wishlist and requested"',why:'寻找愿望单 + 请求访问的完整行为。'},
{group:'Steam Playtest 行为',q:'"Steam Playtest" "requested access"',why:'更精确地抓主动测试用户。'},
{group:'Steam Playtest 行为',q:'"wishlisted and requested" Steam',why:'寻找愿望单与 Playtest 双行为。'},
{group:'内容创作者信号',q:'"Bongo Cat" Twitch',why:'寻找主播或直播用户。'},
{group:'内容创作者信号',q:'"Bongo Cat" streamer',why:'寻找会把桌宠带进直播场景的人。'},
{group:'内容创作者信号',q:'"Bongo Cat" VTuber',why:'寻找虚拟主播和可爱角色受众。'},
{group:'内容创作者信号',q:'"Bongo Cat" "LIVE NOW"',why:'寻找正在直播或做内容的人。'},
{group:'内容创作者信号',q:'"on-screen games" Twitch',why:'寻找明确喜欢屏幕常驻游戏的主播。'}
];

export function seed() {
  return {
    schemaVersion: 2,
    keywords: seedKeywords.map(x => ({ ...x, id: uid(), status: 'testing', note: '', opens: 0 })),
    candidates: [],
    collapsed: false,
    updatedAt: Date.now(),
  };
}
