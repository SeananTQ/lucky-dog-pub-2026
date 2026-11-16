---
last_editor: Codex
last_edit: 2026-11-13
status: draft
---

# 共用爪子推牌与卡牌动画

## 复用范围

爪子推牌、定向翻面、随机推歪及整理回正由共用表现组件提供，单机卡牌与捣乱德州使用同一份实现。共用组件不依赖房间、参局者编号、操作次数、Steam 或牌局规则。现有单机点击和教学触发流程保持原样；需要演示动作的教学可按本说明接入。

- `Scripts/PokerPawEffects.cs` 与 `Scenes/Poker/PokerPaws.tscn`：临时借用指定爪子的纹理、镜像和变换，隐藏原爪、推牌、飞回并恢复原爪。
- `Scripts/CardPushAnimation.cs`：卡牌视觉目标，负责定向翻面、推歪、回正和中断，不改变牌值或玩法状态。
- `Scenes/Poker/CardPushFlip.gdshader`：定向翻面材质。每张牌独立持有材质；单机的正反面共用该张牌的材质，中心点为零，联机 `TextureButton` 的中心为尺寸的一半。
- `CardController.PushAnimation` 与 `RoomPokerCardView.PushAnimation`：分别适配单机卡牌和联机卡牌。`DogVisual.PokerPaw(right)` 提供指定侧的原爪子，外观仍取当前毛色和装备资源。

共用资源位于 `Scenes/Poker`，可以在单机和 Demo 中使用；房间规则与网络代码继续留在 `Scripts/Rooms`，Demo／录屏导出仍排除房间目录。共用能力可用不代表自动开启任何新的教学、联机入口或演出。

## 单机或教学调用

将 `PokerPaws.tscn` 实例放进目标场景，通过 `[Export]` 绑定。在节点就绪后调用一次 `paws.Bind(GetViewport())`，随后指定源爪、目标牌、倾角及触牌回调：

```csharp
// card 为已经显示牌面的 CardController，dog 为现有 DogVisual。
// order 由调用方递增，保持最后推过的牌位于其它牌顶层。
var target = card.PushAnimation;
float angle = target.ChooseTiltAngle();
paws.Play(dog.PokerPaw(right: false), target, angle,
    direction => card.AnimatePushedFlip(faceUp: false, direction, order, angle));
```

该入口只改变可见牌面与姿态，不改变 `CardValue`、`IsHeld`、筹码，也不发送 `Clicked`。正式操作是否合法、是否扣除操作次数、何时提交规则状态，由玩法控制器决定。教学不能用演示动作伪造一次玩家点击或提交网络操作。

默认立即触牌且保持原爪尺寸，适合本人操作或本地演示。远端动作使用 `immediate: false, keepOriginalSize: false`，保留短暂前推和按卡牌宽度适配爪子的表现。指定左爪或右爪与翻面方向是两回事；翻面方向根据爪子到目标牌的显示坐标计算，不要求调用方传入房间身份或“第几次操作”。

推歪和回正可以分别调用 `target.Tilt(order, angle)`、`target.Straighten(slot)`。整理整组卡牌时，由上层只播放一次 `Card_PokerBoardStraighten`，不能每张牌各播一份。`target.Flip(direction, swapFace)` 只提供中点切换回调；音效与具体牌面仍由卡牌控制器处理。

## 坐标与中断边界

- 同一视口可使用 `Bind` 的默认坐标换算。跨 `SubViewport` 时，传入 `CanvasItem -> Transform2D`，把原爪和目标牌都换算到爪子图层所在视口；房间主场景继续由 `ModeManager.BindRoomPokerPaws` 提供换算。
- 目标牌保留自己的初始位置、倍率、转角和中心约定，不把联机牌的左上角原点直接用于单机居中精灵。动画修改视觉体，点击层级提升的是整张牌。
- 原爪不被重挂父节点。返航读取其最新位置，保留右爪镜像、本机爪子原尺寸、最多 35° 的斜推角度及快速转正节奏。
- 教学退出、重新开始或切模式时，调用 `paws.Cancel()`，并对演示过的单机牌调用 `card.ResetPushPresentation(faceUp)`，恢复该玩法当前需要的可见牌面。联机继续恢复权威快照。
- 目标隐藏、销毁或重置使尚未触牌的旧动作失效，不补执行旧回调。卡牌的新发牌、补牌和保留操作也会取消演示动画；旧发牌计时器不能在稍后覆盖新表现。
- 源爪不可用或副本池已满时仍交回触牌回调，由卡牌控制器完成翻面；不因此阻塞玩法计时或规则操作。

## 独立验收入口

`Scenes/Dev/Labs/CardPushReuseLab.tscn` 可直接 F6 运行。场景仅包含实际单机卡牌预制体、小狗及共用爪子组件，不创建房间、牌局或账号存档。点击左／右卡牌或对应按钮即可反复推牌；“回正”整理姿态，“重置”取消并还原。

`--card-push-reuse-smoke` 检查真实单机卡牌的正反面、居中坐标、材质隔离、爪子恢复、同帧重置、旧回调取消以及普通发牌／弃牌／反悔／补牌／被动提示的恢复。原联机爪子实验场景和主场景 Mock 检查仍需保留，用于覆盖跨视口、预测与确认去重等联机接入边界。

## 验证记录

2026-11-13：

- Debug、ExportRelease，以及 Demo／录屏条件编译通过，零警告、零错误；Godot 类型混淆保留检查通过。Demo 条件编译产物包含共用动画类型，不包含 `RoomPokerTableView` 或 `PokerNetworkSession`，并检查了 Demo 资源裁剪仍保留共用资源路径。本轮未打包。
- 单机复用场景默认 D3D12 下通过 25 项节点／时序检查，包含翻牌或延迟发牌中途隐藏后的目标牌面恢复；原联机爪子场景通过 136 项检查。
- 正式主场景 Mock 检查通过，包含真实卡牌输入、跨视口爪子映射、模式往返及离房后单机恢复。主场景使用 Dummy 渲染／音频，独立测试目录隔离真实存档；未进行截图视觉验收或 Steam 双机验收。
- 运行日志仍有环境证书库读取错误，未出现本轮新增脚本异常。
