# 动态玩家轨迹测试说明

这个文件说明的是动态 PCG 测试流程。它使用一个合成玩家，让玩家数据随着事件节点不断变化，并在每个节点重新运行一次 PCG 生成。

当前动态轨迹数据文件在 Unity 中的位置是：

```text
Assets/StreamingAssets/Data/Dynamic/DynamicPlayerTrajectory_Player01.json
```

同一份源数据也保存在 Unity 项目外：

```text
C:/Final_Lesson/Data/Dynamic/DynamicPlayerTrajectory_Player01.json
```

## 这个轨迹模拟了什么

当前轨迹模拟的是一个固定玩家从早期偏好匕首，逐渐转向剑，并在后期积累 Boss 和区域成就的过程。

大致变化如下：

```text
Day 1-5：主要使用和升级 Dagger。
Day 8-18：开始转向 Sword，Sword 的使用时间和资源投入逐渐超过 Dagger。
Day 12 之后：开始积累 Boss 击败记录和 Region 探索记录。
Day 39：玩家 39 级时击败 40 级 IceDragon。
```

数据中设置的武器使用半衰期为：

```text
halfLifeDays = 15
```

也就是说，旧的武器使用时间不会直接清零，而是会随着时间逐渐衰减。

武器有效使用量在每个事件节点按下面公式更新：

```text
CurrentUse = OldUse * 2 ^ (-(NewDay - OldDay) / HalfLifeDays) + ThisEventCombatMinutes
```

含义是：越近期的行为权重越高，越久以前的行为影响越弱。

## 新增脚本作用

### DynamicTrajectoryProcessor.cs

这是动态轨迹计算的核心类。

它维护一个临时玩家状态，并在每个事件节点执行：

```text
1. 对所有武器的 effectiveUseAmount 执行半衰期衰减。
2. 加入当前节点新增的武器使用时间。
3. 加入当前节点新增的武器资源投入。
4. 加入当前节点新增的 Boss 击败记录。
5. 加入当前节点新增的 Region 探索进度。
6. 把当前状态转换成一个临时 PlayerProfile。
7. 调用 TextureSlotGenerator 重新计算 Weapon、Boss、Region 三个槽位。
8. 调用 ThemeInference 重新推断粒子主题。
```

所以它解决的是：

```text
事件流数据 -> 当前玩家状态 -> 本节点 PCG 生成结果
```

### DynamicTrajectoryTestRunner.cs

这是 Unity 里用来测试动态轨迹的组件。

把它挂到一个空 GameObject 上，然后点击 Play，它会：

```text
1. 从 StreamingAssets 读取静态定义表。
2. 从 Dynamic/DynamicPlayerTrajectory_Player01.json 读取动态事件轨迹。
3. 按 eventNodes 的顺序逐个处理事件节点。
4. 每个节点都重新运行一次 PCG。
5. 在 Unity Console 中输出完整的动态结果。
```

Inspector 中主要字段：

```text
Trajectory Relative Path = Dynamic/DynamicPlayerTrajectory_Player01.json
Seed Base = 3001
Run On Start = true
Print Candidate Details = true
Print Json Result = false
```

### ThemeInference.cs

这是第一版 Stage2 主题推断脚本。

它在三个纹理槽位已经选出来之后，读取被选中纹理背后的语义向量，并按权重合成玩家当前的主题向量：

```text
CombinedThemeVector =
  WeaponVector * stage2WeaponSemanticWeight
  + BossVector * stage2BossSemanticWeight
  + RegionVector * stage2RegionSemanticWeight
```

当前默认权重在下面这个文件中：

```text
Assets/StreamingAssets/Data/Config/AlgorithmConfig.json
```

具体值为：

```json
"stage2WeaponSemanticWeight": 0.25,
"stage2BossSemanticWeight": 0.25,
"stage2RegionSemanticWeight": 0.50,
"themeConfidenceThreshold": 0.45,
"themeMarginThreshold": 0.15
```

如果主题最高分太低，或者最高主题和第二主题差距太小，系统不会强行偏向某个主题，而是回退到 `Neutral`。

## 如何在 Unity 中运行

1. 打开 Unity 项目：

```text
C:/Final_Lesson/PCG_VFX
```

2. 打开当前场景，例如 `SampleScene`。

3. 新建一个空物体：

```text
GameObject -> Create Empty
```

4. 可以命名为：

```text
Dynamic_PCG_TestRunner
```

5. 给它添加组件：

```text
DynamicTrajectoryTestRunner
```

6. 保持 `Trajectory Relative Path` 为：

```text
Dynamic/DynamicPlayerTrajectory_Player01.json
```

7. 点击 Play。

8. 打开 Unity Console，查找：

```text
[PCG VFX] Dynamic Trajectory Result
```

## 控制台每个节点会输出什么

每个事件节点都会输出：

```text
Node ID
Day
PlayerLevel
Weapon Slot 结果
Boss Slot 结果
Region Slot 结果
Theme 结果
```

每个槽位中会包含：

```text
Top：评分最高的候选纹理
Selected：最终采样出来的候选纹理
AtlasIndex：该纹理在对应图集中的 index
Selected Score：被采样纹理的评分
Probability：被采样纹理的采样概率
Candidate Details：每个候选纹理的 eligible、inputA、inputB、score、p
```

主题部分会包含：

```text
Selected Theme：最终主题，例如 Ice、Forest、Ocean 或 Neutral
Atlas：该主题对应的 2x2 粒子 flipbook 图集 ID
Texture：该主题粒子图集路径
TexIndexRange：粒子随机采样的 index 范围，当前一般是 0-3
Confidence：最高主题得分
Margin：最高主题与第二主题之间的差距
Combined Vector：由三个槽位合成出来的主题语义向量
Fallback：如果回退到 Neutral，会说明原因
```

## 当前版本的边界

这个动态版本目前已经能完成：

```text
1. 动态事件节点读取。
2. 武器使用时间半衰期更新。
3. 资源投入累积。
4. Boss 击败记录累积。
5. Region 探索记录累积。
6. 每个节点重新计算三个纹理槽位。
7. 每个节点重新推断粒子主题。
8. 在 Unity Console 中输出结果。
```

但当前还没有完成：

```text
1. 自动把 Weapon/Boss/Region 的 atlasIndex 传给 VFX Graph。
2. 自动把 Theme 的粒子图集、HDR 颜色、大小、生命周期、速度范围传给 VFX Graph。
3. 把动态实验结果写成 JSON 或 CSV 日志文件。
4. 在场景中按时间播放每个节点对应的实际 VFX 视觉变化。
```

下一步建议做的是“结果绑定层”：把 `GenerationResult` 中的三个纹理 index 和 `ThemeGenerationResult` 中的粒子主题参数，传给你现有 VFX Graph 的 exposed properties。这样 Unity 播放时就不只是看 Console，而是能直接看到魔法阵外观随节点变化。
