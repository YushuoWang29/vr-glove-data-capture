# 机器人 Pick-and-Place 任务场景

## 当前场景入口

机器人任务已经从“进入 Play Mode 后临时生成”升级为**可在 Unity Scene View 中直接编辑的持久化场景**：

```text
Assets/VRGloveDataCapture/Scenes/TaskSetups/PickPlaceTasks.unity
```

可以双击该文件、按 `Ctrl+Shift+F6`，或执行以下菜单打开：

```text
Tools > VR Glove Data Capture > Task Setups > Open Pick Place Task Setup
```

打开后，编辑器自动形成双场景工作区：

| Scene | 内容 | Git 状态 | 编辑规则 |
|---|---|---|---|
| `TableScene_Vive` | 原厂校准流程、虚拟手、状态面板、示例物体、实体复位按钮 | 本地 SDK，Git 忽略 | 保持原厂，不直接修改 |
| `PickPlaceTasks` | `Task_Worktable`、YCB 物体、彩色方块、容器、触发区、任务控制器 | 本仓库跟踪 | 在 Scene View 中直接编辑 |

**`PickPlaceTasks` 必须保持为 Active Scene**。自动加载器会在打开任务场景后设置这一状态，因此新建、复制或拖入的对象默认保存到项目任务场景，而不是写进原厂场景。

## 双场景架构

### 编辑阶段

任务场景根节点 `Task_Setup_Metadata` 带有 `TaskSetupSceneMarker`，其中记录基础场景路径：

```text
Assets/Hi5_Interaction_SDK/Scenes/Vive/TableScene_Vive.unity
```

`TaskSetupSceneWorkspace` 监听 Unity 的场景打开事件。只要发现标记，它就以 **Additive** 模式加载上述基础场景，并重新把任务场景设为 Active Scene。这样每一个通过项目菜单建立的任务 setup 都复用原厂校准与状态面板，但不复制、不修改专有 `.unity` 文件。

### 运行阶段

持久化场景只保存项目自有组件，不保存 Hi5 专有组件。进入 Play Mode 后：

1. `PickPlaceTaskSceneController` 等待 Hi5 simple-object manager 就绪。
2. `PickPlaceTaskObject` 按序列化的对象 ID 和名称动态挂接 Hi5 simple-object 组件。
3. Hi5 接管可抓物体后，控制器记录新的初始父节点、世界位置、旋转和缩放。
4. `PickPlaceTargetZone` 监听匹配物体进入目标触发体。
5. 控制器订阅原厂 `messageObjectReset`，把新增任务接入同一个实体复位按钮。
6. `VendorDemoLayoutOffset` 幂等地把原厂示例家具和相关物体移动到左右侧，中央工作桌保持无遮挡；收到原厂整场复位消息后，会在下一帧重新应用侧移，避免厂商组件的缓存位置把物体带回中央。

如果只打开原厂 `TableScene_Vive`，项目**不会再在 Play Mode 临时生成第二套任务布局**。所有 pick-and-place 内容只由项目自有 `PickPlaceTasks` 持久化场景提供，从源头避免可见模型与实际被抓物体分属两套实例而产生“残影/隔空抓取”。

每个任务物体使用**单一根对象**：`MeshFilter`、`Renderer`、非 Trigger `Collider`、`Rigidbody`、`PickPlaceTaskObject` 与运行时 Hi5 simple-object 组件都位于同一 GameObject。场景中不保留悬浮任务文字、蓝色起始垫或额外 `YCB_Visual` 子对象。

任务场景自带 `Task_Worktable`：桌面中心约为 `(0.08, 0.65, 0.70)`，尺寸为 `1.80 × 0.08 × 0.72 m`，可操作表面高度为 `0.69 m`；桌面和四条桌腿均使用实体 Collider 与 `Hi5Plane` 层。任务物体按其**实际世界空间 Collider 下边界**落在桌面上方 `12 mm`，避免未激活对象的空 `bounds` 导致开场穿桌或自由落体。

编辑态 `TaskSetupSceneWorkspace` 与运行时 `VendorDemoLayoutOffset` 共用同一组绝对位置规则：打开任务 setup 后，Scene View 会立即显示原厂示例桌、按钮和随桌物体位于左右侧区，中央任务台保持无遮挡。编辑态预览在保存、关闭、脚本重载和进入 Play Mode 前恢复原坐标，并保持原厂 Scene 为未修改状态；运行时再幂等地应用相同规则。因此不会累计漂移，也不会把侧移保存到原厂 `TableScene_Vive.unity`。

