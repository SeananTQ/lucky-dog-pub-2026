---
source: "https://partner.steamgames.com/doc/api/ISteamInventory#TriggerItemDrop"
retrieved_at: "2026-08-12T19:16:41.967Z"
language: zh-CN
---

# ISteamInventory 接口

> Steamworks 官方页面本地快照。原始页面正文见 ../raw/ISteamInventory.html。

Steam 库存查询与操作 API。

参见 Steam 库存服务，了解更多信息。
成员函数
ISteamInventory 的成员函数通过全局访问器函数 SteamInventory() 调用。
AddPromoItem
bool AddPromoItem( SteamInventoryResult_t *pResultHandle, SteamItemDef_t itemDef );
名称	类型	描述
pResultHandle	SteamInventoryResult_t *	返回新库存结果句柄。
itemDef	SteamItemDef_t	要授予玩家的 ItemDef。

向当前用户授予特定的一次性促销物品。

此函数能授予的物品可通过 itemdef 中的政策进行锁定，因此从客户端调用此函数很安全。 调用此函数的主要情景之一是：向同时拥有其他某款特定游戏的用户授予物品。 如果您的游戏有向用户展示一件特定促销物品的自定义 UI，此函数会很有用；而如果您希望向用户授予多个促销物品，请使用 AddPromoItems 或 GrantPromoItems。

任何可授予的物品的 itemdef 都必须带有“promo”属性。 用户必须拥有该促销物品列表中的 AppID，才能被授予特定促销物品。 这样，在配置过的物品定义中指定了 promo 属性的所有物品都能授予玩家， 从而无需更新游戏客户端也能添加额外促销物品。 以下是将物品授予拥有 TF2 或 SpaceWar 的玩家的示例。

promo: owns:440;owns:480

返回： bool
由普通用户调用时，此函数将始终返回 true；而由 SteamGameServer 调用时，则始终返回 false。

调用成功时，如果存在已授予的物品，库存结果将包含这些物品。 就算该用户因不符合促销活动资格而未收到物品，调用仍属成功。

通过 pResultHandle 返回一个新结果句柄。

注意： 完成后，务必针对所提供的库存结果调用 DestroyResult。

示例：

void CInventory::GrantPromoItems()
{
    SteamInventory()->AddPromoItem( &s_GenerateRequestResult, 110 );
}
AddPromoItems
bool AddPromoItems( SteamInventoryResult_t *pResultHandle, const SteamItemDef_t *pArrayItemDefs, uint32 unArrayLength );
名称	类型	描述
pResultHandle	SteamInventoryResult_t *	返回新库存结果句柄。
pArrayItemDefs	const SteamItemDef_t *	要授予用户的物品列表。
unArrayLength	uint32	pArrayItemDefs 中物品的数量。

向当前用户授予特定的一次性促销物品。

此函数能授予的物品可通过 itemdef 中的政策进行锁定，因此从客户端调用此函数很安全。 调用此函数的主要情景之一是：向同时拥有其他某款特定游戏的用户授予物品。 想授予单一促销物品时，请使用 AddPromoItem。 想授予所有适用的促销物品时，请使用 GrantPromoItems。

任何可授予的物品的 itemdef 都必须带有“promo”属性。 用户必须拥有该促销物品列表中的 AppID，才能被授予特定促销物品。 这样，在配置过的物品定义中指定了 promo 属性的所有物品都能授予玩家， 从而无需更新游戏客户端也能添加额外促销物品。 以下是将物品授予拥有 TF2 或 SpaceWar 的玩家的示例。

promo: owns:440;owns:480

返回： bool
由普通用户调用时，此函数将始终返回 true；而由 SteamGameServer 调用时，则始终返回 false。

调用成功时，如果存在已授予的物品，库存结果将包含这些物品。 就算该用户因不符合促销活动资格而未收到物品，调用仍属成功。

通过 pResultHandle 返回一个新结果句柄。

注意： 完成后，务必针对所提供的库存结果调用 DestroyResult。

示例：

void CInventory::GrantPromoItems()
{
    SteamItemDef_t newItems[2];
    newItems[0] = 110;
    newItems[1] = 111;

    SteamInventory()->AddPromoItems( &s_GenerateRequestResult, newItems, 2 );
}
CheckResultSteamID
bool CheckResultSteamID( SteamInventoryResult_t resultHandle, CSteamID steamIDExpected );
名称	类型	描述
resultHandle	SteamInventoryResult_t	要检查 Steam ID 的库存结果句柄。
steamIDExpected	CSteamID	要验证的 Steam ID。

检查库存结果句柄是否属于指定的 Steam ID。

这在使用 DeserializeResult 时非常重要，可验证远程玩家并未假装拥有其他用户的库存。

返回： bool
true 表示结果属于目标 Steam ID；否则返回 false。
ConsumeItem
bool ConsumeItem( SteamInventoryResult_t *pResultHandle, SteamItemInstanceID_t itemConsume, uint32 unQuantity );
名称	类型	描述
pResultHandle	SteamInventoryResult_t *	返回新库存结果句柄。
itemConsume	SteamItemInstanceID_t	要消耗的物品实例 ID。
unQuantity	uint32	堆栈中要消耗的物品数量。

消耗用户库存中的物品。 如果指定物品的数量降至零，该物品将遭永久移除。

物品一旦移除，便无法恢复。 此项功能需谨慎对待：只要您的游戏会执行物品删除操作，我们就强烈建议您设计把关严格的确认流程 UI。

返回： bool
由普通用户调用时，此函数将始终返回 true；而由 SteamGameServer 调用时，则始终返回 false。

