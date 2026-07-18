# VR Glove Data Capture

基于 **Unity、HTC VIVE Pro 2、VIVE Tracker 3.0 与 Noitom Hi5 2.0 数据手套** 的手部运动采集和机器人操作示教项目。

项目当前已经跑通从 SteamVR 设备上线、Hi5 手套接入、V-pose 校准、虚拟手驱动到桌面物体交互的完整流程，并在此基础上增加了校准后注视功能面板、手指运动学增强、手–物体自适应视觉接触、VIVE 前置摄像头透视、VR 第一视角录像、机器人 pick-and-place 任务场景和按 session/trial 管理的结构化同步数据采集。

> - **当前推荐开发版本**：`codex/gaze-control-panel`
> - **Unity 固定版本**：`2019.4.18f1`
> - **主要运行方式**：Windows + SteamVR + Unity Editor Play Mode

## 项目定位

### 当前已经实现

- 使用 **Hi5 2.0 Foundation SDK** 获取手套姿态，并使用 **Hi5 2.0 Interaction SDK** 驱动虚拟手和交互物体。
- 使用左右手各一台 **VIVE Tracker 3.0** 提供手部空间定位。
- 在厂商解算结果上开放手指 **外展/内收（abduction/adduction）**，改善拇指与小指对指能力。
- 支持可选的 **PIP–DIP 比例耦合**，在传感器数量有限时获得更稳定的指尖姿态。
- 提供 **手–物体自适应视觉接触**：自由空间保持手套姿态，接触后把将要穿入刚体的可视指骨截停在物体表面；源骨骼、校准、抓取状态和采集数据保持不变。
- 使用 VIVE Pro 2 前置摄像头实现实验性的 **video see-through** 混合现实透视。
- 使用 Unity Recorder 在 Editor Play Mode 中录制 **VR 第一视角 MP4**。
- 提供可在 **Scene View 直接编辑**的机器人操作任务场景，包括球入桶、杯放定位垫、罐入箱和颜色分类。
- 将新增任务接入 Hi5 原场景的统一复位消息和实体复位按钮。
- 提供 **统一实验数据采集**：导出手部骨骼、关节角、物体与头显位姿、Hi5 模块状态和任务事件，并与 trial 视频共享主机单调时间轴。
- 提供 **session/trial 元数据管理**、操作员控制窗口、原子化文件收尾、SHA-256 校验和及可插拔的原始 IMU provider 接口。
- 完整校准后把原厂主面板切换为 **注视功能控制中心**，可用原厂驻留圆环直接控制透视、VR 视频、trial、事件标记、场景复位和重新校准；原厂校准面板与快捷键全部保留。

### 当前尚未实现

- 随项目提供的 Hi5 Unity SDK **没有公开九轴原始向量 API**；在获得厂商/传输层原始流并实现 `IRawImuProvider` 前，manifest 会明确标记 `unavailable_vendor_api`，不会用姿态差分伪造 IMU 数据。
- 当前同步属于 **单机软件时钟统一**；Hi5 原始硬件包时间戳与视频逐帧曝光时间尚不可得，不能宣称亚毫秒硬件同步。
- 尚未提供 ROS bag/HDF5 批量转换器和跨 session 的数据集索引工具。
- Windows Standalone Player 尚未完成与 Editor Play Mode 同等级别的硬件验证。

因此，当前版本已经形成 **VR 手部交互、任务设计与结构化机器人示教采集基线**；后续关键工作是接入真正的九轴原始数据源、硬件时间戳以及批量数据集转换。

## 系统架构

### 硬件与软件链路

