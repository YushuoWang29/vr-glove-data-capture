# VIVE Pro 2 视频透视混合现实

## 可行性结论

**可以实现**同时看见真实环境和 Unity 虚拟手/物体，但 VIVE Pro 2 是不透明显示头显，因此这里实现的是前置双摄像头驱动的 **video see-through（视频透视）**，不是光学透明式 AR。

HTC 官方说明确认 VIVE Pro 2 提供双摄像头，需在 SteamVR 的 `Settings > Camera` 中启用并重启 SteamVR；官方还提供 Room View。项目代码使用同一类相机硬件，但通过 Valve SteamVR Unity Plugin 的 `SteamVR_TrackedCamera`/OpenVR Tracked Camera 接口把视频帧交给 Unity 合成。[HTC：启用双摄像头](https://www.vive.com/cn/support/vive-pro2/category_howto/activating-the-dual-camera.html)，[Valve：OpenVR SDK](https://github.com/ValveSoftware/openvr)，[Valve：SteamVR Unity Plugin](https://github.com/ValveSoftware/steamvr_unity_plugin)

## 已实现工作流

1. `SteamVrPassthroughAutoInstaller` 在运行时寻找当前主相机或立体相机。
2. 它添加 `SteamVrPassthroughEffect`，但默认保持关闭，避免改变现有校准 demo。
3. 按键盘 `P` 后，组件通过 `SteamVR_TrackedCamera.Source(true)` 获取 HMD 的去畸变视频流。
4. 相机背景改为透明纯色并请求深度纹理。
5. `VRGlovePassthrough.shader` 把真实视频放在无虚拟几何的像素上，把虚拟手和物体按深度保留在前景。
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
6. 启动 Unity Play Mode，等待 Console 出现 `Passthrough is ready ... press P to toggle it`。
7. 按 `P` 开启透视；虚拟手和交互物品应保留在真实视频前景。

`SteamVrPassthroughEffect.SetPassthroughEnabled(bool)` 和 `TogglePassthrough()` 是公共方法，可直接连接 Unity UI 或后续 SteamVR Input Action，不依赖键盘。

## 运行状态诊断

| State | 含义 | 处理 |
|---|---|---|
| `Disabled` | 透视关闭 | 按 `P` 或调用公开方法 |
| `WaitingForSteamVr` | OpenVR 相机服务尚未就绪 | 先启动 SteamVR，确认 OpenVR Loader 已启用 |
| `CameraUnavailable` | SteamVR 未向应用暴露 HMD 相机 | 在 SteamVR 启用 Camera 并重启 SteamVR |
| `WaitingForFrame` | 已获取服务，尚未收到首帧 | 等待数秒；仍无图像则用 Room View 验证硬件 |
| `Streaming` | 正常接收并合成 | 正常状态 |
| `FrameStalled` | 帧序号超过 1 秒未变化 | 关闭再开启透视；必要时重启 SteamVR |
| `Error` | Shader 或组件初始化失败 | 检查 Unity Console 和项目导入完整性 |

## 技术边界

- 当前实现是**实验级背景合成**，不是 SteamVR Compositor 提供的系统级 passthrough layer。
- OpenVR 返回视频纹理和采集时 HMD 位姿，但本版本没有完成双目相机内参、眼–相机外参和逐眼重投影；真实视频与虚拟物体可能存在视差、尺度和边缘配准误差。
- 它没有真实环境深度，因此真实物体不能正确遮挡虚拟手；虚拟几何始终按照 Unity 深度覆盖在视频上。
- 相机曝光、延迟、帧率和视场角与人眼不同，快速转头时可能出现明显滞后。
- Unity 2019 的 VR 后处理路径和 SteamVR 渲染模式可能影响 `OnRenderImage`；当前固定基线应优先使用 Built-in Render Pipeline/OpenVR 的既有配置。
- 该功能不能替代安全监护。移动、抓取真实物体和涉及机械设备的实验仍应保留实体边界和现场保护措施。

## 后续升级路径

若实验要求厘米级虚实配准或真实遮挡，应把下一阶段拆为三项独立标定：双目相机内参、相机到 HMD/眼坐标系外参、真实场景深度。仅提高视频分辨率不会解决这些几何问题。