## 任务清单

| 编号 | 起始物体 | 目标 | 成功判据 | 主要操作特征 |
|---|---|---|---|---|
| 1 | YCB `055_baseball` | 圆形桶 | 棒球进入桶内目标体积 | 球形包络、掌内抓取、释放 |
| 2 | YCB `025_mug` | 圆形定位垫 | 马克杯进入定位垫上方目标体积 | 杯身或把手抓取、姿态控制 |
| 3 | YCB `005_tomato_soup_can` | 方形收纳箱 | 汤罐进入箱内目标体积 | 圆柱抓取、搬运、容器放置 |
| 4A | 红色方块 | 红色分类箱 | 红块进入红箱 | 颜色条件分类、精确放置 |
| 4B | 蓝色方块 | 蓝色分类箱 | 蓝块进入蓝箱 | 颜色条件分类、精确放置 |

目标底面成功时变为绿色，Console 同时记录任务 ID 和累计完成数。当前判据是匹配物体进入对应触发体积，尚未增加“释放后保持静止若干帧”的严格机器人基准条件。

## Scene View 编辑方法

### 调整现有任务

1. 在 Hierarchy 中只编辑 `PickPlaceTasks` 下的 `VRGlove_PickPlace_Tasks`。
2. 移动起始物体、目标容器和对应的 `*_Success_Zone`；默认布局不再包含蓝色起始垫或悬浮文字。
3. `Task_Worktable` 与任务物体同属项目 Scene，可直接移动；若改变桌面高度，应同步调整起始物体和目标容器。
4. 调整目标区时，同时检查其 `BoxCollider` 大小，避免视觉容器与成功体积不一致。
5. 保存场景；物体当前世界位姿会在下一次进入 Play Mode 时成为复位位姿。
6. 不要把 Hi5 专有 simple-object 组件手工保存到任务场景，它们由运行时桥接器统一添加。

### 增加一个可抓物体

可抓物体至少需要以下配置：

| 配置 | 要求 | 作用 |
|---|---|---|
| Layer | `Hi5ObjectGrasp`，索引 11 | 进入 Hi5 抓取碰撞链路 |
| Collider | 非 Trigger，形状贴近操作物体 | 指尖碰撞与目标区检测 |
| Rigidbody | `useGravity = true`、初始 `isKinematic = false`、`ContinuousDynamic` | 释放时参与重力和连续碰撞；被 Hi5 手持时暂时切换为运动学状态 |
| `PickPlaceTaskObject` | 填写唯一 `Task Id`、`Hi5 Object Id`、`Hi5 Object Name` | 运行时注册、目标匹配和复位 |

项目当前占用 Hi5 对象 ID `-1001` 至 `-1005`。新增对象应使用不重复的负数 ID，并为其设置与目标区完全一致的 `Task Id`。

### 增加一个目标区

1. 在目标视觉对象下新建 GameObject。
2. 设置 Layer 为 `Hi5ObjectTrigger`，索引 13。
3. 添加 `BoxCollider` 并勾选 `Is Trigger`。
4. 添加 `PickPlaceTargetZone`。
5. 将 `Task Id` 设置为对应可抓物体的同名 ID。
6. 把根节点的 `PickPlaceTaskSceneController` 拖入 `Controller`。
7. 把需要变色的容器底面 Renderer 拖入 `Indicator`，并设置待完成颜色。

## 新建后续任务 setup

执行：

```text
Tools > VR Glove Data Capture > Task Setups > Create Empty Task Setup Scene
```

编辑器会建立带 `TaskSetupSceneMarker` 的空任务场景和 `Task_Setup_Content` 根节点，然后自动叠加加载原厂基础场景。后续任务集合应分别保存为独立 `.unity` 文件，例如：

```text
Scenes/TaskSetups/StackingTasks.unity
Scenes/TaskSetups/InsertionTasks.unity
Scenes/TaskSetups/BimanualTasks.unity
```

这种组织方式让不同实验任务彼此隔离，同时确保每个 setup 都保留同一套原厂校准、状态面板和复位入口。

`Rebuild Pick Place Task Setup` 菜单会**覆盖** `PickPlaceTasks.unity`，只应用于明确需要恢复项目默认布局的情况；日常 Scene View 调整后不要执行该命令。