```mermaid
flowchart LR
    subgraph Hardware["物理硬件"]
        Gloves["Hi5 2.0 左右手套<br/>指骨 IMU"]
        Trackers["2 × VIVE Tracker 3.0<br/>左右手空间定位"]
        HMD["HTC VIVE Pro 2<br/>显示 + 前置摄像头"]
        Base["2 × Base Station 2.0"]
    end

    subgraph Vendor["厂商运行时与 SDK"]
        SteamVR["SteamVR / OpenVR"]
        Foundation["Hi5 Foundation SDK"]
        Interaction["Hi5 Interaction SDK"]
        Scenes["Calibration / TableScene_Vive"]
    end

    subgraph Project["VRGloveDataCapture 原创扩展"]
        Finger["FingerKinematics<br/>外展/内收 + PIP–DIP 耦合"]
        Contact["HandInteraction<br/>可视手表面接触约束"]
        MR["MixedReality<br/>VIVE 摄像头透视"]
        Capture["Capture<br/>session/trial + 多流 CSV + MP4"]
        Tasks["RoboticsTasks<br/>YCB 抓取任务 + 统一复位"]
    end

    Base --> SteamVR
    Trackers --> SteamVR
    HMD --> SteamVR
    Gloves --> Foundation
    Foundation --> Interaction
    SteamVR --> Interaction
    Interaction --> Scenes
    Scenes --> Finger
    Finger --> Contact
    Tasks --> Contact
    SteamVR --> MR
    Scenes --> Capture
    Scenes --> Tasks
    Contact --> HMD
    MR --> HMD
    Capture --> Dataset["Captures/participants/...<br/>CSV + events + manifest + MP4"]
    Tasks --> Capture
    Tasks --> Demo["任务完成事件 / 场景复位"]
```

### 运行时设计

项目原创功能位于 `Assets/VRGloveDataCapture`，通过自动安装器或反射桥接挂接到本地厂商 SDK：

1. **不直接修改 Hi5 厂商源码**：外展/内收模式通过运行时反射配置。
2. **不提交 Hi5 专有资源**：`Assets/NoitomHi5` 与 `Assets/Hi5_Interaction_SDK` 由用户本地导入并被 Git 忽略。
3. **不修改厂商示例 Scene**：项目任务保存在独立 `.unity` 场景；编辑器自动把原厂 `TableScene_Vive` 作为基础场景叠加加载。
4. **统一复位消息**：新增任务订阅厂商的 `messageObjectReset`，与原有实体按钮共享同一复位链路。
5. **资源可追溯**：YCB 子集保留对象 ID、下载归档哈希、文件哈希和 CC BY 4.0 署名信息。
6. **统一时钟与原子化落盘**：一个采样时刻只读取一次单调时钟，多流共享 `sample_id`/`t_trial_ns`；录制中使用 `.partial`，正常结束后再生成 manifest、校验和与 `COMPLETE`。
7. **原始值不造假**：解算后的骨骼/关节姿态与九轴原始 IMU 分流记录；缺少厂商原始接口时显式标记不可用。
8. **视觉接触与测量隔离**：接触求解器只修改 `Hi5_Hand_Visible_Hand` 的最终显示 Transform；统一采集继续读取 `HI5_InertiaInstance.HandBones`，不会把表面贴合伪装成手套测量。
9. **原厂注视链路复用**：功能面板的碰撞区动态挂接厂商 `VRInteractiveItem`，选择进度继续由原场景 `VREyeRaycaster` 和 `SelectionRadial` 驱动；厂商源码和场景文件均不改写。

## 主要功能

