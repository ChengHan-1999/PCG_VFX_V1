# Stage1 纹理槽位生成说明

这个目录里放的是第一版纯算法实现，用来为魔法阵 VFX 选择三个模块化纹理槽位：

```text
Weapon Texture
Boss Texture
Region Texture
```

当前代码已经可以读取玩家数据和静态定义表，计算每个候选纹理的评分，将评分转换为采样概率，并为每个槽位采样出一个最终纹理。随后还会根据已选中的三个纹理语义向量，做一版基础的 Stage2 主题推断，并把结果一起写入 `GenerationResult`。

目前这一层主要用于验证算法结果和日志输出，还没有自动把结果绑定到 VFX Graph 参数上。

## 当前输入数据

Unity 运行时从下面这个目录读取数据：

```text
Assets/StreamingAssets/Data/
```

当前使用的文件包括：

```text
Data/PlayerProfile.json
Data/Modules/WeaponModuleDefinitions.json
Data/Modules/BossModuleDefinitions.json
Data/Modules/RegionModuleDefinitions.json
Data/Definitions/BossDefinitions.json
Data/Themes/ThemeDefinitions.json
Data/Config/AlgorithmConfig.json
```

Unity 项目外的源文件目前存放在：

```text
C:/Final_Lesson/PlayerProfile.json
C:/Final_Lesson/Data/
```

如果你在 Unity 外部修改了源 JSON 文件，需要把更新后的文件同步到 `Assets/StreamingAssets/Data/`，否则 Unity Play 时读取的仍然是旧版本。

## C# 文件作用

### Data/PcgDataModels.cs

定义 Stage1 和基础 Stage2 需要用到的可序列化数据类。

主要输入类：

```text
AlgorithmConfig
PlayerProfileDatabase
PlayerProfile
WeaponUsageRecord
BossCombatRecord
RegionExplorationRecord
ModuleDefinitionSet
ModuleCandidateDefinition
BossDefinitionSet
BossDefinition
ThemeDefinitionSet
ThemeDefinition
```

主要输出类：

```text
CandidateEvaluation
SlotGenerationResult
ThemeGenerationResult
GenerationResult
```

其中 `GenerationResult` 是核心结果对象。控制台输出、后续 JSON 日志、CSV 实验记录，以及未来 VFX Graph 参数绑定，都应该从这个对象读取。

### Core/PcgDataLoader.cs

负责从 `Application.streamingAssetsPath` 读取 JSON 文件。

它会读取：

```text
PlayerProfile.json
WeaponModuleDefinitions.json
BossModuleDefinitions.json
RegionModuleDefinitions.json
BossDefinitions.json
ThemeDefinitions.json
AlgorithmConfig.json
```

同时会检查玩家数据、模块定义和主题定义是否为空。

### Core/PcgLookupBuilder.cs

把某个玩家的数组数据转换成字典，方便算法快速查找。

例如：

```text
weaponId -> WeaponUsageRecord
bossId -> BossCombatRecord
regionId -> RegionExplorationRecord
bossId -> BossDefinition
```

这样评分器就能把静态模块 `Weapon_Axe` 关联到玩家数据里 `weaponId = Axe` 的行为记录。

### Core/PcgMath.cs

存放通用数学函数：

```text
SafeDivide
Sigmoid
ApplyTemperatureProbabilities
SampleByProbability
FindTopEligible
BuildSlotResult
```

`ApplyTemperatureProbabilities` 会把候选纹理评分转换成采样概率：

```text
P_i = (S_i + epsilon)^(1 / temperature) / sum_j (S_j + epsilon)^(1 / temperature)
```

`SampleByProbability` 使用带 seed 的 `System.Random`。所以在玩家数据和 seed 不变时，采样结果可以复现。

### Core/SlotScorers.cs

包含三个 Stage1 槽位评分器。

#### WeaponSlotScorer

用于计算所有武器纹理候选对象。

候选武器成立条件：

```text
owned == true
并且
effectiveUseAmount > 0 或 activeResourceInvestment > 0
```

当前武器评分公式：

```text
useRatio = effectiveUseAmount_i / sum(effectiveUseAmount)
investmentRatio = activeResourceInvestment_i / sum(activeResourceInvestment)

S_i = weaponUseWeight * useRatio
    + weaponInvestmentWeight * investmentRatio
```

默认配置：

```text
weaponUseWeight = 0.6
weaponInvestmentWeight = 0.4
```

静态玩家数据里的 `effectiveUseAmount` 已经是当前时刻的有效使用量。动态半衰期更新不在这个类里做，而是在 `DynamicTrajectoryProcessor` 里做。

#### BossSlotScorer

用于计算所有 Boss 纹理候选对象。

候选 Boss 成立条件：

```text
该 Boss 出现在 player.bossCombatData 中
并且
存在对应的 BossDefinition
```

当前 Boss 静态价值：

```text
BossValue =
bossDifficultyWeight * challengeDifficulty
+ bossRarityWeight * rarity
```

当前首次击败含金量：

```text
p = sigmoid(bossLevelLambda * (playerLevelAtFirstDefeat - bossLevel))
effectiveAttempts = 1 + bossAttemptGamma * max(0, attemptCountAtFirstDefeat - 1)
Prestige = -log(1 - (1 - p)^effectiveAttempts)
```

最终 Boss 评分：

```text
S_b = BossValue * Prestige
```