通过 pResultHandle 返回一个新结果句柄。

注意： 完成后，务必针对所提供的库存结果调用 DestroyResult。

另见： ExchangeItems、TransferItemQuantity

示例：

void CInventory::DrinkOnePotion( SteamItemInstanceID_t itemID )
{
    SteamInventory()->ConsumeItem( &s_ConsumeRequestResult, itemID, 1 );
}
DeserializeResult
bool DeserializeResult( SteamInventoryResult_t *pOutResultHandle, const void *pBuffer, uint32 unBufferSize, bool bRESERVED_MUST_BE_FALSE = false );
名称	类型	描述
pOutResultHandle	SteamInventoryResult_t *	返回新库存结果句柄。
pBuffer	const void *	要反序列化的缓冲区。
unBufferSize	uint32	pBuffer 的大小。
bRESERVED_MUST_BE_FALSE	bool	必须为 false。

将结果集反序列化，并验证签名字节。

当句柄状态设为 k_EResultExpired 时，此调用有可能进入软故障模式。 GetResultItems 在此模式下仍会成功。 “expired”结果可能表示数据已过时——不只是因为设定的时间（一小时）已过，也是因为在生成结果集后，其中的一个物品已被消耗或交易。 您可以对比从 GetResultTimestamp 到 ISteamUtils::GetServerRealTime 的时间戳来判断数据的老旧程度。 您可以忽略“expired”结果代码，继续做其他事情，也可以要求数据过时的玩家发送已更新的结果集。

在此函数调用结束后应该针对结果调用 CheckResultSteamID，以确认远程玩家并没有假装拥有其他用户的库存。

注意：bRESERVED_MUST_BE_FALSE 参数保留供未来使用，绝不应设为 true。

返回： bool
始终返回 true，然后再通过 GetResultStatus 提供错误代码。

通过 pResultHandle 返回一个新结果句柄。

注意： 完成后，务必针对所提供的库存结果调用 DestroyResult。
DestroyResult
void DestroyResult( SteamInventoryResult_t resultHandle );
名称	类型	描述
resultHandle	SteamInventoryResult_t	要销毁的库存结果句柄。

销毁结果句柄并释放所有关联内存。
ExchangeItems
bool ExchangeItems( SteamInventoryResult_t *pResultHandle, const SteamItemDef_t *pArrayGenerate, const uint32 *punArrayGenerateQuantity, uint32 unArrayGenerateLength, const SteamItemInstanceID_t *pArrayDestroy, const uint32 *punArrayDestroyQuantity, uint32 unArrayDestroyLength );
名称	类型	描述
pResultHandle	SteamInventoryResult_t *	返回新库存结果句柄。
pArrayGenerate	const SteamItemDef_t *	此调用将创建的物品列表。 目前只能为 1 件物品！
punArrayGenerateQuantity	const uint32 *	pArrayGenerate 中要创建的各物品的数量。 目前只能为一件物品，且必须设置为 1！
unArrayGenerateLength	uint32	pArrayGenerate 与 punArrayGenerateQuantity 中物品的数量。 目前必须为 1！
pArrayDestroy	const SteamItemInstanceID_t *	此调用要销毁的物品列表。
punArrayDestroyQuantity	const uint32 *	pArrayDestroy 中要销毁的各物品的数量。
unArrayDestroyLength	uint32	pArrayDestroy 与 punArrayDestroyQuantity 中物品的数量。

授予一个物品，以交换一组其他物品。

可用于实现合成配方或变形，也可用在自行变为其他物品的物品上（比如宝箱）。

此 API 的调用方传入请求的物品，以及要与其交换的现有物品及其数量的数组。 此 API 目前接受物品数组来进行生成，但目前数组的数量只能为 1，新物品的数量也只能为 1。

所有可授予的物品的 itemdef 都必须带有 exchange 属性。 exchange 属性指明能有效交换此物品的一组配方。 库存服务将自动评估交换配方。如果提供的材料与配方不符，或数量不足，交换将失败。

例如：

exchange: 101x1,102x1;103x5;104x3,105x3

根据以上表达，可以通过三种方式换取标的物：一个 #101 加一个 #102; 五个 #103; 三个 #104 加三个 #105。 参见 Steam 库存架构 文档，了解更多。

返回： bool
此函数返回 true，表示成功；当调用自 SteamGameServer 时，或者 unArrayGenerateLength 或 punArrayGenerateQuantity 不设为 1 时，则返回 false。

如果不符合配方或无法提供所需数量，交换将失败。

通过 pResultHandle 返回一个新结果句柄。

注意： 完成后，务必针对所提供的库存结果调用 DestroyResult。

另见： ConsumeItem、TransferItemQuantity

示例：

// 在用户的库存中寻找带有特定 itemdef 的物品
SteamItemInstanceID_t CInventory::GetItemIdFromInventory( SteamItemDef_t itemDefId );

void CInventory::Exchange()
{
    SteamItemInstanceID_t inputItems[2];
    uint32 inputQuantities[2];
    inputItems[0] = GetItemIdFromInventory( 103 );
    inputQuantities[0] = 3;
    inputItems[1] = GetItemIdFromInventory( 104 );
    inputQuantities[1] = 3;
    SteamItemDef_t outputItems[1];
    outputItems[0] = 100;
    uint32 outputQuantity[1];
    outputQuantity[0] = 1;

    SteamInventory()->ExchangeItems( &s_ExchangeRequestResult, outputItems, outputQuantity, 1, inputItems, inputQuantities, 2 );
}
GenerateItems
bool GenerateItems( SteamInventoryResult_t *pResultHandle, const SteamItemDef_t *pArrayItemDefs, const uint32 *punArrayQuantity, uint32 unArrayLength );
名称	类型	描述
pResultHandle	SteamInventoryResult_t *	返回新库存结果句柄。
pArrayItemDefs	const SteamItemDef_t *	要授予用户的物品列表。
punArrayQuantity	const uint32 *	pArrayItemDefs 中要授予的各物品的数量。 此为可选项，传入 NULL 指定各物品为 1。
unArrayLength	uint32	pArrayItemDefs 中物品的数量。

