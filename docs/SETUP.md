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

1. 启动 SteamVR，确认头显、两个基站和两个 Tracker 均为绿色。
2. 启动 Hi5 手套并确认厂商运行时已识别左右手。
3. 在 Unity 中打开 `Assets/NoitomHi5/Scenes/Vive/Calibration.unity`。
4. 进入 Play Mode，佩戴头显，按厂商示例中的注视流程完成 V-pose 校准。
5. 完成校准后加载 Hi5 Interaction SDK 示例场景，验证左右手位置、姿态、抓取和碰撞。
6. 在此基线上启用 `VRGloveDataCapture` 组件；功能配置见对应设计文档。

## 已验证状态

已在实际硬件上完成以下闭环：SteamVR 设备上线、Unity 示例场景运行、头显显示、注视触发校准、虚拟手驱动及与虚拟物品交互。

## 常见故障边界

- `Assembly-CSharp-Editor`/Burst 解析失败通常说明 Unity 版本或 SDK 编译链不匹配；本项目固定使用 Unity 2019.4.18f1。
- Tracker 已在 SteamVR 中上线但手的位置错误时，应先重新运行厂商 V-pose 校准，而不是修改模型骨骼。
- 透视功能依赖 SteamVR 相机权限、VIVE Pro 2 前置摄像头和 OpenVR Tracked Camera 接口，不能由 Hi5 SDK 替代。
- 手指增强是厂商解算后的约束后处理；它不能从不可观测数据中恢复每个关节的独立真实角度。
