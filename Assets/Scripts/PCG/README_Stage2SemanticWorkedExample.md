# Stage 2：主题语义计算演示（Player_01，Seed 1001）

## 这份文档解决什么问题

它用项目内已经存在的合成数据，逐步说明：

```text
玩家行为数据
-> Weapon / Boss / Region 三个槽位的候选分数与概率
-> 当前的“硬选择主题向量”
-> 建议的“软语义向量”
-> 最终主题
```

这不是来自真实玩家的测量；所有 Player Profile、机会数据和轨迹事件都是为情境验证而设计的合成数据。

> 重要修正：当前 Slot 的概率 `p_i` 已经由包含 confidence 的分数生成。
> 因此本项目的软语义不应写成 `p_i * c_i * e_i`，否则 confidence 会被计算两次。
> 正确的简化形式是：`softVector = sum(p_i * e_i)`。

---

## 0. 当前数据

主题轴固定顺序为：

```text
[Ice, Forest, Galaxy, Ocean, Holy]
```

本例使用 `Player_01`，配置中的 Stage 2 权重为：

```text
Weapon = 0.10
Boss   = 0.45
Region = 0.45
```

### Player_01 的 Weapon 数据

| Weapon | Effective use | Investment | Available combat minutes |
|---|---:|---:|---:|
| Sword | 80 | 900 | 266.7 |
| Bow | 1280 | 8600 | 1561.0 |
| Axe | 35 | 400 | 116.7 |
| Staff | 60 | 700 | 200.0 |

总有效使用量为 `1455`，总资源投入为 `10600`。

Weapon 的当前公式：

```text
Score = Confidence * (0.55 * ChoiceShare + 0.30 * UseRate + 0.15 * InvestShare)
Confidence = 1 - exp(-AvailableCombatMinutes / 30)
```

以 Bow 为例：

```text
ChoiceShare = 1280 / 1455 = 0.8797
UseRate     = 1280 / 1561 = 0.8200
InvestShare = 8600 / 10600 = 0.8113
Confidence  = 1 - exp(-1561 / 30) ≈ 1.0000

BowScore = 1.0 * (0.55*0.8797 + 0.30*0.82 + 0.15*0.8113)
         = 0.8515
```

完整结果：

| Candidate | Score | Probability |
|---|---:|---:|
| Sword | 0.1329 | 0.1096 |
| Bow | 0.8515 | 0.7017 |
| Axe | 0.1066 | 0.0879 |
| Staff | 0.1224 | 0.1009 |

含义是：Bow 是最强证据，但并非 100%；Sword、Axe、Staff 仍然保留较小的可能性。

### Player_01 的 Boss 数据

| Boss | First-defeat attempts | Wins | Total attempts |
|---|---:|---:|---:|
| IceDragon | 6 | 1 | 6 |
| SeaTurtle | 4 | 1 | 4 |

Boss 仍保留原有的挑战/稀有度和首杀难度逻辑，新增 Laplace 平滑胜率：

```text
SmoothedWinRate = (Wins + 1) / (TotalAttempts + 2)
AdjustedPrestige = Prestige * lerp(1, SmoothedWinRate, 0.25)
BossScore = BossValue * AdjustedPrestige
```

IceDragon：

```text
BossValue        = 0.7*0.70 + 0.3*0.50 = 0.64
SmoothedWinRate  = (1 + 1) / (6 + 2) = 0.25
BossScore         = 0.2724
```

完整结果：

| Candidate | Score | Probability |
|---|---:|---:|
| IceDragon | 0.2724 | 0.6813 |
| SeaTurtle | 0.1274 | 0.3187 |

### Player_01 的 Region 数据

| Region | Exploration | Quests | Recent visit | Available play |
|---|---:|---:|---:|---:|
| SnowfieldRuins | 92/100 | 34/40 | 177.5 | 229.2 |
| ForestRuins | 51/90 | 16/38 | 96.5 | 190.8 |
| OceanRuins | 30/85 | 8/28 | 65.3 | 172.6 |