授予当前用户特定物品，只用于开发者。

此 API 只用于原型制作，只有您游戏的发行商组中的 Steam 帐户才能使用。

您可以传入通过物品的 SteamItemDef_t 识别的物品数组，也可选择性地传入第二个数组，内含各项物品的对应数量。 以上数组的长度必须相符！

返回： bool
由普通用户调用时，此函数将始终返回 true；而由 SteamGameServer 调用时，则始终返回 false。

通过 pResultHandle 返回一个新结果句柄。

注意： 完成后，务必针对所提供的库存结果调用 DestroyResult。

示例：

void CInventory::GrantTestItems()
{
    SteamItemDef_t newItems[2];
    uint32 quantities[2];
    newItems[0] = 110;
    newItems[1] = 111;
    quantities[0] = 1;
    quantities[1] = 1;

    SteamInventory()->GenerateItems( &s_GenerateRequestResult, newItems, quantities, 2 );
}
GetAllItems
bool GetAllItems( SteamInventoryResult_t *pResultHandle );
名称	类型	描述
pResultHandle	SteamInventoryResult_t *	返回新库存结果句柄。

开始检索当前用户库存中的所有物品。

注意： 此函数的调用受制于速率极限，如果调用太频繁，可能返回缓存的结果。 建议您只在即将显示用户的完整库存，或您预计库存已变更时，才调用此函数。

返回： bool
由普通用户调用时，此函数将始终返回 true；而由 SteamGameServer 调用时，则始终返回 false。

通过 pResultHandle 返回一个新结果句柄。

注意： 完成后，务必针对所提供的库存结果调用 DestroyResult。

示例：

void SpaceWarItem::LoadInventory( IGameEngine *pGameEngine )
{
    SteamInventory()->GetAllItems( &s_RequestResult );
}
GetEligiblePromoItemDefinitionIDs
bool GetEligiblePromoItemDefinitionIDs( CSteamID steamID, SteamItemDef_t *pItemDefIDs, uint32 *punItemDefIDsArraySize );
名称	类型	描述
steamID	CSteamID	物品将授予的用户的 Steam ID。 应与 SteamInventoryEligiblePromoItemDefIDs_t.m_steamID 相同。
pItemDefIDs	SteamItemDef_t *	通过将物品定义 ID 复制至此数组的方式返回物品定义 ID。
punItemDefIDsArraySize	uint32 *	应为 pItemDefIDs 的长度，且与 SteamInventoryEligiblePromoItemDefIDs_t.m_numEligiblePromoItemDefs 相同。

获取用户可被授予的物品定义 ID 列表。

在处理 SteamInventoryEligiblePromoItemDefIDs_t 调用结果获取物品定义 ID 时，应调用此函数。

返回： bool

另见： AddPromoItem、AddPromoItems
GetItemDefinitionIDs
bool GetItemDefinitionIDs( SteamItemDef_t *pItemDefIDs, uint32 *punItemDefIDsArraySize );
名称	类型	描述
pItemDefIDs	SteamItemDef_t *	通过将物品定义复制至此数组的方式返回物品定义。
punItemDefIDsArraySize	uint32 *	应设置为 pItemDefIDs 的长度。 如果 pItemDefIDs 为 NULL，则将返回数组需要存放的元素数量。

返回在 Steamworks 网站的应用程序管理员面板中定义的所有物品定义 ID。

物品定义可为非连续整数。

应调用此函数以响应 SteamInventoryDefinitionUpdate_t 回调。 如果您的游戏对数字定义 ID 进行了硬编码（如：紫色面具 = 20，蓝色武器模组 = 55），且不允在没有客户端补丁的情况下添加新物品类型，便无需调用此函数。

返回： bool
如果成功，返回 true； 只有在物品定义未从服务器加载，或当前的应用程序中不存在物品定义时，才会返回 false。

如果调用成功，那么 punItemDefIDsArraySize 将包含可用的物品定义数量。
GetItemDefinitionProperty
bool GetItemDefinitionProperty( SteamItemDef_t iDefinition, const char *pchPropertyName, char *pchValueBuffer, uint32 *punValueBufferSizeOut );
名称	类型	描述
iDefinition	SteamItemDef_t	要获取其属性的物品定义。
pchPropertyName	const char *	要获取的属性值对应的名称。 如果传入 NULL，则 pchValueBuffer 将包含所有可用名称的列表，名称之间以逗号分隔。
pchValueBuffer	char *	返回与 pchPropertyName 关联的值。
punValueBufferSizeOut	uint32 *	应设置为 pchValueBuffer 的大小，且返回存放该值所需的字节数。

从指定的物品定义中获取字符串形式的属性。
获取特定物品定义的属性值。

请注意有些属性（如“名称”）可能已经本地化，内容将由当前设置的 Steam 语言设置而定（请见 ISteamApps::GetCurrentGameLanguage）。 属性名称不论何时均为 ASCII 字母数字和下划线。

为 pchPropertyName 传入 NULL，以获得可用属性名称的列表，名称之间以逗号分隔。 在此模式下，punValueBufferSizeOut 将包含建议的缓冲区大小。 在其他情况下，将是实际复制入 pchValueBuffer 的字节大小。