## 统一复位行为

原厂实体按钮发布 Hi5 `messageObjectReset`。`PickPlaceTaskSceneController` 收到消息后执行：

1. 将五个任务物体恢复到进入 Play Mode 后记录的初始父节点、世界位置、旋转和缩放。
2. 将刚体线速度、角速度清零，恢复 `useGravity = true`、`isKinematic = false` 的动态释放状态并唤醒刚体。
3. 清空累计完成集合，将所有目标底面恢复为待完成颜色。
4. 保留原厂示例物体自身的复位行为。

Game View 聚焦时按 `F8` 会发布同一个 Hi5 复位消息，因此它是实体按钮的键盘等效入口。

## 设计依据与数据来源

**YCB Object and Model Set** 面向机器人操作基准，包含日常物体的网格、纹理和 RGB-D 数据。本项目只引入三个 Google 16k textured mesh，并将其归一化到约 `74 mm`、`117 mm` 和 `102 mm` 的最大尺寸。交互碰撞采用简化球体或包围盒，渲染仍使用 YCB 纹理网格。

YCB 数据集页面声明数据采用 **CC BY 4.0**；署名和改动说明见根目录 `THIRD_PARTY_NOTICES.md` 与 `LICENSES/YCB-CC-BY-4.0.txt`。任务形式参考 [ManiSkill 桌面夹爪任务](https://maniskill.readthedocs.io/en/latest/tasks/table_top_gripper/) 中的 `PickCube`、`PickSingleYCB` 和 `PlaceSphere`。YCB 的正式项目与引用信息见 [YCB Benchmarks](https://www.ycbbenchmarks.com/) 和 [YCB 数据下载页](https://ycb-benchmarks.s3-website-us-east-1.amazonaws.com/)。

## 验证流程

### 静态资源验证

执行：

```text
Tools > VR Glove Data Capture > Validate Pick Place Task Assets
```

验证器检查项目自有任务场景、三个 YCB 模型及纹理、三个 Hi5 物理层和本地 `TableScene_Vive`。

### Play Mode 自动测试

测试名称：

```text
PickPlaceTaskSceneSmokeTests.EditableTaskSceneBindsFiveTasksAndHi5ResetRestoresTheirPoses
```

在 Edit Mode 下按 `Ctrl+Shift+F7`，或执行 `Tools > VR Glove Data Capture > Task Setups > Run Pick Place Play Mode Test`，即可单独运行该回归测试。Console 汇总 `passed=1, failed=0, skipped=0` 表示通过。

测试先加载原厂场景，使 Hi5 manager 就绪，再以 Additive 模式加载持久化任务场景，并验证：没有悬浮文字、起始垫或重复渲染子对象；实体工作桌、四条桌腿及其碰撞层正确；5 个对象均处于桌面投影范围且实际 Collider 下边界不穿桌；原厂示例区完整移到两侧且整场复位后仍保持侧移；5 个对象 ID 唯一且完成 Hi5 绑定；全部对象具备动态重力和实体 Collider；马克杯保存的初始姿态直立；5 个目标判据全部触发；`messageObjectReset` 恢复位置、动态刚体状态、速度、任务进度和目标颜色。未安装本地 Hi5 Interaction SDK 时测试标记为忽略。

### 实机检查

1. 打开 `PickPlaceTasks.unity`，确认 Hierarchy 同时显示 `TableScene_Vive` 和 `PickPlaceTasks`。
2. 确认 Scene View 在 Play Mode 前已经能看到中央实体工作桌及完整任务区。
3. 启动 SteamVR 与 Hi5 运行时，完成校准后进入 Play Mode。
4. 确认原厂校准/状态面板仍在，原厂桌和示例物体位于左右侧，中央工作桌无遮挡。
5. 确认所有任务物体在重力启用后稳定落在桌面而不是落向地面，虚拟手能抓取五个任务物体。
6. 逐项放置，确认目标底面变绿并出现全部完成日志。
7. 移动物体后拍下原厂实体复位按钮，确认原厂物体和项目任务物体同时复位。
8. 再用 `F8` 验证相同的全场复位结果。

## 数据资产清单

下载归档、仓库内文件来源、哈希和裁剪范围记录在 [YCB_ASSET_MANIFEST.md](YCB_ASSET_MANIFEST.md)。仓库没有包含 `.tgz`、RGB/RGB-D 原始扫描、点云或高面数版本。