Region 保留原资格规则：探索或区域任务任一达到 `0.8`，才有资格显示在 Region Slot。

Snowfield：

```text
Exploration = 0.92
Quest       = 0.85
Depth       = 0.6*0.92 + 0.4*0.85 = 0.892
RecentShare = 177.5 / 460.3 = 0.3856
VisitRate   = 177.5 / 229.2 = 0.7744
Confidence  = 1 - exp(-229.2 / 45) = 0.9939

RegionScore = 0.9939 * (0.40*0.3856 + 0.25*0.7744 + 0.35*0.892)
            = 0.6560
```

只有 Snowfield 同时通过了资格门槛，因此它的 Region 概率为 `1.0`。

---

## 1. 旧方法：硬选择一个语义向量（仅用于对比）

现有代码会用 Seed 1001 从候选概率中采样一次。该例实际采样结果是：

```text
Weapon -> Bow
Boss   -> IceDragon
Region -> SnowfieldRuins
```

三张纹理仍应按这个结果显示，保证视觉上有生成感。

它们对应的语义向量为：

```text
Bow       = [0.05, 0.55, 0.00, 0.00, 0.05]
IceDragon = [0.85, 0.05, 0.00, 0.10, 0.20]
Snowfield = [0.90, 0.00, 0.00, 0.05, 0.15]
```

当前主题向量：

```text
HardThemeVector
= 0.10*Bow + 0.45*IceDragon + 0.45*Snowfield
= [0.7925, 0.0775, 0.0000, 0.0675, 0.1625]
```

最大值是 Ice 的 `0.7925`，因此主题是 Ice。

问题不在于结果错误；问题是如果下次随机采样恰好抽到 SeaTurtle，主题的 Ocean 分量会突然变大，即使 Player_01 的整体证据并没有突然变化。

---

## 2. 当前实现：软语义向量

“软”不是模糊或神秘的意思，只是：**不只取一个候选，而是让每个候选按自己的概率贡献一点语义。**

```text
SoftSlotVector = sum(CandidateProbability * CandidateSemanticVector)
```

### Weapon 的软结果

```text
WeaponSoft
= 0.1096*SwordVector
 + 0.7017*BowVector
 + 0.0879*AxeVector
 + 0.1009*StaffVector
= [0.0550, 0.4268, 0.0353, 0.0000, 0.0814]
```

Bow 仍然贡献最大，但 Staff 带来一点 Galaxy / Holy，Axe 带来一点 Forest；这正是 Player_01 的完整武器证据。

### Boss 的软结果

```text
BossSoft
= 0.6813*IceDragonVector + 0.3187*SeaTurtleVector
= [0.5950, 0.0500, 0.0000, 0.3550, 0.1522]
```

它表达的是：冰龙主导，但海龟 BOSS 也提供了 Ocean 证据。

### Region 的软结果

```text
RegionSoft = 1.0*SnowfieldVector
           = [0.9000, 0.0000, 0.0000, 0.0500, 0.1500]
```

### 最终软主题向量

```text
SoftThemeVector
= 0.10*WeaponSoft + 0.45*BossSoft + 0.45*RegionSoft
= [0.6783, 0.0652, 0.0035, 0.1822, 0.1441]
```

结果仍然是 Ice，但表达更准确：

```text
Ice   = 0.6783  <- 明确主主题
Ocean = 0.1822  <- 次级影响，主要来自 SeaTurtle
Holy  = 0.1441  <- 次级影响
```

这时可以令“魔法阵的三张槽位纹理”继续使用随机采样结果，
但“主题”和“VFX 预设”使用 SoftThemeVector 的最大值。这样纹理丰富、主题稳定，两者不再互相干扰。

---

## 3. M 是什么？现在是否需要？

`M` 只是一个人为写在 JSON 中的表格，用于把“行为风格”翻译成“主题加分”。

例如先把数据压缩成四个行为指标：

```text
b = [Commitment, Challenge, Exploration, Volatility]
```

再由一个 `5 x 4` 表格映射到五个主题轴：