返回： bool
如果成功，返回 true；否则，返回 false，表示物品定义并未从服务器加载，或该应用程序中不存在物品定义，或物品定义中未找到属性名称。

关联的值通过 pchValueBuffer 返回，需要存放此值的总字节数从 punValueBufferSizeOut 获得。 建议调用此函数两次，首先将 pchValueBuffer 设为 NULL，以及 punValueBufferSizeOut 设为零，以获得后续调用的缓冲区需要的大小。

输出示例如下：
pchPropertyName 设为 NULL：
appid,itemdefid,Timestamp 等等……
pchPropertyName 设为 "名称"：
SW_DECORATION_HAT

注意： 首先调用 LoadItemDefinitions，以确保在调用 GetItemDefinitionProperty 之物品已可用。
GetItemsByID
bool GetItemsByID( SteamInventoryResult_t *pResultHandle, const SteamItemInstanceID_t *pInstanceIDs, uint32 unCountInstanceIDs );
名称	类型	描述
pResultHandle	SteamInventoryResult_t *	返回新库存结果句柄。
pInstanceIDs	const SteamItemInstanceID_t *	要更新状态的物品实例 ID 列表。
unCountInstanceIDs	uint32	pInstanceIDs 中物品的数量。

获取当前用户库存的子集状态。

子集由物品实例 ID 数组指定。

此调用的结果可用 SerializeResult 序列化，传给其他玩家以“证明”该用户拥有特定物品，而无须暴露该用户的整个库存。 例如，您可以用该用户当前已装备物品的 ID 执行调用，将结果序列化后放进缓冲区，在进入游戏时将此缓冲区传送给其他玩家。

返回： bool
由普通用户调用时，此函数将始终返回 true；而由 SteamGameServer 调用时，则始终返回 false。

通过 pResultHandle 返回一个新结果句柄。

注意： 完成后，务必针对所提供的库存结果调用 DestroyResult。
GetItemPrice
bool GetItemPrice( SteamItemDef_t iDefinition, uint64 *pPrice );
名称	类型	描述
iDefinition	SteamItemDef_t	要为其获取价格的物品定义 ID。
pPrice	uint64*	要填入的价格指针。 价格会以用户的本地币种表示。

成功调用 RequestPrices 后，您可以调用此方法，以获取特定物品定义的价格。

返回： bool
true 表示成功，说明 pPrice 已成功填入给定物品定义 ID 的价格。
false 表示参数无效，或给定物品定义 ID 无价格。

另见： RequestPrices
GetItemsWithPrices
bool GetItemsWithPrices( SteamItemDef_t *pArrayItemDefs, uint64 *pPrices, uint32 unArrayLength );
名称	类型	描述
pArrayItemDefs	SteamItemDef_t *	要填入的物品定义 ID 的数组。
pPrices	uint64*	pArrayItemDefs 中各相应物品定义 ID 的价格数组。 价格会以用户的本地币种表示。
unArrayLength	uint32	这应为 pArrayItemDefs 与 pPrices 数组的长度， 从GetNumItemsWithPrices 调用结果获取。

成功调用 RequestPrices 后，您可以调用此方法，以获取适用的物品定义的价格。 使用 GetNumItemsWithPrices 的结果，作为您传入数组的大小。

返回： bool
true 表示成功，说明 pArrayItemDefs 与 pPrices 已成功填入物品定义 ID 与销售物品的价格。
false 说明参数无效。

另见： RequestPrices、GetItemPrice
GetNumItemsWithPrices
uint32 GetNumItemsWithPrices();
调用 RequestPrices 成功后，此函数将返回具有有效定价的物品定义数量。

返回： uint32

另见： RequestPrices、GetItemsWithPrices
GetResultItemProperty
bool GetResultItemProperty( SteamInventoryResult_t resultHandle, uint32 unItemIndex, const char *pchPropertyName, char *pchValueBuffer, uint32 *punValueBufferSizeOut );
名称	类型	描述
resultHandle	SteamInventoryResult_t	含有要获取其属性的物品的结果句柄。
unItemIndex	uint32
pchPropertyName	const char *	要获取值的属性名称。 如果传入 NULL，则 pchValueBuffer 将包含所有可用名称的列表，名称之间以逗号分隔。
pchValueBuffer	char *	返回与 pchPropertyName 关联的值。
punValueBufferSizeOut	uint32 *	应设置为 pchValueBuffer 的大小，且返回存放该值所需的字节数。

获取库存结果集内一个物品的动态属性。

属性名称始终由 ASCII 字母、数字，和/或下划线组成。

如果给定缓冲区无法容纳结果，可只复制部分结果。

返回： bool
如果成功，返回 true；否则返回 false，表示库存结果句柄无效或提供的索引不包含物品。
GetResultItems
bool GetResultItems( SteamInventoryResult_t resultHandle, SteamItemDetails_t *pOutItemsArray, uint32 *punOutItemsArraySize );
名称	类型	描述
resultHandle	SteamInventoryResult_t	要为其获取物品的库存结果句柄。
pOutItemsArray	SteamItemDetails_t *	将详细数据复制至此数组，以将其返回。
punOutItemsArraySize	uint32 *	应设置为 pOutItemsArray 的长度。 如果 pOutItemsArray 为 NULL，则将返回数组需要容纳的元素数量。

获取与库存结果句柄关联的物品。

返回： bool
true 表示调用成功，否则返回 false。
调用失败的潜在原因包括：

resultHandle 无效或无库存结果句柄。

pOutItemsArray 不足以容纳数组。

该用户未拥有任何物品。