| 功能 | 默认状态与入口 | 输出或效果 | 详细文档 |
|---|---|---|---|
| **Hi5 手指外展/内收解锁** | Play Mode 自动启用 | 关闭厂商 `finger ADB fixed`，保留手指横向自由度 | [FINGER_KINEMATICS.md](docs/FINGER_KINEMATICS.md) |
| **PIP–DIP 耦合** | 可选，需要在手骨骼根节点配置组件 | 按比例约束 DIP 屈伸，同时保留其他旋转分量 | [FINGER_KINEMATICS.md](docs/FINGER_KINEMATICS.md) |
| **手–物体自适应视觉接触** | Play Mode 自动安装到左右可视手 | 手套目标将穿入刚体时，逐指骨贴合表面；不改校准、抓取和采集源 | [HAND_OBJECT_CONTACT.md](docs/HAND_OBJECT_CONTACT.md) |
| **VIVE 视频透视** | Play Mode 按 `P` 开关，默认关闭 | 将 OpenVR Tracked Camera 视频合成到虚拟物体之后 | [MIXED_REALITY.md](docs/MIXED_REALITY.md) |
| **VR 第一视角录像** | Editor Play Mode 按 `F9` 开始/停止 | `Recordings/vr_view_*.mp4` | [VR_VIEW_RECORDING.md](docs/VR_VIEW_RECORDING.md) |
| **统一实验数据采集** | `Capture Control` 配置；Play Mode 按 `F12` 启停 trial、`F11` 标记 | `Captures/participants/<ID>/sessions/...` 下的 CSV、事件、manifest、校验和与同步 MP4 | [DATA_CAPTURE.md](docs/DATA_CAPTURE.md) |
| **机器人抓取任务台** | 打开项目自有 `PickPlaceTasks` 场景，可在 Scene View 编辑 | 5 个可抓物体、5 个目标区及任务完成反馈 | [PICK_PLACE_TASKS.md](docs/PICK_PLACE_TASKS.md) |
| **整场任务复位** | 拍下原场景复位按钮，或按 `F8` | 恢复物体姿态、刚体状态、速度、进度和目标颜色 | [PICK_PLACE_TASKS.md](docs/PICK_PLACE_TASKS.md) |
| **校准后注视功能面板** | 完成 V/B/P-pose 后自动替换主面板内容 | 注视控制透视、VR 视频、trial、标记、复位和重新校准；状态实时显示 | [GAZE_CONTROL_PANEL.md](docs/GAZE_CONTROL_PANEL.md) |

### 机器人任务

| 任务 | 操作物体 | 目标 | 训练价值 |
|---|---|---|---|
| 球入桶 | YCB `055_baseball` | 圆形桶 | 球形抓取、搬运和释放 |
| 杯放定位垫 | YCB `025_mug` | 圆形定位垫 | 杯身/把手抓取与姿态控制 |
| 罐入箱 | YCB `005_tomato_soup_can` | 方形收纳箱 | 圆柱抓取与容器放置 |
| 颜色分类 | 红、蓝规则方块 | 对应颜色箱 | 条件分类和精确放置 |

目标底面在匹配物体进入后变为绿色，Console 会记录任务 ID 和累计完成数。当前成功判据是进入目标触发体积，尚未要求物体释放后保持静止若干帧。

## 硬件与版本要求

### 已验证配置

| 类别 | 设备或软件 | 已验证版本/数量 | 用途 |
|---|---|---|---|
| 头显 | HTC VIVE Pro 2 | 1 台 | VR 显示、头部追踪、前置摄像头透视 |
| 基站 | SteamVR Base Station 2.0 | 2 台 | Lighthouse 空间定位 |
| Tracker | VIVE Tracker 3.0 | 2 台 | 左右手空间位姿 |
| 数据手套 | Noitom Hi5 2.0 | 左右手各 1 只 | 手指姿态与交互输入 |
| Unity | Unity Editor | `2019.4.18f1` | 固定开发和运行版本 |
| SteamVR 插件 | SteamVR Unity Plugin | `2.8.2` | OpenVR 输入、相机和设备接口 |
| OpenVR XR | OpenVR XR Plugin | `1.2.3` | Unity XR 显示后端 |
| Foundation SDK | Hi5 2.0 Foundation SDK | `1.1.0.23` | Hi5 设备和基础手部数据 |
| Interaction SDK | Hi5 2.0 Interaction SDK | `1.1.0.3` | 虚拟手、抓取、碰撞和复位逻辑 |
| 录像 | Unity Recorder | `2.2.0-preview.4` | Editor Play Mode MP4 录像 |

### 兼容性约束

