# 校准后注视功能面板

## 功能目标

**校准后注视功能面板**复用 Hi5 原厂 UI 已验证的视线射线与驻留圆环，把原来依赖键盘的运行时功能放到头显内操作。完整校准之前，原厂主界面、状态提示和 V/B/P-pose 校准流程保持不变；完整 P-pose 结束后，同一个主窗口区域自动显示功能控制中心。

该功能不会修改 `Assets/NoitomHi5`、`Assets/Hi5_Interaction_SDK` 或原厂 `.unity` 场景。运行时安装器仅在检测到原厂 `MenuStateMachine`、`VREyeRaycaster`、`SelectionRadial` 和 `VRInteractiveItem` 后创建面板，因此没有加载原厂 UI 的其他场景不会出现空面板。

进入场景且尚未显示功能面板时，原厂主入口保持为从左到右的 **Calibrate、Reconnect、Interaction**。原厂资源中的遗留 `Interaction` 对象默认与 `Reconnect` 占用同一位置，且仍绑定 `ReConnect` 状态；项目适配器会在运行时把它移动到 `Reconnect` 右侧、复制原 `Close` 槽位的 Interaction/Exit 状态，并停用被替代的 `Close` 入口。该处理只改变当前 Play Mode 实例，不保存或改写原厂 Scene。

## 使用流程

### 首次校准

1. 打开包含 `TableScene_Vive` 的交互场景并进入 **Play Mode**。
2. 使用原厂主面板的 **Calibrate** 注视按钮进入校准。
3. 按原厂提示完成 **V-pose、B-pose、P-pose**。
4. 完整 P-pose 完成后，系统自动返回主窗口并显示 **VR GLOVE CONTROL CENTER**。
5. 注视任一功能按钮并保持视线，直到原厂圆形进度完成。

功能按钮**不会在校准前显示**。运行时控制器直接读取厂商完整类型 `HI5.HI5_Manager_Thread.IsCalibrationComplete`，同时保留对旧无命名空间版本的兼容；因此完成 P-pose 后，即使厂商菜单状态已经从 `Finish` 返回主页面，面板仍能可靠识别校准完成。

驻留等待时间和填充时间直接沿用当前场景中原厂 `SelectionRadial` 的序列化配置，不在项目扩展中另设第二套计时器。`TableScene_Vive` 当前配置为约 **0.5 秒等待 + 1.0 秒填充**。

### 重新校准

1. 注视 **RECALIBRATE**。
2. 功能面板立即隐藏，并恢复原厂校准界面。
3. 重新完成 V/B/P-pose。
4. P-pose 完成后功能面板再次自动出现。

若 **trial 采集**或**VR 视频录制**仍在进行，重新校准命令会被拒绝，并显示 `STOP TRIAL AND VIDEO RECORDING BEFORE RECALIBRATION`。该限制用于防止校准流程无意截断或污染正在录制的实验。

## 六个注视控制

| 按钮 | 等价入口 | 行为 | 关键状态或输出 |
|---|---|---|---|
| **PASSTHROUGH** | `P` | 开启或关闭 VIVE Pro 2 前置摄像头 video see-through | 只有 `LIVE` 表示持续收到真实相机帧；等待、停帧和不可用会直接显示在按钮上 |
| **VR VIEW VIDEO** | `F9` | 开始或停止独立 VR 视角 MP4 录制 | Editor Play Mode 输出到 `Recordings/vr_view_*.mp4`；活动统一 trial 管理同步视频时遵守原有互锁 |
| **TRIAL CAPTURE** | `F12` | 开始 trial，或停止并原子化收尾 | 输出到 `Captures/participants/<ID>/sessions/...`；开始失败时底部提示显示 `LastError` |
| **EVENT MARKER** | `F11` | 向活动 trial 写入 `manual_marker` | 标签为 `gaze_panel`，与其他流共享 trial 单调时间戳；无活动 trial 时禁用 |
| **RESET SCENE** | `F8` / 原厂实体复位按钮 | 发布同一个 `messageObjectReset`，恢复原厂和新增任务物体 | pick-and-place 物体、刚体速度、目标颜色和任务进度一起恢复 |
| **RECALIBRATE** | 原厂 Calibrate | 返回原厂 V/B/P-pose 流程 | trial 或视频活动时拒绝执行；完成后自动返回功能面板 |

键盘入口和原厂实体按钮继续保留，便于桌面调试和故障回退。注视入口与键盘入口调用同一公共控制方法，不通过模拟按键触发。

## 状态显示

### 按钮颜色

| 颜色 | 含义 | 典型状态 |
|---|---|---|
| **灰白色** | 可操作、当前未活动 | Passthrough Off、Video Idle、Trial Ready |
| **浅绿色** | 功能已活动或当前可执行 | Passthrough Live、Event Marker Ready |
| **浅红色** | 正在写入持续性数据 | Video Recording、Trial Recording |
| **浅黄色** | 需要谨慎确认或仍在等待 | Scene Reset、Passthrough WaitingForFrame |
| **浅灰色** | 当前不可用 | 相机组件尚未安装、无活动 trial 时的 Event Marker |

