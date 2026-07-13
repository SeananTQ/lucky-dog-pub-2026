
盲盒时间有BUG



Game Over的逻辑还在，需要处理掉,只是隐藏了




Windows 代码签名证书：证明 LuckyDogRise.exe 来自 Seanan Studio，降低 SmartScreen 和杀毒软件警告。尚未完成。
代码签名之后，Windows 可以在 EXE 属性里显示数字签名和发布者，例如：
Publisher: Seanan Studio
但它需要购买由可信 CA 颁发的代码签名证书，通常分为：
普通 OV Code Signing
更贵的 EV Code Signing
目前 Playtest 包没有代码签名，所以：
可以正常发给朋友运行。
Windows 可能显示“未知发布者”。
SmartScreen 可能提示“Windows 已保护你的电脑”。
某些杀毒软件可能因为程序小众、未签名且包含全局键鼠 hook 而更加警惕。
我们之前的决策是：代码签名暂时不作为 Playtest 完成条件。当前先允许 SmartScreen 警告，等临近正式公开发布时再决定是否购买证书。





Mary Johnson
地址
1010 NE 5th St
Clatskanie
Oregon
97016
美国

194382485