- 必须优先使用 **Unity `2019.4.18f1`**。Unity 6 会导致旧版 Hi5/SteamVR 插件出现 Burst、Mono.Cecil 或 `Assembly-CSharp-Editor` 解析错误。
- 项目当前以 **Windows + SteamVR** 为目标平台。
- VIVE 透视依赖 SteamVR 相机权限和 OpenVR Tracked Camera 接口，不由 Hi5 SDK 提供。
- Hi5 SDK 和用户手册不随本仓库分发，必须从授权来源取得。

## 快速开始

### 1. 获取当前开发版本

```powershell
git clone https://github.com/YushuoWang29/vr-glove-data-capture.git
cd vr-glove-data-capture
git checkout codex/gaze-control-panel
```

默认 `main` 当前仍是初始化基线；在功能分支合并前，应使用上述推荐分支。

### 2. 安装运行环境

1. 通过 Unity Hub 安装 **Unity `2019.4.18f1`**，包含 Windows Build Support。
2. 安装并启动 SteamVR。
3. 将 VIVE Pro 2、两个 Base Station 2.0 和两个 VIVE Tracker 3.0 配对并上线。
4. 在 SteamVR 状态窗口确认头显、基站和 Tracker 均为绿色。

### 3. 打开 Unity 项目

在 Unity Hub 中添加并打开：

```text
Unity Project/vr-glove-data-capture
```

首次打开时等待 Package Manager 恢复依赖并完成脚本编译。不要删除仓库中的 `Assets/SteamVR/OpenVRUnityXRPackage`，OpenVR XR 依赖从该本地包恢复。

### 4. 导入 Hi5 SDK

按以下顺序导入本地授权资源：

1. **Hi5 2.0 Foundation SDK `1.1.0.23`**。
2. 等待 Unity 编译完成，确认生成 `Assets/NoitomHi5`。
3. **Hi5 2.0 Interaction SDK `1.1.0.3`**。
4. 再次等待编译完成，确认生成 `Assets/Hi5_Interaction_SDK`。
5. 若 SteamVR 提示生成或更新 Input Actions，按提示保存并重新载入。

这两个厂商目录被 `.gitignore` 排除，不应强制加入 Git。

### 5. 完成手套与 Tracker 校准

1. 启动 Hi5 手套和厂商运行时，确认左右手均被识别。
2. 打开：

   ```text
   Assets/NoitomHi5/Scenes/Vive/Calibration.unity
   ```

3. 进入 Play Mode，佩戴头显。
4. 按厂商说明通过注视启动校准，并完成 V-pose/手套与 Tracker 对齐。
5. 确认左右虚拟手的位置、朝向和手指动作基本正确。

### 6. 运行完整交互与任务 Demo

打开项目自有任务场景：

```text
Assets/VRGloveDataCapture/Scenes/TaskSetups/PickPlaceTasks.unity
```

也可以按 **`Ctrl+Shift+F6`**，或执行：

```text
Tools > VR Glove Data Capture > Task Setups > Open Pick Place Task Setup
```

打开后，Hierarchy 中应同时出现两个 Scene：

| Scene | 所有权 | 作用 | 是否直接编辑 |
|---|---|---|---|
| `TableScene_Vive` | Hi5 原厂、本地导入 | 校准流程、虚拟手、状态面板、原厂物品、实体复位按钮 | 否 |
| `PickPlaceTasks` | 本仓库 | YCB 任务物体、容器、目标区、任务控制器 | 是 |

进入 Play Mode 后，项目会自动执行以下扩展：

- 启用手指外展/内收模式。
- 给左右 Hi5 可视手安装手–物体自适应接触求解器；仅显示层生效。
- 在主相机上安装透视组件。
- 安装 `F9` VR 录像热键。
- 安装独立于 Scene 的统一采集管理器；任务物体会自动加入物体轨迹，任务成功和复位会自动加入事件流。
- 将场景中已经可见、可编辑的 YCB 任务物体注册到 Hi5 simple-object manager。
- 将新增物体注册到 Hi5 simple-object manager。
- 订阅场景原有的统一复位消息。
- 完成原厂 V/B/P-pose 校准后，将原厂主窗口自动切换为注视功能控制中心。

### 7. 校准后使用注视功能面板