如果调用成功，那么 punItemDefIDsArraySize 将包含可用的物品定义数量。

示例：

bool bGotResult = false;
std::vector<SteamItemDetails_t> vecDetails;
uint32 count = 0;
if ( SteamInventory()->GetResultItems( callback->m_handle, NULL, &count ) )
{
	vecDetails.resize( count );
	bGotResult = SteamInventory()->GetResultItems( callback->m_handle, vecDetails.data(), &count );
}
GetResultStatus
EResult GetResultStatus( SteamInventoryResult_t resultHandle );
名称	类型	描述
resultHandle	SteamInventoryResult_t	要为其获取状态的库存结果句柄。

查询异步库存结果句柄的状态。

此处采用轮询机制，作用等同于为 SteamInventoryResultReady_t 注册回调函数。

返回： EResult
调用成功与否没有关系。

以下为可能值：

k_EResultPending——仍在进行中。

k_EResultOK——已结束，请求已成功完成，已有可用结果。

k_EResultExpired——已结束，已有结果可用，可能过时（请见 DeserializeResult）。

k_EResultInvalidParam——错误：API 调用参数无效。

k_EResultServiceUnavailable——错误：服务暂停，可稍后再试。

k_EResultLimitExceeded——错误：操作将超出单个用户库存限制。

k_EResultFail——错误：一般错误。

示例：

void SpaceWarItem::CheckInventory( IGameEngine *pGameEngine )
{
    if ( s_RequestResult != 0 )
    {
        EResult result = SteamInventory()->GetResultStatus( s_RequestResult );
        if ( result == k_EResultOK )
        {
            // 做点什么
        }
    }
}
GetResultTimestamp
uint32 GetResultTimestamp( SteamInventoryResult_t resultHandle );
名称	类型	描述
resultHandle	SteamInventoryResult_t	要为其获取时间戳的库存结果句柄。

获取结果生成时的服务器时间。

返回： uint32
时间以 Unix 时间戳表示（自 1970 年 1 月 1 日起的秒数）。

您可以将此值与 ISteamUtils::GetServerRealTime 比较，判断结果的新旧程度。
GrantPromoItems
bool GrantPromoItems( SteamInventoryResult_t *pResultHandle );
名称	类型	描述
pResultHandle	SteamInventoryResult_t *	返回新库存结果句柄。

授予当前用户所有可授予的一次性促销物品。

此函数能授予的物品可通过 itemdef 中的政策进行锁定，因此从客户端调用此函数很安全。 向同时拥有其他某款特定游戏的用户授予项目，是调用此函数的主要情景之一。 如果您只想授予特定促销物品，而非所有促销物品，请参考：AddPromoItem 与 AddPromoItems。

任何可授予的物品的 itemdef 都必须带有“promo”属性。 用户必须拥有该促销物品列表中的 AppID，才能被授予特定促销物品。 这样，在配置过的物品定义中指定了 promo 属性的所有物品都能授予玩家， 从而无需更新游戏客户端也能添加额外促销物品。 以下是将物品授予拥有 TF2 或 SpaceWar 的玩家的示例。

promo: owns:440;owns:480

返回： bool
由普通用户调用时，此函数将始终返回 true；而由 SteamGameServer 调用时，则始终返回 false。

调用成功时，如果存在已授予的物品，库存结果将包含这些物品。 就算该用户因不符合促销活动资格而未收到物品，调用仍属成功。

通过 pResultHandle 返回一个新结果句柄。

注意： 完成后，务必针对所提供的库存结果调用 DestroyResult。

示例：

void CInventory::GrantPromoItems()
{
    SteamInventory()->GrantPromoItems( &s_GenerateRequestResult );
}
LoadItemDefinitions
bool LoadItemDefinitions();
触发异步加载和物品定义的刷新。

物品定义是“定义 ID”（介于 1 与 999999999 之间的整数）在字符串属性集里的映射。 其中有些属性是为了在 Steam 社区网站中显示物品所必须具备的。 其他属性则可由应用程序定义。 如果您的游戏对数字定义 ID 进行了硬编码（如：紫面罩 = 20，蓝武器模块 = 55），且不允许在没有客户端补丁的状况下添加新物品类型，便无需调用此函数。

返回： bool
触发 SteamInventoryDefinitionUpdate_t 回调。
此调用始终返回 true。
RequestEligiblePromoItemDefinitionsIDs
SteamAPICall_t RequestEligiblePromoItemDefinitionsIDs( CSteamID steamID );
名称	类型	描述
steamID	CSteamID	要为其请求符合条件的促销物品的用户的 Steam ID。

请求可手动授予特定用户的"符合条件"的促销物品列表。

这些物品均为不会被自动授予用户的"manual"类型促销物品。 可以用于每周开放可用的物品等。

在调用完此函数后，您需要调用 GetEligiblePromoItemDefinitionIDs 以取得实际的物品定义 ID。

返回： SteamAPICall_t，与 SteamInventoryEligiblePromoItemDefIDs_t 调用结果一起使用。
如果 steamID 不是有效的个人帐户，则返回 k_uAPICallInvalid。
RequestPrices
SteamAPICall_t RequestPrices();

请求能以用户的本地币种购买的所有物品定义的价格。 将返回 SteamInventoryRequestPricesResult_t 调用结果，并附以用户的本地币种代码。 之后，您可以调用 GetNumItemsWithPrices 与 GetItemsWithPrices，获取所有已知物品定义的价格，或者调用 GetItemPrice，获取特定物品定义。

返回： SteamAPICall_t，与 SteamInventoryRequestPricesResult_t 调用结果一起使用。
如果存在内部问题，返回 k_uAPICallInvalid。

