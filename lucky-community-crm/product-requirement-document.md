# Lucky Dog Rise X 用户搜索与关系管理工作台

版本：V3 PRD  
用途：独立游戏市场推广辅助工具

---

# 1. 项目背景

Lucky Dog Rise 是一款 Steam 桌宠游戏。

推广策略不是传统广告投放，而是在 X（Twitter）寻找潜在用户，通过：

1. 观察用户行为；
2. 判断用户是否属于目标群体；
3. 建立自然互动；
4. 在 Steam Playtest / 正式上线阶段邀请体验。

目标用户主要包括：

## A. 桌宠用户

已经使用：

- Bongo Cat
- Mewgenics 类桌面陪伴产品
- Shimeji
- Desktop Mate
- 其他 on-screen companion

的人。

特征：

- 会晒桌面截图；
- 会表达“陪伴感”；
- 会讨论帽子、皮肤、装扮。

---

## B. Steam 玩家

不是所有 Steam 玩家都是目标。

重点寻找：

- 喜欢轻量游戏；
- 喜欢 cozy 游戏；
- 喜欢试玩新游戏；
- 喜欢收集；
- 喜欢养成。

---

## C. 狗狗爱好者

尤其：

- 会晒狗照片；
- 喜欢给狗戴帽子、眼镜；
- 喜欢宠物拟人化。

---

## D. 内容创作者

包括：

- Twitch主播
- VTuber
- 游戏视频作者
- 独立游戏推广者

---

# 2. 核心理念

不要寻找“宣传游戏的人”。

寻找：

> 会自然喜欢 Lucky Dog Rise 这种体验的人。


判断标准：

不是：

“他关注游戏。”

而是：

“他的生活方式里已经存在桌面陪伴、宠物、收集、轻量游戏。”

---

# 3. 搜索关键词系统

关键词分为多个实验组。

---

# Group 1：Bongo Cat 用户

目的：

寻找已经验证过桌宠需求的人。


关键词：

```
"Bongo Cat"
"BongoCat"
"Bongo Cat" obsessed
"Bongo Cat" addicted
"Bongo Cat" desktop
"Bongo Cat" hats
"Bongo Cat" skins
```

高价值信号：

例如：

> I love having this little guy on my desktop

> I keep Bongo Cat running all day


---

# Group 2：桌面陪伴

关键词：

```
desktop pet
desktop companion
on-screen pet
screen pet
virtual pet desktop
little companion on my desktop
```

注意：

避免：

```
desktop pet game
```

因为容易进入开发者圈。

---

# Group 3：Steam 用户行为

寻找：

正在寻找新游戏的人。


关键词：

```
Steam recommendation
Steam discovery
Steam demo
Steam playtest
requested access Steam
wishlist and requested
```

---

# Group 4：Cozy / Idle 玩家

关键词：

```
cozy game
cozy games
idle game
relaxing game
cute game
comfort game
```

---

# Group 5：狗狗装扮灵感

用于未来：

- 装扮设计；
- 用户生成内容；
- 还愿帖。


关键词：

帽子：

```
dog wearing hat
dog in hat
dog hat
shiba hat
```

眼镜：

```
dog sunglasses
dog wearing sunglasses
dog glasses
```

---

# Group 6：主播 / VTuber

关键词：

```
playing indie games
Steam demo
Let's play indie
Twitch indie game
VTuber game
```

---

# 4. 用户评级系统

## S级

定义：

极高概率成为核心用户。


条件：

满足多个：

- 使用过桌宠；
- 喜欢收集；
- Steam玩家；
- 主播；
- 狗狗爱好者。


例：

Nu：

评级：

S


理由：

```
Bongo Cat 重度用户
喜欢桌面陪伴
有哈士奇
Twitch主播
会试玩Steam游戏
```

---

## A+

强相关。

例如：

Bongo Cat 用户。

---

## A

潜在用户：

- cozy玩家
- Steam玩家
- 狗狗爱好者


---

## B

普通互动对象。

---

## C

只做素材参考。

例如：

狗狗照片。

---

# 5. 候选人数据结构


```json
{
"name":"",
"twitter":"",
"rank":"S/A+/A/B/C",

"type":[
"Steam Player",
"Streamer",
"Dog Owner",
"Desktop Pet User"
],

"evidence":"",

"source_keyword":"",

"status":
"待互动/已互动/已回复/长期关注",

"notes":""
}
```


---

# 6. 互动策略

## 第一阶段

不要推广游戏。

目标：

成为一个正常用户。


回复方向：

### 桌宠用户

不要：

> Check my game


应该：

> I really understand why having a little companion on your desktop feels nice.


---

### 狗狗用户

不要：

> Can I add your dog to my game?


先：

> This hat looks amazing on your pup!


---

### Steam玩家

不要：

> Wishlist my game


先：

讨论：

- 游戏体验；
- 喜欢的机制。


---

# 7. Playtest阶段策略

开放 Playtest 后：

优先联系：

S级：

例如：

Nu

模板：

```
Your post about having a little companion around during the day stayed with me.

I’ve just opened the Steam Playtest for Lucky Dog Rise — a little Shiba that stays by your side while you work, take breaks, or play games.

Since you already enjoy desktop companions, I thought you might like to meet him.
```

---

# 8. 工具功能需求

## 页面布局

宽屏。

不要手机适配。

推荐：

```
-------------------------------------------------
关键词实验区 | 候选用户数据库 | 用户详情
-------------------------------------------------
```

---

# 左侧：关键词实验台

显示：

- keyword
- 分类
- 使用次数
- 有效次数
- 转化率


例如：

```
"Bongo Cat obsessed"

实验次数: 5

有效:
4

评级:
★★★★★
```


---

# 中间：候选用户列表


显示：

```
@Numuru0

S

主播/Vtuber

证据:
Bongo Cat
Steam
Discord
Dog owner

状态:
已回复
```


---

# 右侧：用户档案


记录：

- 推文链接
- 截图
- 互动历史
- 下一步


---

# 9. 数据保存

必须支持：

localStorage

并支持：

导出：

```
lucky_dog_x_database.json
```


导入：

恢复数据。


---

# 10. 后续可能功能

## 用户关系时间线

例如：

```
2026-08-05
发现

2026-08-06
回复

2026-08-09
发送Playtest邀请
```


---

## 标签系统

例如：

```
#BongoCat
#DogOwner
#Streamer
#PotentialReviewer
#DogFashion
```


---

## 自动评分

未来可以根据：

关键词匹配数量：

自动计算：

```
Score =
DesktopPet × 40
Steam × 20
Dog × 20
Creator × 20
```


---

# 目标

最终这个工具不是 Twitter CRM。

它是：

> Lucky Dog Rise 的早期用户发现系统。

核心不是找到最多的人。

而是找到：

> 第一批真正会爱上这只狗的人。

---

这个文档直接交给 Codex 就可以继续开发。你现在这个工具其实已经不是“小脚本”了，而是在搭一个独立游戏发行早期的 **用户关系数据库（Community CRM）**。你今天找到 Nu 这种案例，正好证明这个方向是有效的。