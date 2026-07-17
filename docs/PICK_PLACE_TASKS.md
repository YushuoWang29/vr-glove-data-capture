# 机器人 Pick-and-Place 任务场景

## 版本目标

本版本在厂商 `TableScene_Vive` 示例中增加一组可用于手部示教和机器人模仿学习原型采集的桌面操作任务。实现遵循三个约束：

1. 以低认知负担的 **pick-and-place** 为主，先覆盖球体、带把手物体、圆柱体和规则方块。
2. 不修改、不复制仓库忽略的 Hi5 专有 Scene；任务台在 Play Mode 中由项目原创代码自动注入。
3. 复用 Hi5 场景原有的 `messageObjectReset` 消息，使实体复位按钮成为整个场景的统一复位入口。

## 任务清单

| 编号 | 起始物体 | 目标 | 成功判据 | 主要操作特征 |
|---|---|---|---|---|
| 1 | YCB `055_baseball` | 圆形桶 | 棒球进入桶内目标体积 | 球形包络、掌内抓取、释放 |
| 2 | YCB `025_mug` | 圆形定位垫 | 马克杯进入定位垫上方目标体积 | 杯身或把手抓取、姿态控制 |
| 3 | YCB `005_tomato_soup_can` | 方形收纳箱 | 汤罐进入箱内目标体积 | 圆柱抓取、搬运、容器放置 |
| 4A | 红色方块 | 红色分类箱 | 红块进入红箱 | 颜色条件分类、精确放置 |
| 4B | 蓝色方块 | 蓝色分类箱 | 蓝块进入蓝箱 | 颜色条件分类、精确放置 |

目标底面在成功时变为绿色。Console 同时记录任务 ID 和累计完成数。当前判据是物体碰撞体进入对应触发体积；它适合验证工作流和采集示教，但尚未加入“释放后静止若干帧”的严格机器人基准判据。

## 设计依据与数据来源

**YCB Object and Model Set** 面向机器人操作基准，包含日常物体的网格、纹理和 RGB-D 数据。本项目只引入三个 **Google 16k textured mesh**，避免把完整数据集或原始扫描数据塞入源码仓库。YCB 数据集页面声明数据采用 **CC BY 4.0**；署名和改动说明见根目录 `THIRD_PARTY_NOTICES.md` 与 `LICENSES/YCB-CC-BY-4.0.txt`。

任务形式参考了 [ManiSkill 桌面夹爪任务](https://maniskill.readthedocs.io/en/latest/tasks/table_top_gripper/) 中的 `PickCube`、`PickSingleYCB` 和 `PlaceSphere`：先用可解释的单物体放置任务验证抓取、搬运和释放，再逐步增加分类、堆叠、随机化和更严格的成功条件。YCB 的正式项目与引用信息见 [YCB Benchmarks](https://www.ycbbenchmarks.com/) 和 [YCB 数据下载页](https://ycb-benchmarks.s3-website-us-east-1.amazonaws.com/)。

## 自动安装机制

运行时安装器位于：

```text
Assets/VRGloveDataCapture/Runtime/RoboticsTasks/
```

`PickPlaceTaskSceneInstaller` 只在加载名为 `TableScene_Vive` 的 Scene 且 Hi5 simple-object manager 就绪后工作。它以原有 `Interaction_Simple_Object_3` 的底面作为桌面高度基准，在其后方生成任务布局。新增可抓物体通过运行时反射获得与厂商简单物体相同的四类 Hi5 组件，ID 使用 `-1001` 到 `-1005`，避免与示例物体的低位负数 ID 冲突。

YCB 模型以实际网格包络自动归一化到约 `74 mm`、`117 mm` 和 `102 mm` 的最大尺寸。交互碰撞采用简化球体或包围盒，以提高 Hi5 指尖碰撞的稳定性；渲染仍使用 YCB 纹理网格。

## 统一复位行为

场景原有实体按钮会发布 Hi5 `messageObjectReset`。新增 `PickPlaceTaskSceneController` 订阅同一消息，并在收到消息时执行：

1. 将五个任务物体恢复到各自初始父节点、世界位置、旋转和缩放。
2. 将刚体线速度、角速度清零，恢复为 Hi5 simple-object 的初始 kinematic 状态并休眠。
3. 清空累计完成集合，将所有目标底面恢复为待完成颜色。
4. 让厂商示例物体继续执行其原有复位逻辑。

Game View 聚焦时按 `F8` 会主动发布同一个 Hi5 复位消息，因此它是实体按钮的键盘等效入口，不是另一套局部复位。

## 测试步骤

1. 使用 Unity `2019.4.18f1` 打开 `Unity Project/vr-glove-data-capture`，等待无编译错误。
2. 确认本地已按既有流程导入 Foundation SDK 和 Interaction SDK。
3. 打开 `Assets/Hi5_Interaction_SDK/Scenes/Vive/TableScene_Vive.unity`。
4. 启动 SteamVR 与 Hi5 运行时，完成校准后进入 Play Mode。
5. 在原有简单球体后方确认出现标题 `ROBOT PICK & PLACE BENCHMARKS`、三个 YCB 物体、两个彩色方块和对应容器。
6. 逐项抓取并放入目标；确认对应目标底面变绿，Console 最终出现 `All pick-and-place tasks completed`。
7. 将部分物体随意移动或放入错误容器，拍下原场景实体复位按钮；确认全部新增物体回到蓝色起始垫、速度归零、目标颜色恢复。
8. 再次移动物体，聚焦 Game View 后按 `F8`，确认产生相同的整场复位结果。

如果标题和任务台没有生成，优先检查 Scene 名称、Console 是否出现 `Hi5 simple-object manager did not become ready`，以及本地厂商 SDK 是否完整导入。

## 自动验证

- Unity 菜单 `Tools > VR Glove Data Capture > Validate Pick Place Task Assets` 检查三个 YCB 模型及纹理、三个 Hi5 物理层和本地 `TableScene_Vive` 是否齐全。
- Play Mode 测试 `PickPlaceTaskSceneSmokeTests.TableSceneBuildsFiveTasksAndHi5ResetRestoresTheirPoses` 会加载真实厂商场景，验证 5 个任务对象与 5 个目标区生成、全部成功判据触发，以及 Hi5 复位消息恢复位置、刚体状态、速度和目标颜色。
- 未安装本地 Hi5 Interaction SDK 时，Play Mode 测试会标记为忽略；资源静态验证会明确报出缺失场景。

## 数据资产清单

下载归档与仓库内文件的来源、哈希和裁剪范围记录在 [YCB_ASSET_MANIFEST.md](YCB_ASSET_MANIFEST.md)。仓库没有包含 `.tgz`、RGB/RGB-D 原始扫描、点云或高面数版本。