另见： GetNumItemsWithPrices、GetItemsWithPrices、GetItemPrice
SendItemDropHeartbeat
void SendItemDropHeartbeat();
已弃用。
SerializeResult
bool SerializeResult( SteamInventoryResult_t resultHandle, void *pOutBuffer, uint32 *punOutBufferSize );
名称	类型	描述
resultHandle	SteamInventoryResult_t	要序列化的库存结果句柄。
pOutBuffer	void *	序列化后的结果将复制至的缓冲区。
punOutBufferSize	uint32 *	应设置为 pOutBuffer 的大小。 如果 pOutBuffer 为 NULL，则将返回容纳缓冲区所需要的大小。

经序列化的结果集含有无法在不同游戏会话中伪造或重复使用的短签名。

结果集可以在本机客户端进行序列化，再经由您的游戏网络传输给其他玩家，最后由远程玩家反序列化。 这种做法可以让黑客无法假装自己拥有稀有物品或高价值物品。 把含有签名字节的结果集序列化至输出缓冲区里。 序列化后的结果大小将视被序列化的物品数量而决定。 将物品安全传给其他玩家时，建议您先使用 GetItemsByID 来创建最小结果集。

结果带有内置的时间戳，一个小时后将被视为“过期”。 参见 DeserializeResult，了解如何处理“过期”问题。

如果这将 pOutBuffer 设为 NULL，那么 punOutBufferSize 将设为需要的缓冲区大小。 因此，您可以先建立缓冲区后再次调用此函数，将数据填入缓冲区。

返回： bool
true 表示成功，说明 punOutBufferSize 已成功填入缓冲区的大小；如果 pOutBuffer 指向大小足够的缓冲区，该缓冲区也将填入。
false 表示发生以下情况之一：

调用函数的并非普通用户。 GameServers 不支持调用函数。

resultHandle 无效或无库存结果句柄。

传入 punOutBufferSize 的值小于预计值，且 pOutBuffer 不为 NULL。
StartPurchase
SteamAPICall_t StartPurchase( const SteamItemDef_t *pArrayItemDefs, const uint32 *punArrayQuantity, uint32 unArrayLength );
名称	类型	描述
pArrayItemDefs	SteamItemDef_t *	用户希望购买的物品定义 ID 的数组。
punArrayQuantity	uint32 *	用户希望购买的各物品定义 ID 的数量的数组。
unArrayLength	uint32	应为 pArrayItemDefs 与 punArrayQuantity 数组。.

根据用户想购买的物品定义的“购物车”，为用户开始购买进程。 用户将在 Steam 叠加界面中收到通知，以使用本地币种完成购买，必要时为 Steam 钱包充值等。

如果购买进程成功开始，
则 m_ulOrderID 与 m_ulTransID 将在 SteamInventoryStartPurchaseResult_t 调用结果中有效。

如果用户授权交易并完成购买，则将触发 SteamInventoryResultReady_t 回调，然后您就能获取用户获得的的新物品了。 注意：完成后，务必在提供的库存结果上调用 DestroyResult。

返回： SteamAPICall_t，与 SteamInventoryStartPurchaseResult_t 调用结果一起使用。
如果输入无效，则返回 k_uAPICallInvalid。
在开发时进行测试：在应用发布前测试 StartPurchase 时，开发或发行团队的成员进行的所有交易将内部通过沙盒小额交易 API 进行。 这样，如果您是该 Steamworks 发行商的成员，您在应用发布前进行购买不会被扣费。
TransferItemQuantity
bool TransferItemQuantity( SteamInventoryResult_t *pResultHandle, SteamItemInstanceID_t itemIdSource, uint32 unQuantity, SteamItemInstanceID_t itemIdDest );
名称	类型	描述
pResultHandle	SteamInventoryResult_t *	返回新库存结果句柄。
itemIdSource	SteamItemInstanceID_t	要转移的源物品。
unQuantity	uint32	将从 itemIdSource 转移至 itemIdDest 的物品数量。
itemIdDest	SteamItemInstanceID_t	目标物品。 您可以传入 k_SteamItemInstanceIDInvalid，将源堆栈拆分成含有所请求数量的新物品堆栈。

在用户库存内的堆栈之间转移物品。

可用来堆栈、分割及移动物品。 源物品与目标物品必须拥有相同的 itemdef ID。 若需将物品移至目标堆栈，需指定来源、移动数量及目标物品 ID。 要分割现有堆栈，需将 k_SteamItemInstanceIDInvalid 传入 itemIdDest。 这将会产生一个带有所请求数量的新物品堆栈。

注意： 可交易性或可出售性限制也会随转移的物品一起被复制。 目标堆栈会收到其中所有物品的最新可交易性或可出售性日期。

返回： bool
由普通用户调用时，此函数将始终返回 true；而由 SteamGameServer 调用时，则始终返回 false。

通过 pResultHandle 返回一个新结果句柄。

注意： 完成后，务必针对所提供的库存结果调用 DestroyResult。

另见： ConsumeItem、ExchangeItems

示例：

void CInventory::CombineItems()
{
    SteamInventoryResult_t transferResult;

    // 从已有堆栈中分割出两件物品
    SteamItemInstanceID_t bigStack = GetItemIdFromInventory( ... );
    SteamInventory()->TransferItemQuantity( &transferResult, bigStack, 1, k_SteamItemInstanceIDInvalid );

    // 从堆栈 A 移动两件物品至堆栈 B
    SteamItemInstanceID_t originStack = GetItemIdFromInventory( ... );
    SteamItemInstanceID_t destStack = GetItemIdFromInventory( ... );
    SteamInventory()->TransferItemQuantity( &transferResult, originStack, 2, destStack );
}
TriggerItemDrop
bool TriggerItemDrop( SteamInventoryResult_t *pResultHandle, SteamItemDef_t dropListDefinition );
名称	类型	描述
pResultHandle	SteamInventoryResult_t *	返回新库存结果句柄。
dropListDefinition	SteamItemDef_t	必须参考“playtimegenerator”类型的 itemdefid。 参见 inventory schema，了解更多。

