# VR Glove Data Capture

基于 **Unity、HTC VIVE Pro 2、VIVE Tracker 3.0 与 Noitom Hi5 2.0** 的手部运动采集研究项目。本项目面向标定后手部追踪、扩展手指运动学、混合现实可视化、同步数据记录与可复现实验流程。

## 当前基线

- Unity `2019.4.18f1`
- SteamVR Unity Plugin `2.8.2`
- OpenVR XR Plugin `1.2.3`
- Hi5 2.0 Foundation SDK `1.1.0.23`（本地导入）
- Hi5 2.0 Interaction SDK `1.1.0.3`（本地导入）
- HTC VIVE Pro 2、两台 SteamVR Base Station 2.0、两台 VIVE Tracker 3.0

厂商标定与交互示例已在上述物理硬件上验证。仓库中的原创代码用于增加约束手指运动学、前置摄像头透视实验、数据采集和运行时诊断。

## 已实现的扩展

- **Hi5 手指外展/内收解锁**：运行时自动关闭厂商 SDK 的 `finger ADB fixed` 模式，不修改本地厂商源码。
- **可选 PIP–DIP 耦合**：在 `LateUpdate` 中按比例约束 DIP 屈伸，同时保留原有外展/内收和非屈伸旋转。
- **VIVE Pro 2 视频透视**：使用 OpenVR Tracked Camera 将真实世界视频合成到虚拟物体之后；Play Mode 中按 `P` 开关。
- **VR 第一视角 MP4 录像**：Unity Editor Play Mode 中按 `F9` 开始/停止录制 Game View（头显桌面镜像），视频写入本地 `Recordings/`。
- **机器人抓取任务台**：在 Hi5 `TableScene_Vive` 中自动生成球入桶、杯放定位垫、罐入箱和红蓝方块分类任务，并接入场景原有的实体复位按钮。

设计、配置和验证方法见 [手指运动学文档](docs/FINGER_KINEMATICS.md)、[混合现实透视文档](docs/MIXED_REALITY.md)、[VR 视角录像文档](docs/VR_VIEW_RECORDING.md) 与 [机器人抓取任务文档](docs/PICK_PLACE_TASKS.md)。

## 仓库结构

```text
Unity Project/
  vr-glove-data-capture/
    Assets/
      SteamVR/             Valve SteamVR Unity Plugin
      VRGloveDataCapture/  本项目原创代码与资源
    Packages/              Unity 包清单与锁文件
    ProjectSettings/       Unity 项目设置
docs/                      安装、设计与验证文档
```

Unity 生成的 `Library`、`Temp`、`Logs`、`UserSettings`、构建输出、原始实验数据以及本地厂商 SDK 均不进入 Git。

## 本地安装

1. 安装 Unity `2019.4.18f1` 与 SteamVR。
2. 克隆仓库并打开 `Unity Project/vr-glove-data-capture`。
3. 从 Noitom 的授权来源获得 Hi5 2.0 SteamVR Foundation SDK 和 Interaction SDK。
4. 先导入 Foundation SDK，再导入 Interaction SDK。本地生成的 `Assets/NoitomHi5` 与 `Assets/Hi5_Interaction_SDK` 目录由 Git 忽略。
5. 打开厂商场景 `Assets/NoitomHi5/Scenes/Vive/Calibration.unity`，完成校准后再测试项目功能。

完整流程见 [docs/SETUP.md](docs/SETUP.md)。

## 数据策略

原始采集记录、受试者数据、标定导出和生成的数据集不得提交到 Git。应将其存入获批准的研究数据位置；仓库只保存数据结构、合成示例或已明确获准的去标识样本。

## 开发流程

默认分支为 `main`。开发使用短生命周期的 `codex/<topic>` 分支和 Pull Request，详见 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 许可证

原创项目代码使用 [MIT License](LICENSE)。第三方组件保留其自身条款，见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) 与 `LICENSES/`。