1. 按原厂流程注视 **Calibrate**，依次完成 V-pose、B-pose 和 P-pose。
2. P-pose 完成后，主窗口自动显示六个功能按钮；保持视线直到原厂圆形进度完成即可触发。
3. 面板按钮可控制 **Passthrough、VR View Video、Trial Capture、Event Marker、Reset Scene、Recalibrate**。
4. **Recalibrate** 会返回原厂校准界面；活动 trial 或视频未停止时会拒绝进入，避免截断实验记录。

完整状态、限制和排障见 [GAZE_CONTROL_PANEL.md](docs/GAZE_CONTROL_PANEL.md)。

### 8. 常用按键与注视操作

| 按键/操作 | 功能 | 成功判据 |
|---|---|---|
| `P` | 开启/关闭 VIVE 视频透视 | Console 出现 `Passthrough Streaming: LIVE` 才表示收到连续相机帧 |
| `F9` | 开始/停止 VR 第一视角录像 | `Recordings/` 中生成 MP4 |
| `F12` | 开始/停止并最终化一个统一采集 trial | Console 出现 `TRIAL STARTED` / `TRIAL FINALIZED`，trial 目录出现 `COMPLETE` |
| `F11` | 在活动 trial 中写入人工事件标记 | `events/events.csv` 出现 `manual_marker` |
| `F8` | 发布全场复位消息 | 新增物体、任务进度和目标颜色恢复 |
| 校准后注视功能按钮 | 与 `P/F9/F12/F11/F8` 相同的公共控制接口，并提供重新校准 | 按钮第二行实时显示 `LIVE`、`RECORDING`、`READY` 或不可用原因 |
| `Ctrl+Shift+F7`（Edit Mode） | 运行 pick-and-place Play Mode 冒烟测试 | Console 出现 `passed=1, failed=0, skipped=0` |
| `Ctrl+Shift+F10`（Edit Mode） | 运行项目全部 4 项 Play Mode 回归测试 | Console 出现 `passed=4, failed=0, skipped=0` |
| 场景实体复位按钮 | 与 `F8` 相同的统一复位 | 原厂物体和新增任务物体同时恢复 |

三个 Edit Mode 命令使用 Unity `Shortcut` API 注册，可在 `Edit > Shortcuts` 的 **VR Glove Data Capture** 分类下重新绑定。项目不再占用无修饰键的 `F6`、`F7` 和 `F10`，从而避免与 Terrain 和 Recorder 默认快捷键冲突。Play Mode 的 `F8/F9/F11/F12` 是 Game View 运行时输入，不注册为编辑器全局命令。

### 9. 采集结构化示教数据

1. 执行 `Tools > VR Glove Data Capture > Capture Control`。
2. 填写匿名 Participant ID、Session Label、Task ID 和 Condition。
3. 保持 **Require Hi5 Skeleton** 开启；需要视频时保持 **Record VR View Video** 开启。
4. 进入 Play Mode，确认控制窗口显示 Hi5 Bones 后按 `F12`。
5. 完成任务，必要时按 `F11` 添加人工标记，再按 `F12` 收尾。
6. 点击控制窗口的 **Open Latest Trial**，检查 `manifest.json`、`COMPLETE` 和所需数据流。

完整目录、字段、时间戳语义、九轴 IMU 能力边界和质量检查见 [DATA_CAPTURE.md](docs/DATA_CAPTURE.md)。

## 项目目录