如果用户已达到一定游戏时间，则触发物品掉落。

可以在以下两个位置设置此时间：

在应用程序层面，“库存服务”中的“游戏时间物品授予”。 将自动应用于所有没有指定任何先决替代的“playtimegenerator”物品。

在单个的“playtimegenerator”物品定义中。 该设置的优先级将高于任何应用程序层面的设置。

只有标为“playtime item generators”的物品定义才能生成。

通常只有在游戏或关卡结束，或游戏中会发生物品掉落的重要时刻，才能调用此函数。 游戏时间生成器设置的间隔大小为分钟，因此调用频率小于一分钟是没用的，且会在 Steam 客户端中遭到速率限制。 Steam 服务器会计算游戏时间，防止物品掉落过于频繁。 服务器还会管理将物品添加到玩家库存中这一事宜。

返回： bool
由普通用户调用时，此函数将始终返回 true；而由 SteamGameServer 调用时，则始终返回 false。

通过 pResultHandle 返回一个新结果句柄。

注意： 完成后，务必针对所提供的库存结果调用 DestroyResult。

如果用户符合条件，此函数返回的库存结果即为授予的新物品。 如果用户不符合条件，此函数将返回空结果（“[]”）。

示例：

void CInventory::FinishGame()
{
    SteamInventory()->TriggerItemDrop( &s_PlaytimeRequestResult, 10 );
}
StartUpdateProperties
SteamInventoryUpdateHandle_t StartUpdateProperties();

开始交易请求，以为当前用户更新动态属性。 此调用受到用户的速率限制，因此属性修改应尽量按批次进行（如在地图或游戏会话结束时）。 为所有要修改的物品调用 SetProperty 或 RemoveProperty 后，需要调用 SubmitUpdateProperties 向 Steam 服务器发送请求。 SteamInventoryResultReady_t 回调会与操作结果一起触发。

示例：

void CInventory::FinishLevel()
{
    SteamInventoryUpdateHandle_t updateHandle = SteamInventory()->StartUpdateProperties();
    for ( SteamItemInstanceID_t itemid : m_vecItemIDs )
    {
        SteamInventory()->SetProperty( updateHandle, itemid, "string_value", "blah" );
        SteamInventory()->SetProperty( updateHandle, itemid, "bool_value", true );
        SteamInventory()->SetProperty( updateHandle, itemid, "int64_value", (int64)55 );
        SteamInventory()->SetProperty( updateHandle, itemid, "float_value", 123.456f );
    }

    SteamInventoryResult_t resultHandle;
    SteamInventory()->SubmitUpdateProperties( updateHandle, &resultHandle );
}

注意： 完成后，务必在提供的库存结果上为 SubmitUpdateProperties 调用 DestroyResult 。

返回： SteamInventoryUpdateHandle_t

另见： SetProperty、RemoveProperty、SubmitUpdateProperties
SubmitUpdateProperties
bool SubmitUpdateProperties( SteamInventoryUpdateHandle_t handle, SteamInventoryResult_t * pResultHandle );
名称	类型	描述
handle	SteamInventoryUpdateHandle_t	与交易请求对应的更新句柄，由 StartUpdateProperties 返回。
pResultHandle	SteamInventoryResult_t *	返回新库存结果句柄。

提交交易请求，为当前用户在物品上修改动态属性。 参见 StartUpdateProperties。

注意： 完成后，务必在提供的库存结果上调用 DestroyResult。

返回： bool

另见： StartUpdateProperties、SetProperty、RemoveProperty
RemoveProperty
bool RemoveProperty( SteamInventoryUpdateHandle_t handle, SteamItemInstanceID_t nItemID, const char *pchPropertyName );
名称	类型	描述
handle	SteamInventoryUpdateHandle_t	与交易请求对应的更新句柄，由 StartUpdateProperties 返回。
nItemID	SteamItemInstanceID_t	被修改的物品的 ID。
pchPropertyName	const char*	被移除的动态属性。

为给定物品移除一个动态属性。

返回： bool

另见： StartUpdateProperties、SetProperty、SubmitUpdateProperties
SetProperty
bool SetProperty( SteamInventoryUpdateHandle_t handle, SteamItemInstanceID_t nItemID, const char *pchPropertyName, const char *pchPropertyValue );
bool SetProperty( SteamInventoryUpdateHandle_t handle, SteamItemInstanceID_t nItemID, const char *pchPropertyName, bool bValue );
bool SetProperty( SteamInventoryUpdateHandle_t handle, SteamItemInstanceID_t nItemID, const char *pchPropertyName, int64 nValue );
bool SetProperty( SteamInventoryUpdateHandle_t handle, SteamItemInstanceID_t nItemID, const char *pchPropertyName, float flValue );

名称	类型	描述
handle	SteamInventoryUpdateHandle_t	与交易请求对应的更新句柄，由 StartUpdateProperties 返回。
nItemID	SteamItemInstanceID_t	被修改的物品的 ID。
pchPropertyName	const char*	添加或更新的动态属性。
pchPropertyValue	const char*	设置的字符串值。
bValue	bool	设置的布尔值。
nValue	int64	设置的 64 位整数值。
flValue	float	设置的浮点值。

