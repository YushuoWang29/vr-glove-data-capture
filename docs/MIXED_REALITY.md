# VIVE Pro 2 视频透视混合现实

## 可行性结论

**可以实现**同时看见真实环境和 Unity 虚拟手/物体，但 VIVE Pro 2 是不透明显示头显，因此这里实现的是前置双摄像头驱动的 **video see-through（视频透视）**，不是光学透明式 AR。

HTC 官方说明确认 VIVE Pro 2 提供双摄像头，需在 SteamVR 的 `Settings > Camera` 中启用并重启 SteamVR；官方还提供 Room View。项目代码使用同一类相机硬件，但通过 Valve SteamVR Unity Plugin 的 `SteamVR_TrackedCamera`/OpenVR Tracked Camera 接口把视频帧交给 Unity 合成。[HTC：启用双摄像头](https://www.vive.com/cn/support/vive-pro2/category_howto/activating-the-dual-camera.html)，[Valve：OpenVR SDK](https://github.com/ValveSoftware/openvr)，[Valve：SteamVR Unity Plugin](https://github.com/ValveSoftware/steamvr_unity_plugin)

## 已实现工作流

1. `SteamVrPassthroughAutoInstaller` 在运行时寻找当前主相机或立体相机。
2. 它添加 `SteamVrPassthroughEffect`，但默认保持关闭，避免改变现有校准 demo。
3. 按键盘 `P` 后，组件通过 `SteamVR_TrackedCamera.Source(true)` 获取 HMD 的去畸变视频流。
4. 组件读取 OpenVR 的 `Prop_NumCameras_Int32` 与 `Prop_CameraFrameLayout_Int32`。VIVE Pro 2 实测纹理为 `1224×1840`，对应 `VerticalLayout`；OpenVR 对该布局的定义是 **上/下 = 左/右眼**，因此必须先按眼拆分，不能把整张纹理当作单目画面。
5. SteamVR Unity 包装层会把 OpenVR `frameBounds` 的 V 坐标翻转，运行日志中对应负的 V 缩放。旧实现先固定选择上/下半区、再应用该翻转，结果把右相机送入 Unity 左眼、左相机送入右眼。当前 Shader 根据实际 `_CameraUvTransform` 正负选择打包半区，保持**渲染眼与物理相机同侧**。
6. 项目明确使用 Unity 2019 的 **Single Pass Instanced**。该版本中，单次 `CommandBuffer.DrawMesh` 默认只提交一个实例且只绑定纹理数组第一个切片，会产生“左眼有背景、右眼仍是虚拟场景”的已知问题。当前命令显式把 `CameraTarget` 的 `depthSlice` 设为 `-1`、把 instance multiplier 设为 `2`，绘制后再恢复为 `1`，从而写入左右眼全部切片。[Unity Issue 1301011](https://issuetracker.unity3d.com/issues/xr-commandbuffer-dot-drawmesh-only-renders-to-the-left-eye-when-using-single-pass-instanced-mode)，[Unity 2019.4 `SetInstanceMultiplier`](https://docs.unity3d.com/2019.4/Documentation/ScriptReference/Rendering.CommandBuffer.SetInstanceMultiplier.html)
7. `VRGlovePassthrough.shader` 通过目标 VR Camera 的背景 CommandBuffer（Forward 使用 `BeforeForwardOpaque`，Deferred 使用 `BeforeGBuffer`）分别为左右眼绘制现实背景。虚拟手、交互物体和 UI 随后继续走 Unity 原有深度与透明渲染，因此显示在现实背景前方。
8. 这一实现不再依赖只作用于单眼的 `OnRenderImage` 后处理，也不使用 XR RenderTexture 不可靠的 Alpha 通道作为遮罩。
9. 再按 `P` 会移除背景 CommandBuffer、对称释放视频服务，并恢复原 Camera 设置。

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
9. 以 `Passthrough Streaming: LIVE: 2 camera(s), VerticalStereo, ... XR draw SinglePassInstanced x2/all-slices` 一类日志作为**软件已经收到连续双目相机帧、识别布局并配置双眼绘制**的判据；同时确认左、右眼各自只看到一个现实画面，真实画面在背景、虚拟手和交互物品在前景。
10. 若左右眼已经能融合、但两眼画面都确实倒置，按一次 `O`。该键只在每只眼自己的相机区域内旋转 180°，Console 会显示 `per-eye Rotate180`；再按一次恢复 `Normal`。

`SteamVrPassthroughEffect.SetPassthroughEnabled(bool)` 和 `TogglePassthrough()` 是公共方法，可直接连接 Unity UI 或后续 SteamVR Input Action，不依赖键盘。

## 为什么不能靠整张纹理旋转 180° 修复接缝

**整体旋转与双眼配对是两类独立问题**。上下打包纹理整体旋转 180° 会同时交换上、下相机区域；即使画面方向看似转正，也可能继续或再次形成交叉双目。中间接缝、重影和无法融合还可能来自左右眼交换、相机内参、相机到眼睛的外参、视场角差异及场景深度，仅旋转不能把这些几何关系对齐。

本版本采用两级处理：

1. 默认逻辑只负责依据 OpenVR 布局和 SteamVR UV 变换，把左相机送入左眼、右相机送入右眼。
2. `O` 键只负责可选的**各眼内部朝向**，不会交换相机，也不承诺修复视差或接缝。

Valve 在 OpenVR 头文件中定义了 VerticalLayout 的上/下左右眼顺序，并另外提供相机内参、投影矩阵与 Camera-to-Head 变换接口；这也说明完整几何配准需要的不只是纹理旋转。[Valve OpenVR `openvr.h`](https://github.com/ValveSoftware/openvr/blob/master/headers/openvr.h)，[Valve SteamVR Unity Tracked Camera](https://github.com/ValveSoftware/steamvr_unity_plugin/blob/master/Assets/SteamVR/Scripts/SteamVR_TrackedCamera.cs)

## 运行状态诊断

| State | 含义 | 处理 |
|---|---|---|
| `Disabled` | 透视关闭 | 按 `P` 或调用公开方法 |
| `WaitingForSteamVr` | OpenVR 相机服务尚未就绪 | 先启动 SteamVR，确认 OpenVR Loader 已启用 |
| `CameraUnavailable` | SteamVR 未向应用暴露 HMD 相机 | 在 SteamVR 启用 Camera 并重启 SteamVR |
| `WaitingForFrame` | 已获取服务，尚未收到首帧 | 等待数秒；仍无图像则用 Room View 验证硬件 |
| `Streaming` | 帧序号持续变化，代码正在合成真实视频 | Console 必须出现 `Passthrough Streaming: LIVE` 及 `XR draw SinglePassInstanced x2/all-slices`；这是通过软件侧验收的必要条件 |
| `FrameStalled` | 帧序号超过 1 秒未变化 | 关闭再开启透视；必要时重启 SteamVR |
| `Error` | Shader 或组件初始化失败 | 检查 Unity Console 和项目导入完整性 |

当 SteamVR 未初始化时，透视组件不会主动反复访问 `SteamVR.instance`；它保持 `WaitingForSteamVr` 并把相机获取重试限制为每 2 秒一次。这样可避免 OpenVR 失败后连续连接/断开 compositor 管道。此状态下应退出 Play Mode、等待 SteamVR Ready，再重新进入，而不是连续点击透视按钮。

## 技术边界

- 当前实现是**实验级背景合成**，不是 SteamVR Compositor 提供的系统级 passthrough layer。
- OpenVR 返回视频纹理和采集时 HMD 位姿，但本版本没有完成双目相机内参、眼–相机外参和逐眼重投影；真实视频与虚拟物体可能存在视差、尺度和边缘配准误差。
- 它没有真实环境深度，因此真实物体不能正确遮挡虚拟手；虚拟几何始终按照 Unity 深度覆盖在视频上。
- 相机曝光、延迟、帧率和视场角与人眼不同，快速转头时可能出现明显滞后。
- 当前背景路径面向项目固定的 **Built-in Render Pipeline + OpenVR Single Pass Instanced**。CommandBuffer 负责全切片双实例绘制，Shader 再通过 `unity_StereoEyeIndex` 为每眼选取相机区域；若修改 Stereo Rendering Path、图形 API 或 XR 后端，必须重新执行双眼验收。[Unity 2019.4 Single Pass Instanced](https://docs.unity3d.com/2019.4/Documentation/Manual/SinglePassInstancing.html)
- 代码编译成功只能验证 API、Shader 与程序集路径正确；相机权限、USB 链路、SteamVR 服务和实际光学观感必须在连接 VIVE Pro 2 后以 `Streaming: LIVE` 日志和头显画面共同验收。
- 该功能不能替代安全监护。移动、抓取真实物体和涉及机械设备的实验仍应保留实体边界和现场保护措施。

## 与 VR 视角录像联动

透视进入 `Streaming` 后按 `F9`，Unity Recorder 会录制 Game View 中已完成合成的画面，因此 MP4 应同时包含真实背景、虚拟手和虚拟物体。录像只保存项目配置的 VR View 视角，不会把上下堆叠的原始双目纹理直接写入 MP4。录像操作、输出位置与边界见 [VR_VIEW_RECORDING.md](VR_VIEW_RECORDING.md)。

## 后续升级路径

若实验要求厘米级虚实配准或真实遮挡，应把下一阶段拆为三项独立标定：双目相机内参、相机到 HMD/眼坐标系外参、真实场景深度。仅提高视频分辨率不会解决这些几何问题。

## 本版本验证状态

- **已完成**：Shader/脚本编译、Single Pass Instanced 双实例判定、OpenVR VerticalStereo 眼区 CPU 参考映射、负 V 缩放下的左右眼断言、各眼 180° 旋转不越区断言；项目全部 Play Mode 测试结果为 `4 passed / 0 failed / 0 skipped`。
- **仍需实机复测**：VIVE Pro 2 中左右眼是否稳定融合、`Normal`/`Rotate180` 哪个与当前 SteamVR 驱动方向一致、近距离真实物体与虚拟物体的视差。没有完成这次头显复测前，不能把几何配准标记为最终验收通过。
