# 已验证的硬件与 Unity 配置

## 支持基线

- Unity `2019.4.18f1`
- SteamVR Unity Plugin `2.8.2`
- OpenVR XR Plugin `1.2.3`
- Hi5 2.0 Foundation SDK `1.1.0.23`
- Hi5 2.0 Interaction SDK `1.1.0.3`
- HTC VIVE Pro 2
- 两台 SteamVR Base Station 2.0
- 两台 VIVE Tracker 3.0，每只手一台

## SDK 导入顺序

1. 用 Unity Hub 打开 `Unity Project/vr-glove-data-capture`，等待 Package Manager 和脚本编译完成。
2. 导入 Hi5 2.0 Foundation SDK，确认生成 `Assets/NoitomHi5`。
3. 导入 Hi5 2.0 Interaction SDK，确认生成 `Assets/Hi5_Interaction_SDK`。
4. 若 SteamVR 提示生成输入动作，按其提示保存并重新载入项目。
5. 不要将上述两个厂商目录加入 Git；它们受厂商条款约束并包含超过 GitHub 单文件限制的 Android 二进制。

## 硬件到 Unity 的连接顺序

1. 启动 SteamVR，确认头显、两个基站和两个 Tracker 均为绿色，并等待状态显示 **Ready**。
2. 启动 Hi5 手套并确认厂商运行时已识别左右手。
3. 在 Unity 执行 `Tools > VR Glove Data Capture > VR Runtime > Validate SteamVR and HMD`，确认 OpenVR 与 HMD 均已就绪。
4. 在 Unity 中打开 `Assets/NoitomHi5/Scenes/Vive/Calibration.unity`。
5. 进入 Play Mode，佩戴头显，按厂商示例中的注视流程完成 V-pose 校准。
6. 完成校准后加载 Hi5 Interaction SDK 示例场景，验证左右手位置、姿态、抓取和碰撞。
7. 在此基线上启用 `VRGloveDataCapture` 组件；功能配置见对应设计文档。

## 扩展功能快速验证

1. 进入任一 Hi5 校准完成后的交互场景。
2. Console 出现 `Hi5 finger abduction/adduction is enabled` 后，先张开手掌，再分别完成拇指–食指与拇指–小指对指。
3. 若需要强制 PIP–DIP 生理耦合，将 `FingerJointCoupling` 添加到左右 `Noitom_*Hand` 骨骼根节点；保持手掌自然伸直并执行组件菜单 `Capture current pose as coupling neutral`。
4. 在 SteamVR 的 `Settings > Camera` 中启用 VIVE Pro 2 双摄像头并重启 SteamVR。
5. 回到 Play Mode，按键盘 `P` 启用视频透视；以 Console 出现 `Passthrough Streaming: LIVE` 作为软件确实收到连续相机帧的判据。
6. 在 Game View 聚焦时按 `F9` 开始 VR 第一视角录像，再按 `F9` 停止；MP4 输出到 Unity 项目根目录的 `Recordings/`。
7. 打开 `Assets/VRGloveDataCapture/Scenes/TaskSetups/PickPlaceTasks.unity`，或按 `Ctrl+Shift+F6`。编辑器会自动把原厂 `TableScene_Vive` 叠加加载，因此 Hierarchy 中应同时看到原厂基础场景和项目任务场景。进入 Play Mode，完成任意任务后拍下原厂场景的复位按钮，确认全部新增物体及目标状态一并恢复；`F8` 是同一复位消息的键盘备用入口。

详见 [FINGER_KINEMATICS.md](FINGER_KINEMATICS.md)、[MIXED_REALITY.md](MIXED_REALITY.md)、[VR_VIEW_RECORDING.md](VR_VIEW_RECORDING.md) 与 [PICK_PLACE_TASKS.md](PICK_PLACE_TASKS.md)。

## 已验证状态

已在实际硬件上完成以下闭环：SteamVR 设备上线、Unity 示例场景运行、头显显示、注视触发校准、虚拟手驱动及与虚拟物品交互。

## 常见故障边界

- `Assembly-CSharp-Editor`/Burst 解析失败通常说明 Unity 版本或 SDK 编译链不匹配；本项目固定使用 Unity 2019.4.18f1。
- 若 Play Mode 已进入但头显没有 Unity 画面，先退出 Play Mode；OpenVR `Not Initialized (109)` 表示进入时 SteamVR 尚未就绪。等待 SteamVR Ready，并通过项目的 VR Runtime 验证菜单后再启动。项目会对任务 Scene 和原厂交互 Scene 自动执行这一预检。
- Tracker 已在 SteamVR 中上线但手的位置错误时，应先重新运行厂商 V-pose 校准，而不是修改模型骨骼。
- 透视功能依赖 SteamVR 相机权限、VIVE Pro 2 前置摄像头和 OpenVR Tracked Camera 接口，不能由 Hi5 SDK 替代。
- `Passthrough is ready` 只表示组件已装到相机；只有 `Passthrough Streaming: LIVE` 才表示 OpenVR 已返回连续相机帧。
- VR 录像基于 Unity Recorder `2.2.0-preview.4`，仅在 Unity Editor Play Mode 工作，不属于 Windows Player 运行时录像功能。
- 手指增强是厂商解算后的约束后处理；它不能从不可观测数据中恢复每个关节的独立真实角度。