```text
vr-glove-data-capture/
├─ README.md
├─ LICENSE                         # 原创代码 MIT License
├─ THIRD_PARTY_NOTICES.md          # 第三方组件和数据集声明
├─ LICENSES/
│  ├─ SteamVR-BSD-3-Clause.txt
│  └─ YCB-CC-BY-4.0.txt
├─ docs/
│  ├─ SETUP.md                     # 硬件、SDK 和 Unity 配置
│  ├─ FINGER_KINEMATICS.md         # 手指自由度与 PIP–DIP 耦合
│  ├─ HAND_OBJECT_CONTACT.md        # 可视手指表面接触与数据隔离
│  ├─ GAZE_CONTROL_PANEL.md         # 校准后注视面板、状态与安全约束
│  ├─ MIXED_REALITY.md             # VIVE 视频透视
│  ├─ VR_VIEW_RECORDING.md         # VR 第一视角录像
│  ├─ DATA_CAPTURE.md               # session/trial、多流数据、时间戳和数据字典
│  ├─ PICK_PLACE_TASKS.md          # 机器人任务和复位机制
│  └─ YCB_ASSET_MANIFEST.md        # YCB 来源、裁剪范围和哈希
└─ Unity Project/
   └─ vr-glove-data-capture/
      ├─ Assets/
      │  ├─ SteamVR/               # Valve SteamVR Unity Plugin
      │  ├─ VRGloveDataCapture/
      │  │  ├─ Runtime/
      │  │  │  ├─ FingerKinematics/
      │  │  │  ├─ HandInteraction/
      │  │  │  ├─ MixedReality/
      │  │  │  ├─ Capture/
      │  │  │  ├─ RoboticsTasks/
      │  │  │  └─ UserInterface/    # 校准状态桥接和注视功能控制中心
      │  │  ├─ Editor/             # Recorder 桥接和资源验证器
      │  │  ├─ Scenes/TaskSetups/  # Git 管理、可直接编辑的任务场景
      │  │  ├─ Materials/          # 任务场景持久化材质
      │  │  ├─ Resources/          # YCB 轻量模型子集
      │  │  └─ Tests/PlayMode/     # 任务生成与复位冒烟测试
      │  ├─ NoitomHi5/             # 本地导入，Git 忽略
      │  └─ Hi5_Interaction_SDK/   # 本地导入，Git 忽略
      ├─ Packages/
      └─ ProjectSettings/
```

Unity 生成的 `Library`、`Temp`、`Logs`、`UserSettings`、构建输出，以及实验产生的 `Captures`、`Recordings` 不进入 Git。

## 验证与测试

### Unity 菜单验证

在 Unity 中运行：

```text
Tools > VR Glove Data Capture > Validate Pick Place Task Assets
```

验证器会检查：

- 三个 YCB 模型是否能够通过 `Resources.Load` 载入。
- 模型材质是否成功关联纹理。
- `Hi5ObjectGrasp`、`Hi5Plane` 和 `Hi5ObjectTrigger` 层是否位于正确索引。
- 项目自有 `PickPlaceTasks.unity` 是否存在。
- 本地 `TableScene_Vive` 是否存在。

### Play Mode 自动测试

Test Runner 中的测试：

```text
PickPlaceTaskSceneSmokeTests.EditableTaskSceneBindsFiveTasksAndHi5ResetRestoresTheirPoses
```

可以直接按 `Ctrl+Shift+F7`，或执行 `Tools > VR Glove Data Capture > Task Setups > Run Pick Place Play Mode Test` 运行该测试。

该测试会先加载项目自有任务场景，再叠加加载真实厂商场景，并验证：

1. 持久化场景包含 5 个可抓物体和 5 个目标区。
2. 五项匹配放置均能触发成功。
3. `messageObjectReset` 能恢复全部物体的位置和 kinematic 状态。
4. 刚体线速度与角速度归零。
5. 任务进度和目标颜色恢复。

当前开发版本已经在 Unity `2019.4.18f1` 下通过资源验证和上述 Play Mode 测试。

手–物体自适应视觉接触测试为：

```text
AdaptiveHandContactSmokeTests.SolverAutoInstallsOnBothVisibleHandsWithoutWritingSourceBones
```

该测试会加载真实 Hi5 场景，验证左右手自动安装、源/显示骨骼隔离、刚体接触时源姿态零写入，并构造一个食指–球面探针接触来确认只截停可视指骨。

校准后注视面板测试为：

```text
GazeControlPanelSmokeTests.PanelPreservesCalibrationAndRoutesVendorGazeToProjectControls
```