为给定物品设置一个动态属性。 支持字符串值、布尔值、64 位整数值及 32 位浮点值。

返回： bool

另见： StartUpdateProperties、RemoveProperty、SubmitUpdateProperties
回调
以下是可以通过调用 SteamAPI_RunCallbacks 触发的回调。 其中许多将响应 ISteamInventory 的成员函数并直接触发。
SteamInventoryDefinitionUpdate_t
每当物品定义被更新时，此回调便会被触发，这可能是因为要响应 LoadItemDefinitions，也可能是因为有了新物品定义可用（如：玩家还在游戏中时新物品类型动态增加）。

此回调无字段。

关联函数： LoadItemDefinitions
SteamInventoryEligiblePromoItemDefIDs_t
在您请求可手动授予特定用户的"eligible"促销物品列表时返回。 这些物品均为不会被自动授予用户的"manual"类型促销物品。 可以用于每周开放可用的物品等。

名称	类型	描述
m_result	EResult	成功时，返回 k_ EResultOK，任何其他值均表示失败。
m_steamID	CSteamID	物品将授予的用户的 Steam ID。
m_numEligiblePromoItemDefs	int	该用户可用的符合资格的促销物品数量。 您应该以此创建一个 SteamItemDef_t 缓冲区，与 GetEligiblePromoItemDefinitionIDs 一起使用，获取实际的 ItemDefs。
m_bCachedData	bool	代表数据来自于缓存而非服务器。 如果用户没有登录，或无法以其他方式连上 Steam 服务器，便会出现这种情况。

关联函数： RequestEligiblePromoItemDefinitionsIDs
SteamInventoryFullUpdate_t
当 GetAllItems 成功返回比上一个已知结果更新的结果时触发。 （如果库存没有改变，或两个重叠调用的结果在运行时调换了位置，且已知较早的结果过时，则不会被触发。）
常规的 SteamInventoryResultReady_t 回调仍然会在之后立即触发。这里只是为了您的便利而提供额外提醒。

名称	类型	描述
m_handle	SteamInventoryResult_t	新库存结果句柄。
SteamInventoryResultReady_t
每当库存结果从 k_EResultPending 转移为任何其他完成状态时触发。完整状态列表请见 GetResultStatus。 每个句柄始终有且只有一个回调。

名称	类型	描述
m_handle	SteamInventoryResult_t	已准备好的库存结果。
m_result	EResult	句柄的新状态。 等同于调用 GetResultStatus。
SteamInventoryStartPurchaseResult_t
在调用 StartPurchase 后返回。

名称	类型	描述
m_result	EResult	成功时，返回 k_ EResultOK；任何其他值均表示失败。
m_ulOrderID	uint64	最初购买时自动生成的订单 ID。
m_ulTransID	uint64	最初购买时自动生成的交易 ID。
SteamInventoryRequestPricesResult_t
在调用 RequestPrices 后返回。

名称	类型	描述
m_result	EResult	成功时，返回 k_ EResultOK；任何其他值均表示失败。
m_rgchCurrency	char	此字符串代表用户的本地币种代码。
结构体
以下为 ISteamInventory 中的函数可能返回或与之交互的结构体。
SteamItemDetails_t

名称	类型	描述
m_itemId	SteamItemInstanceID_t	全局唯一的物品实例句柄。
m_iDefinition	SteamItemDef_t	该物品的物品定义数量。
m_unQuantity	uint16	该物品的当前数量。
m_unFlags	uint16	为 ESteamItemFlags 的位掩码集合。
枚举
以下为经过定义来与 ISteamInventory 一起使用的枚举。
ESteamItemFlags
以下为在 SteamItemDetails_t 中设置的位标志。

名称	值	描述
k_ESteamItemNoTrade	1 << 0	物品锁在帐户中，无法交易或送出。 这是永久附于特定物品实例上的物品状态标志。
k_ESteamItemRemoved	1 << 8	物品已被销毁、交易、过时或失效。 这是动作确认标志，作为结果集的一部分，只设置一次。
k_ESteamItemConsumed	1 << 9	物品数量已通过 ConsumeItem API 减 1。 这是动作确认标志，作为结果集的一部分，只设置一次。
Typedef
以下是经过定义来与 ISteamInventory 一起使用的 typedef。

名称	基类型	描述
SteamInventoryResult_t	int32	异步库存结果句柄。
SteamItemDef_t	int32	您游戏中的物品类型通过 32 位的“物品定义号码”来识别。
有效的定义号码介于 1 至 999999999 之间，小于或等于 0 无效，而大于或等于十亿（1x10^9）的数字保留给 Steam 内部使用。
SteamItemInstanceID_t	uint64	每一个物品实例都有全局唯一的实例 ID。
此 ID 对应的是一个玩家与特定物品实例的组合，不会转移给其他玩家或重新用于其他物品。
SteamInventoryUpdateHandle_t	uint64	从调用 StartUpdateProperties 返回，将会开始交易请求以为当前用户修改物品的动态属性。
常量
以下是经过定义来与 ISteamInventory 一起使用的常量。

名称	类型	值	描述
k_SteamInventoryResultInvalid	SteamInventoryResult_t	-1	无效的 Steam 库存结果句柄。
k_SteamItemInstanceIDInvalid	SteamItemInstanceID_t	(SteamItemInstanceID_t)~0	无效物品实例 ID。 通常当操作失败时才会返回。 建议您初始化所有具有这个值的新物品实例。
STEAMINVENTORY_INTERFACE_VERSION	const char *	"STEAMINVENTORY_INTERFACE_V002"
