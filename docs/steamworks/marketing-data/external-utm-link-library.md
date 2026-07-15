---
last_editor: Codex
last_edit: 2026-07-15
status: draft
---

# 外部媒体 UTM 链接库

## 用途

本文件统一维护《Lucky Dog Rise》从外部媒体跳转至 Steam 商店页的链接。Steamworks 的 UTM 分析可以将带 UTM 的访问归因至愿望单、购买或激活；不同平台、个人资料位和具体宣传内容必须使用不同的参数组合。

Steam 商店页基础地址：

```text
https://store.steampowered.com/app/2583700
```

游戏名称 slug 不是必需部分。为节约字符，所有链接都使用 App ID 形式。

## 固定规则

- `utm_source` 表示媒体平台，保持稳定且使用小写英文，例如 `x`、`youtube`、`bilibili`。
- `utm_medium` 表示链接摆放位置，例如 `profile`、`post`、`description`、`comment`、`bio`。
- `utm_campaign` 表示一次可复盘的宣传阶段，例如 `coming_soon`、`trailer_01`、`launch`。同一阶段的内容应沿用同一个 campaign。
- `utm_content` 仅用于区分同一 campaign 内的具体素材或帖子，例如 `20260715_announce`、`short_01`、`devlog_02`。
- 参数值使用小写英文、数字、连字符或下划线；不使用空格、中文或随意改名。
- 个人资料位链接优先短，只保留 `utm_source`；除非某个平台有足够字符空间且确实需要区分位置。
- 每一条公开宣传内容都使用自己的 `utm_content`。不要让多个帖子共用同一 `utm_content`。
- 更换 UTM 链接不会追溯旧访问。UTM 报表访问数据按小时更新；愿望单归因最迟在点击后 72 小时内计入，最终转换数据约 4 天后稳定。

## 当前已启用：X

账号：`@LuckyDogRise`

个人资料“网站”字段：

```text
https://store.steampowered.com/app/2583700?utm_source=x
```

该链接足够短，可放入 X 的 100 字符网站字段；它用于统计所有从 X 主页资料位进入 Steam 的访问。

常规帖子链接模板：

```text
https://store.steampowered.com/app/2583700?utm_source=x&utm_medium=post&utm_campaign={campaign}&utm_content={content}
```

示例：首次“即将推出”公告帖。

```text
https://store.steampowered.com/app/2583700?utm_source=x&utm_medium=post&utm_campaign=coming_soon&utm_content=20260715_announce
```

示例：首支预告片帖子。

```text
https://store.steampowered.com/app/2583700?utm_source=x&utm_medium=post&utm_campaign=trailer_01&utm_content=main_post
```

示例：同一预告片帖下的置顶回复。置顶回复与主帖必须使用不同 `content`。

```text
https://store.steampowered.com/app/2583700?utm_source=x&utm_medium=comment&utm_campaign=trailer_01&utm_content=pinned_reply
```

## 预设媒体链接

以下平台尚未建立账号或发布计划时，无需提前启用；建立后直接从对应模板复制并替换花括号内容。

### YouTube

频道“简介/链接”位：

```text
https://store.steampowered.com/app/2583700?utm_source=youtube&utm_medium=profile&utm_campaign=coming_soon
```

视频简介链接：

```text
https://store.steampowered.com/app/2583700?utm_source=youtube&utm_medium=description&utm_campaign={campaign}&utm_content={video_id}
```

置顶评论链接：

```text
https://store.steampowered.com/app/2583700?utm_source=youtube&utm_medium=comment&utm_campaign={campaign}&utm_content={video_id}_pinned
```

### TikTok

个人资料网站位：

```text
https://store.steampowered.com/app/2583700?utm_source=tiktok&utm_medium=bio&utm_campaign=coming_soon
```

视频说明或评论中若该账号具备可点击外链能力：

```text
https://store.steampowered.com/app/2583700?utm_source=tiktok&utm_medium=post&utm_campaign={campaign}&utm_content={video_id}
```

多数 TikTok 帖子不能直接放可点击外链时，视频应引导玩家使用个人资料链接；此类访问会统一归因到 `tiktok / bio`，不能精确区分到某条视频。

### Bilibili

空间简介或个人资料链接：

```text
https://store.steampowered.com/app/2583700?utm_source=bilibili&utm_medium=profile&utm_campaign=coming_soon
```

视频简介：

```text
https://store.steampowered.com/app/2583700?utm_source=bilibili&utm_medium=description&utm_campaign={campaign}&utm_content={video_id}
```

置顶评论：

```text
https://store.steampowered.com/app/2583700?utm_source=bilibili&utm_medium=comment&utm_campaign={campaign}&utm_content={video_id}_pinned
```

### 小红书

个人资料链接：

```text
https://store.steampowered.com/app/2583700?utm_source=xiaohongshu&utm_medium=profile&utm_campaign=coming_soon
```

笔记中若可使用可点击外链：

```text
https://store.steampowered.com/app/2583700?utm_source=xiaohongshu&utm_medium=post&utm_campaign={campaign}&utm_content={note_id}
```

平台限制无法点击外链时，笔记只负责引导至个人资料；不要把无法点击的长 UTM 链接塞进正文。

### Instagram

个人资料链接：

```text
https://store.steampowered.com/app/2583700?utm_source=instagram&utm_medium=bio&utm_campaign=coming_soon
```

Story 链接贴纸或可点击链接：

```text
https://store.steampowered.com/app/2583700?utm_source=instagram&utm_medium=story&utm_campaign={campaign}&utm_content={story_id}
```

### Bluesky

个人资料链接：

```text
https://store.steampowered.com/app/2583700?utm_source=bluesky&utm_medium=profile&utm_campaign=coming_soon
```

帖子链接：

```text
https://store.steampowered.com/app/2583700?utm_source=bluesky&utm_medium=post&utm_campaign={campaign}&utm_content={post_id}
```

### Reddit

个人资料链接：

```text
https://store.steampowered.com/app/2583700?utm_source=reddit&utm_medium=profile&utm_campaign=coming_soon
```

Subreddit 帖子或评论：

```text
https://store.steampowered.com/app/2583700?utm_source=reddit&utm_medium=post&utm_campaign={campaign}&utm_content={subreddit}_{post_id}
```

### Discord

Discord 没有统一的公开个人资料外链位。适合在官方群的欢迎频道、公告频道或置顶消息使用：

```text
https://store.steampowered.com/app/2583700?utm_source=discord&utm_medium=channel&utm_campaign={campaign}&utm_content={channel}_{message_id}
```

### Facebook

主页“About/网站”位：

```text
https://store.steampowered.com/app/2583700?utm_source=facebook&utm_medium=profile&utm_campaign=coming_soon
```

帖子链接：

```text
https://store.steampowered.com/app/2583700?utm_source=facebook&utm_medium=post&utm_campaign={campaign}&utm_content={post_id}
```

## 发帖前检查

1. 从基础地址或本文件对应模板复制，确认 App ID 是 `2583700`。
2. 为本次宣传确定稳定的 `campaign`，例如 `trailer_01`，不要在同一活动中混用多个名称。
3. 为每个公开位置写一个唯一的 `content`，例如 `main_post`、`pinned_reply`、`short_01`。
4. 在 Steamworks 的“UTM 分析”页面使用 `Test a UTM Link` 验证链接格式。
5. 不将个人测试点击或朋友测试点击视作实际宣传效果；若需测试，在记录中明确标注。

## 参考

- Steamworks UTM Analytics：<https://partner.steamgames.com/doc/marketing/utm_analytics?language=english>