该测试验证原厂校准对象不被删除、六个按钮均带有原厂 `VRInteractiveItem` 和 gaze collider、驻留完成可调用透视控制，以及重新校准会恢复原厂校准面板。按 **`Ctrl+Shift+F10`**（Edit Mode）或执行 `Tools > VR Glove Data Capture > Run All Project Play Mode Tests` 可以一次运行注视面板、手–物体接触、pick-and-place 复位和统一采集四项测试；当前结果为 `passed=4, failed=0, skipped=0`。

统一采集的端到端测试为：

```text
UnifiedCaptureSmokeTests.TrialFinalizesAtomicMachineReadableStreamsAndManifest
```

执行 `Tools > VR Glove Data Capture > Data Capture > Run Unified Capture Play Mode Test`。测试会使用并清理专用的 `AUTOMATED_CAPTURE_TEST` 目录，验证主要数据流、事件、manifest、SHA-256、`COMPLETE` 和 `.partial` 清理。

## 常见问题

### Unity 报 `Assembly-CSharp-Editor` 或 Mono.Cecil 解析失败

优先检查 Unity 版本。该 SDK 组合固定使用 `2019.4.18f1`；不要直接使用 Unity 6 打开旧版 Hi5 工程。

### SteamVR 中 Tracker 在线，但虚拟手位置错误

重新运行 `Calibration.unity` 的 V-pose 对齐。Tracker 在线只表示设备可见，不表示手套坐标系已经与 Tracker 对齐。

### 按 `P` 后没有真实世界画面

检查 SteamVR 的 Camera 权限并重启 SteamVR。`Passthrough is ready` 只表示组件安装完成，只有 `Passthrough Streaming: LIVE` 才表示收到了连续图像。

### 按 `F9` 没有开始录像

确认当前处于 **Unity Editor Play Mode**，并让 Game View 获得键盘焦点。录像功能依赖 Editor-only 的 Unity Recorder，不是 Windows Player 录像器。

### 按 `F12` 无法开始统一采集

先打开 `Capture Control` 查看错误状态。默认会拒绝在没有 Hi5 骨骼的情况下开始，避免生成看似完整但没有手部轨迹的数据；确认已经加载 VIVE 基础场景、完成校准并进入 Play Mode。只有调试采集文件结构时才关闭 **Require Hi5 Skeleton**。

### 打开 `TableScene_Vive` 没看到机器人任务台

这是双场景结构的预期行为。请打开 `Assets/VRGloveDataCapture/Scenes/TaskSetups/PickPlaceTasks.unity`，或按 `Ctrl+Shift+F6`。编辑器会自动叠加加载原厂 `TableScene_Vive`；如果未加载，先运行资源验证菜单检查本地 SDK 是否完整。

## 数据与版本管理

### 数据策略

以下内容不得直接提交到源码仓库：

- 受试者身份信息和原始实验记录。
- 大规模九轴传感器数据、视频和中间导出文件。
- 标定导出、缓存和临时采集文件。
- 仓库根目录 `Captures/` 下的 session/trial、CSV、事件、manifest、校验和与视频。
- 未经授权的厂商 SDK、PDF、安装包和示例构建。

仓库只保存源码、配置、数据结构、可再分发的小型测试资源以及明确授权的数据集子集。

### Git 工作流

- 稳定基线使用 `main`。
- 功能开发使用 `codex/<topic>` 短生命周期分支。
- 一个版本应包含代码、Unity `.meta` 文件、测试和对应文档。
- 合并前至少完成 Unity 编译、相关功能验证和 `git diff --check`。

详细约定见 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 许可证与第三方资源

- 项目原创源码采用 [MIT License](LICENSE)。
- SteamVR Unity Plugin 保留 Valve 的 BSD 3-Clause 条款。
- YCB 模型子集采用 **Creative Commons Attribution 4.0 International**，并保留数据集作者署名和改动说明。
- Hi5 2.0 SDK 不随仓库分发，使用者必须遵守厂商授权条款。

完整声明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) 和 [LICENSES](LICENSES/)。
