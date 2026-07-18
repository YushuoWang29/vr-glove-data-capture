# VIVE Pro 2 视频透视混合现实

## 可行性结论

**可以实现**同时看见真实环境和 Unity 虚拟手/物体，但 VIVE Pro 2 是不透明显示头显，因此这里实现的是前置双摄像头驱动的 **video see-through（视频透视）**，不是光学透明式 AR。

HTC 官方说明确认 VIVE Pro 2 提供双摄像头，需在 SteamVR 的 `Settings > Camera` 中启用并重启 SteamVR；官方还提供 Room View。项目代码使用同一类相机硬件，但通过 Valve SteamVR Unity Plugin 的 `SteamVR_TrackedCamera`/OpenVR Tracked Camera 接口把视频帧交给 Unity 合成。[HTC：启用双摄像头](https://www.vive.com/cn/support/vive-pro2/category_howto/activating-the-dual-camera.html)，[Valve：OpenVR SDK](https://github.com/ValveSoftware/openvr)，[Valve：SteamVR Unity Plugin](https://github.com/ValveSoftware/steamvr_unity_plugin)

## 已实现工作流

1. `SteamVrPassthroughAutoInstaller` 在运行时寻找当前主相机或立体相机。
2. 它添加 `SteamVrPassthroughEffect`，但默认保持关闭，避免改变现有校准 demo。
3. 按键盘 `P` 后，组件通过 `SteamVR_TrackedCamera.Source(true)` 获取 HMD 的去畸变视频流。
4. 相机背景改为透明纯色并请求深度纹理。
5. `VRGlovePassthrough.shader` **只使用相机深度缓冲判断虚拟几何覆盖率**：远平面像素显示真实视频，存在 Unity 几何的像素显示虚拟手和物体。不能使用 Game View Alpha 作为遮罩，因为 XR 渲染目标的 Alpha 可能恒为 1，导致相机画面被完全盖住。
6. 再按 `P` 会对称释放视频服务，并恢复原 Camera 设置。

核心文件：

```text
Assets/VRGloveDataCapture/Runtime/MixedReality/
  SteamVrPassthroughAutoInstaller.cs
  SteamVrPassthroughEffect.cs
  VRGlovePassthrough.shader
```

## 首次启用

1. 打开 SteamVR。
2. 进入 `Settings > Camera`。
3. 打开 `Enable Camera`。
4. 按 SteamVR 提示执行 `Restart SteamVR`。
5. 先用 SteamVR 自带 Room View 验证摄像头确实工作。
6. 等待 SteamVR 状态变为 **Ready**，在 Unity 执行 `Tools > VR Glove Data Capture > VR Runtime > Validate SteamVR and HMD`。
7. 检查通过后启动 Unity Play Mode，等待 Console 出现 `Passthrough is ready ... press P to toggle it`。这只表示组件安装完成，不代表摄像头已经出帧。
8. 按 `P` 或在完整校准后注视 **PASSTHROUGH**，观察状态依次进入 `WaitingForSteamVr`、`WaitingForFrame` 和 `Streaming`。
9. 以 `Passthrough Streaming: LIVE: VIVE tracked-camera frames are being composited (宽x高)` 作为**软件已经收到连续相机帧**的判据；同时确认真实画面在背景、虚拟手和交互物品在前景。

`SteamVrPassthroughEffect.SetPassthroughEnabled(bool)` 和 `TogglePassthrough()` 是公共方法，可直接连接 Unity UI 或后续 SteamVR Input Action，不依赖键盘。

## 运行状态诊断

| State | 含义 | 处理 |
|---|---|---|
| `Disabled` | 透视关闭 | 按 `P` 或调用公开方法 |
| `WaitingForSteamVr` | OpenVR 相机服务尚未就绪 | 先启动 SteamVR，确认 OpenVR Loader 已启用 |
| `CameraUnavailable` | SteamVR 未向应用暴露 HMD 相机 | 在 SteamVR 启用 Camera 并重启 SteamVR |
| `WaitingForFrame` | 已获取服务，尚未收到首帧 | 等待数秒；仍无图像则用 Room View 验证硬件 |
| `Streaming` | 帧序号持续变化，代码正在合成真实视频 | Console 必须出现 `Passthrough Streaming: LIVE`；这是通过软件侧验收的必要条件 |
| `FrameStalled` | 帧序号超过 1 秒未变化 | 关闭再开启透视；必要时重启 SteamVR |
| `Error` | Shader 或组件初始化失败 | 检查 Unity Console 和项目导入完整性 |

当 SteamVR 未初始化时，透视组件不会主动反复访问 `SteamVR.instance`；它保持 `WaitingForSteamVr` 并把相机获取重试限制为每 2 秒一次。这样可避免 OpenVR 失败后连续连接/断开 compositor 管道。此状态下应退出 Play Mode、等待 SteamVR Ready，再重新进入，而不是连续点击透视按钮。

## 技术边界

- 当前实现是**实验级背景合成**，不是 SteamVR Compositor 提供的系统级 passthrough layer。
- OpenVR 返回视频纹理和采集时 HMD 位姿，但本版本没有完成双目相机内参、眼–相机外参和逐眼重投影；真实视频与虚拟物体可能存在视差、尺度和边缘配准误差。
- 它没有真实环境深度，因此真实物体不能正确遮挡虚拟手；虚拟几何始终按照 Unity 深度覆盖在视频上。
- 相机曝光、延迟、帧率和视场角与人眼不同，快速转头时可能出现明显滞后。
- Unity 2019 的 VR 后处理路径和 SteamVR 渲染模式可能影响 `OnRenderImage`；当前固定基线应优先使用 Built-in Render Pipeline/OpenVR 的既有配置。
- 代码编译成功只能验证 API、Shader 与程序集路径正确；相机权限、USB 链路、SteamVR 服务和实际光学观感必须在连接 VIVE Pro 2 后以 `Streaming: LIVE` 日志和头显画面共同验收。
- 该功能不能替代安全监护。移动、抓取真实物体和涉及机械设备的实验仍应保留实体边界和现场保护措施。

## 与 VR 视角录像联动

透视进入 `Streaming` 后按 `F9`，Unity Recorder 会录制 Game View 中已完成合成的画面，因此 MP4 应同时包含真实背景、虚拟手和虚拟物体。录像操作、输出位置与边界见 [VR_VIEW_RECORDING.md](VR_VIEW_RECORDING.md)。

## 后续升级路径

若实验要求厘米级虚实配准或真实遮挡，应把下一阶段拆为三项独立标定：双目相机内参、相机到 HMD/眼坐标系外参、真实场景深度。仅提高视频分辨率不会解决这些几何问题。