注意：这是根据你目前论文公式整理出的第一版可运行实现。`bossLevelLambda`、`bossAttemptGamma` 和 Boss 含金量公式的最终解释，后面仍然建议和导师再确认。

#### RegionSlotScorer

用于计算所有区域纹理候选对象。

区域完成度：

```text
exploration = completedExplorationPoints / totalExplorationPoints
quest = completedRegionalQuests / totalRegionalQuests
```

候选区域成立条件：

```text
exploration >= regionEligibilityThreshold
或
quest >= regionEligibilityThreshold
```

当前区域评分公式：

```text
S_r =
regionExplorationWeight * exploration
+ regionQuestWeight * quest
```

默认配置：

```text
regionEligibilityThreshold = 0.8
regionExplorationWeight = 0.6
regionQuestWeight = 0.4
```

### Core/ThemeInference.cs

用于执行第一版 Stage2 主题推断。

它不是直接读取玩家原始行为，而是读取 Stage1 已经选出的三个纹理模块，并组合这些模块背后的语义向量：

```text
CombinedThemeVector =
  WeaponVector * stage2WeaponSemanticWeight
  + BossVector * stage2BossSemanticWeight
  + RegionVector * stage2RegionSemanticWeight
```

当前默认权重：

```text
stage2WeaponSemanticWeight = 0.25
stage2BossSemanticWeight = 0.25
stage2RegionSemanticWeight = 0.50
```

然后它会把 `CombinedThemeVector` 与 `ThemeDefinitions.json` 中的主题原型向量比较，选出最接近的主题。

为了避免语义证据太弱时强行生成某个主题，它使用两个约束：

```text
themeConfidenceThreshold = 0.45
themeMarginThreshold = 0.15
```

如果最高主题得分太低，或者最高主题与第二主题差距太小，就回退到 `Neutral` 主题。

### Core/TextureSlotGenerator.cs

协调一次完整生成。

它会依次调用：

```text
WeaponSlotScorer.Generate
BossSlotScorer.Generate
RegionSlotScorer.Generate
ThemeInference.Infer
```

最后返回一个 `GenerationResult`。

### Runtime/PcgGenerationTestRunner.cs

Unity 场景中的测试组件。

Inspector 中可以设置：

```text
profileId
seed
runOnStart
printCandidateDetails
```

默认值：

```text
profileId = Player_01
seed = 1001
runOnStart = true
printCandidateDetails = true
```

场景开始时会执行：

```text
1. 从 StreamingAssets 读取全部 JSON 数据。
2. 找到指定玩家。
3. 执行 Stage1 三槽位生成。
4. 执行基础 Stage2 主题推断。
5. 在 Unity Console 输出可读结果。
6. 在 Unity Console 输出完整 GenerationResult JSON。
```

它也提供右键菜单命令：

```text
Run PCG Texture Slot Generation
```

## 如何在 Unity 中运行

1. 打开 Unity 项目：

```text
C:/Final_Lesson/PCG_VFX
```

2. 打开 `SampleScene`。

3. 新建一个空物体：

```text
GameObject -> Create Empty
```

4. 可以把它重命名为：

```text
PCG_TestRunner
```

5. 添加组件：

```text
PcgGenerationTestRunner
```

6. 在 Inspector 中设置：

```text
Profile Id = Player_01
Seed = 1001
Run On Start = true
Print Candidate Details = true
```

7. 点击 Play。

8. 打开 Unity Console，查找：

```text
[PCG VFX] Texture Slot Generation Result
```

## 默认结果参考

使用当前数据和配置：

```text
profileId = Player_01
seed = 1001
```

预期 Stage1 结果大致为：

```text
Weapon selected: Weapon_Bow
Boss selected: Boss_IceDragon
Region selected: Region_SnowfieldRuins
```

预期候选评分与概率：

```text
Weapon_Sword  score=0.0670  p=0.0670
Weapon_Bow    score=0.8524  p=0.8524
Weapon_Axe    score=0.0295  p=0.0295
Weapon_Staff  score=0.0512  p=0.0512
Weapon_Dagger score=0       p=0
Weapon_Hammer score=0       p=0
```

```text
Boss_IceDragon          score=0.3352  p=0.6867
Boss_Goblin             score=0       p=0
Boss_SeaTurtle          score=0.1529  p=0.3133
Boss_StarGiant          score=0       p=0
Boss_AncientCultLeader  score=0       p=0
```

```text
Region_SnowfieldRuins   score=0.8920  p=1.0000
Region_ForestRuins      score=0       p=0
Region_OceanRuins       score=0       p=0
Region_SkyRuins         score=0       p=0
Region_MonasteryRuins   score=0       p=0
```

## 当前还没有实现的部分

当前算法层还没有自动完成：

```text
1. 写出 JSON 或 CSV 实验日志文件。
2. 把三个槽位的 atlasIndex 自动绑定到 VFX Graph。
3. 把主题粒子图集、SparkColor、SparkSize、SparkLife 等参数自动绑定到 VFX Graph。
4. 批量运行所有玩家和多个 seed 的实验。
5. 生成论文评估用的统计指标表格。
```

## 后续需要确认的公式问题

下面这些参数虽然已经有默认值，但建议作为论文方法部分的待确认项：

```text
bossLevelLambda
bossAttemptGamma
Boss prestige formula interpretation
Weapon investment 是否需要 raw proportion，还是 log-normalized investment
samplingTemperature 是否固定为 1.0，还是使用 adaptive temperature
themeConfidenceThreshold 和 themeMarginThreshold 的最终阈值
```