面板采用与原厂白色校准界面接近的 **灰白底、灰黑字、细灰边框和圆角矩形**。每个按钮把功能标题和状态拆成两行，并按按钮渲染边界自动缩放，长状态不会再越出按钮。面板底部提示显示最近一次命令的结果。透视按钮中的 **LIVE** 是确认真实世界透视生效的主要判据；`REQUESTED`、`WaitingForSteamVr` 或 `WaitingForFrame` 只表示请求已经发出，不代表已经收到连续视频帧。

## 实现边界

### 保留的原厂行为

- 原厂 `MenuStateMachine` 和 `CalibrationStateMachine` 仍负责校准页面切换。
- 原厂 `VREyeRaycaster` 仍负责从头显中心发出视线射线。
- 原厂 `VRInteractiveItem` 仍负责 `OnOver` / `OnOut` 焦点事件。
- 原厂 `SelectionRadial` 仍负责等待、进度显示和完成事件。
- 原厂状态面板、校准入口和校准数据保存逻辑没有被删除或替换。
- 原厂主入口的 `Reconnect` 保持原位置；`Interaction` 使用其右侧独立碰撞区，注视两者不会再命中同一个按钮。

### 项目扩展行为

- `GazeFunctionPanelController` 在运行时找到原厂主状态节点，并在完整校准后隐藏原主页面子项、显示项目功能面板。
- `GazeDwellButton` 通过反射订阅原厂事件，避免仓库源码对不随 Git 分发的 Hi5 程序集形成编译时依赖。
- 重新校准时原主页面和校准状态会恢复；完整 P-pose 后再次切换到功能面板。
- 面板只负责调用公共命令，不写入手套校准、源骨骼、关节姿态或采集数据。

## 验证

### 自动测试

执行：

```text
Tools > VR Glove Data Capture > Run All Project Play Mode Tests
```

或在 Edit Mode 按 **`Ctrl+Shift+F10`**。注视面板测试为：

```text
GazeControlPanelSmokeTests.PanelPreservesCalibrationAndRoutesVendorGazeToProjectControls
```

测试实际加载本地 `TableScene_Vive`，验证原厂校准对象仍存在、`Interaction` 位于 `Reconnect` 右侧且碰撞体不重叠、入口绑定 Interaction/Exit 状态、六个功能按钮均接入原厂视线交互、完成注视能够切换透视请求，以及重新校准能够恢复经过适配的原厂主界面。视觉断言还会检查背景亮度、灰黑文字、圆角网格和文字不越界；透视断言会检查 `VerticalStereo` 的左右眼区域映射及各眼内部 180° 旋转不越区。当前完整结果为 **4 项通过、0 失败、0 跳过**。

### 头显验收

1. 刚进入 Play Mode 时确认 `Reconnect` 居中、`Interaction` 位于其右侧；分别注视后应进入各自状态，`Interaction` 不再触发 Reconnect。
2. 完成 P-pose 后确认功能面板自动出现，原厂圆形视线进度仍可见。
3. 注视 **PASSTHROUGH**，等待按钮显示 `LIVE`，确认能看到真实环境和虚拟物体叠加。
4. 注视 **VR VIEW VIDEO** 开始和停止一次录制，检查 `Recordings/` 中 MP4 能正常播放。
5. 在 `Capture Control` 配置元数据后注视 **TRIAL CAPTURE**，添加一次 **EVENT MARKER**，再停止 trial；检查 `COMPLETE` 和 `events/events.csv`。
6. 移动物体后注视 **RESET SCENE**，确认原厂物体和 pick-and-place 物体同时复位。
7. 注视 **RECALIBRATE**，确认功能面板消失、原厂 V/B/P-pose 页面恢复，完成后功能面板再次出现。

## 常见问题

### 完成校准后仍显示原厂主页面

确认已经完成完整 **P-pose**，而不是只完成 V-pose 或 B-pose。功能切换以厂商 `CalibrationStateMachine.Finish` 或 `HI5.HI5_Manager_Thread.IsCalibrationComplete` 为依据。若 Console 没有出现 `[GazeControlPanel] Installed`，检查当前场景是否实际加载了原厂 `TableScene_Vive` 及其 UI 对象；若日志已经出现 Installed 但完成 P-pose 后仍无按钮，应先确认当前 Git 分支包含本次命名空间修复，而不是重复校准。

### 看得到按钮但注视没有进度

确认头显中心准星确实落在按钮表面；按钮之间保留了空隙，用于让原厂射线可靠地产生 `OnOut` 后再进入下一个按钮。若原厂 Calibrate 按钮也无法产生进度，应先排查原厂 `VREyeRaycaster`、`SelectionRadial` 和相机引用，而不是项目功能面板。

### 透视按钮停在等待状态

检查 SteamVR Camera 权限并重启 SteamVR。只有按钮显示 **LIVE** 才代表 OpenVR Tracked Camera 正在连续送帧；详细排障见 [MIXED_REALITY.md](MIXED_REALITY.md)。

### VR 视频按钮不可用

独立 MP4 录制当前依赖 Editor-only 的 Unity Recorder，只在 **Unity Editor Play Mode** 可用。Windows Standalone Player 中按钮会报告不可用，不会伪造成功状态。