```text
             Commitment  Challenge  Exploration  Volatility
Ice              0.15       0.55       0.35        -0.10
Forest           0.25       0.00       0.55         0.10
Galaxy           0.05       0.55       0.05         0.35
Ocean            0.05       0.10       0.55         0.10
Holy             0.45       0.40       0.25         0.05
```

这就是 `M`。它不是模型自动学出来的结论，而是设计师/研究者写下的规则。

例如设 Player_01：

```text
b = [0.81, 0.64, 0.89, 0.00]
```

则 `M*b` 会给各主题一组额外加分。它能强化“高专精 + 高挑战 + 高探索”的风格，但也有风险：手工权重会与三槽位语义重复计算，甚至把原本 Ice 的结果推向 Holy。

**结论：当前版本不要马上加 M。**

先实现下面两项就足够构成清楚、可解释的论文方法：

```text
1. 用 SoftSlotVector 取代“只读 selectedModuleId”的主题输入。
2. 在动态轨迹中对最终主题向量加时间平滑。
```

只有当你发现“相同三槽位，但不同投入/挑战/探索风格”确实需要产生不同主题时，再把 M 作为第三步可控实验加入。

---

## 4. 时间平滑是什么

动态节点中，令：

```text
NewEvidence = 本节点计算出的 SoftThemeVector
SmoothedTheme(t) = (1 - alpha) * SmoothedTheme(t-1) + alpha * NewEvidence
```

建议先用：

```text
alpha = 0.30
```

含义：新行为只立即影响 30%，旧主题保留 70%。

这不是让系统“反应迟钝”，而是避免一次随机纹理采样或单个事件让主题突然跳变。持续的玩家行为会在连续节点中不断累积，最后自然完成主题转换。

---

## 5. VFX：主题决定运动身份，行为决定有限幅度的表现强度

推荐的层级是：

```text
Slots -> SoftThemeVector -> ThemePreset -> VFX baseline
                                   +
                 current behaviour -> bounded VFX modifiers
```

### ThemePreset（由主题固定）

主题应决定“运动签名”，而不是只决定颜色：

| Theme | 主要运动签名 |
|---|---|
| Ice | 快速、锐利的径向晶体爆开 |
| Forest | 低速、飘荡、上升、较强乱流 |
| Galaxy | 旋涡/切向速度、快速闪烁消散 |
| Ocean | 环状扩散后形成柔和旋流 |
| Holy | 向上升腾或螺旋收束，不必是爆开 |

这部分由设计师在 Unity 中调好，存为 ThemeDefinitions.json 的主题预设。

### 行为修正（不改变主题身份）

导师提到的鼠标速度、情绪等变量，若使用，应放在这一层：只在一个主题预设附近小范围调制，而不是重新判定主题。

例如：

```text
SparkSpeed = ThemeSpeed * lerp(0.85, 1.15, CombatTempo)
SpawnCount = ThemeCount * lerp(0.80, 1.20, CombatIntensity)
Turbulence = ThemeTurbulence * lerp(0.85, 1.20, ExplorationActivity)
```

对当前论文更合适的输入是已有的、可解释的合成行为数据：

```text
CombatTempo        <- 近期有效战斗分钟 / 可用战斗机会
ChallengeIntensity <- BOSS 相对难度与挑战持续性
ExplorationActivity<- 区域近期访问率
```

不建议现在直接加入“真实情绪状态”或“真实鼠标轨迹”：项目没有真实参与者，也没有可靠的情绪测量来源。若导师希望展示这一想法，可以在动态 JSON 中明确写一个合成的 `inputTempo` 或 `arousalProxy`，并说明它是**受控实验变量**，不是测得的情绪。

## 当前实施状态与后续顺序

```text
已完成：SoftSlotVector + 动态主题平滑。
下一步：每个主题设计不同的 VFX 运动签名，并存入 JSON。
之后：用已有行为数据小范围调制速度、密度、乱流、旋转等参数。
可选：只有需要区分“相同槽位、不同风格”时，才加入 M 映射矩阵。
```
