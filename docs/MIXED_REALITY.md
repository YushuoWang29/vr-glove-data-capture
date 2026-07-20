# VIVE Pro 2 视频透视混合现实

## 可行性结论

**可以实现**同时看见真实环境和 Unity 虚拟手/物体，但 VIVE Pro 2 是不透明显示头显，因此这里实现的是前置双摄像头驱动的 **video see-through（视频透视）**，不是光学透明式 AR。

HTC 官方说明确认 VIVE Pro 2 提供双摄像头，需在 SteamVR 的 `Settings > Camera` 中启用并重启 SteamVR；官方还提供 Room View。项目代码使用同一类相机硬件，但通过 Valve SteamVR Unity Plugin 的 `SteamVR_TrackedCamera`/OpenVR Tracked Camera 接口把视频帧交给 Unity 合成。[HTC：启用双摄像头](https://www.vive.com/cn/support/vive-pro2/category_howto/activating-the-dual-camera.html)，[Valve：OpenVR SDK](https://github.com/ValveSoftware/openvr)，[Valve：SteamVR Unity Plugin](https://github.com/ValveSoftware/steamvr_unity_plugin)

这里的**基础透视验收**是：左右眼持续收到各自的相机画面、画面方向正确，并作为虚拟手和任务物体的背景稳定显示。它不等同于厘米级虚实配准；近距离视差和真实遮挡属于后述的双目几何与深度问题。

## 已实现工作流

1. `SteamVrPassthroughAutoInstaller` 在运行时寻找当前主相机或立体相机。
2. 它添加 `SteamVrPassthroughEffect`，但默认保持关闭，避免改变现有校准 demo。
3. 按键盘 `P` 后，组件通过 `SteamVR_TrackedCamera.Source(true)` 获取 HMD 的去畸变视频流。
4. 组件读取 OpenVR 的 `Prop_NumCameras_Int32` 与 `Prop_CameraFrameLayout_Int32`。本机 VIVE Pro 2 的实机日志为 **`1224×1840`、`VerticalStereo`、2 cameras**；项目据此把上、下两个相机区域分别送入左右渲染眼，不能把整张纹理当作单目画面。
5. Shader 首先用 Unity 内置的 `_ProjectionParams.x` 修正当前 XR 眼目标的投影方向。背景 pass 直接输出 clip-space 顶点，绕过了普通几何所经过的投影矩阵；若当前眼目标采用 flipped projection，该修正负责恢复一致的视图 UV。Unity 官方将 `_ProjectionParams.x` 定义为正常投影时 `+1`、翻转投影时 `-1`。[Unity 2019.4：内置 Shader 变量](https://docs.unity3d.com/2019.4/Documentation/Manual/SL-UnityShaderVariables.html)，[Unity：平台相关渲染差异](https://docs.unity3d.com/2019.4/Documentation/Manual/SL-PlatformDifferences.html)
6. SteamVR Unity 包装层另外会翻转 OpenVR `frameBounds` 的 V 坐标；本机日志中的 Valve 变换为 **`(1,-1,0,1)`**。它描述的是外部相机纹理的裁剪和方向，不是 XR 眼目标方向。Shader 在选择逻辑眼区后最后应用该仿射变换，并根据缩放符号保持**渲染眼与物理相机同侧**。[Valve：`SteamVR_TrackedCamera.cs`](https://github.com/ValveSoftware/steamvr_unity_plugin/blob/master/Assets/SteamVR/Scripts/SteamVR_TrackedCamera.cs)，[Valve：Tracked Camera 示例](https://github.com/ValveSoftware/steamvr_unity_plugin/blob/master/Assets/SteamVR/Extras/SteamVR_TestTrackedCamera.cs)
7. 组件根据 `XRSettings.stereoRenderingMode` 支持两条路径。当前 OpenVR Loader 的项目配置与实机初始化日志均为 **Single Pass Instanced**，因此命令把 `CameraTarget.depthSlice` 设为 `-1`、把 instance multiplier 设为 `2`，绘制后恢复为 `1`，覆盖左右纹理数组切片；只有明确改为 Multi Pass 时才使用 `x1`。旧实现曾在 `Awake` 中过早读取 XR 模式，当时 Loader 尚未完成初始化，因而错误缓存成 `MultiPass x1`，这会造成只绘制一个眼切片。当前版本改为在相机首帧安装 CommandBuffer 前重新读取，并在运行中模式变化时原子地移除、重建和重装。[Unity Issue 1301011](https://issuetracker.unity3d.com/issues/xr-commandbuffer-dot-drawmesh-only-renders-to-the-left-eye-when-using-single-pass-instanced-mode)，[Unity 2019.4 `SetInstanceMultiplier`](https://docs.unity3d.com/2019.4/Documentation/ScriptReference/Rendering.CommandBuffer.SetInstanceMultiplier.html)
8. `VRGlovePassthrough.shader` 通过目标 VR Camera 的背景 CommandBuffer（Forward 使用 `BeforeForwardOpaque`，Deferred 使用 `BeforeGBuffer`）绘制现实背景。虚拟手、交互物体和 UI 随后继续走 Unity 原有深度与透明渲染，因此显示在现实背景前方。
9. 这一实现不再依赖只作用于单眼的 `OnRenderImage` 后处理，也不使用 XR RenderTexture 不可靠的 Alpha 通道作为遮罩。再按 `P` 会移除背景 CommandBuffer、对称释放视频服务，并恢复原 Camera 设置。

### 三段 UV 变换不可合并

| 阶段 | 输入依据 | 处理对象 | 解决的问题 |
|---|---|---|---|
| **XR 目标投影修正** | `_ProjectionParams.x` | 全屏 quad 的视图 UV | D3D/XR 眼目标的 flipped projection |
| **逐眼区域选择** | `unity_StereoEyeIndex`、`VerticalStereo`、`swapStereoEyes` | 逻辑左右相机区域 | 每个渲染眼只采样对应相机，不出现一眼上下双画面 |
| **Valve frameBounds** | `_CameraUvTransform`，实机为 `(1,-1,0,1)` | 外部 tracked-camera 纹理 | 相机源纹理的裁剪、负 V 方向及打包区域地址 |

正确顺序是 **XR 目标投影 → 逐眼区域 → Valve frameBounds → 纹理采样**。把任意两段翻转相互替代，会重新产生上下倒置、左右眼交换或两者同时出现的问题。

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
9. 以以下日志作为当前项目配置下**软件已收到连续双目帧、识别布局并配置双眼绘制**的判据：

   ```text
   LIVE: 2 camera(s), VerticalStereo, texture 1224x1840,
   per-eye Normal, UV (1.0000,-1.0000,0.0000,1.0000),
   XR draw SinglePassInstanced x2/all-slices, Direct3D11
   ```

   若有意把 Player Settings 的 Stereo Rendering Mode 改成 Multi Pass，对应日志才应为 `XR draw MultiPass x1`。**OpenVR 初始化日志与 LIVE 日志必须一致**；项目配置显示 Single Pass Instanced 而 LIVE 显示 MultiPass 时不能通过验收。

10. 在头显中确认左、右眼各自只看到**一个**现实画面，文字没有水平镜像，地面、桌面和天花板方向正确，真实画面在背景、虚拟手和交互物品在前景。正式通过时必须保持 **`per-eye Normal`**。
11. `O` 只保留为故障隔离开关：它会在每个眼区内部旋转 180°，用于判断错误来自源图朝向还是目标投影。它不是正常配置，也不是验收条件；诊断完成后必须再按一次回到 `Normal`。

`SteamVrPassthroughEffect.SetPassthroughEnabled(bool)` 和 `TogglePassthrough()` 是公共方法，可直接连接 Unity UI 或后续 SteamVR Input Action，不依赖键盘。

## 为什么不能靠旋转修复方向或接缝

**整体旋转与双眼配对是两类独立问题**。上下打包纹理整体旋转 180° 会同时交换上、下相机区域；即使画面方向看似转正，也可能继续或再次形成交叉双目。中间接缝、重影和无法融合还可能来自左右眼交换、相机内参、相机到眼睛的外参、视场角差异及场景深度，仅旋转不能把这些几何关系对齐。

本版本采用三段明确分工：

1. `_ProjectionParams.x` 修正 Unity 当前 XR 渲染目标的投影方向。
2. OpenVR 布局和 Valve `frameBounds` 负责相机区域及源纹理方向。
3. `O` 仅临时改变**各眼内部朝向**，不会校正相机内参、外参、延迟或场景深度。

Valve 在 OpenVR 头文件中定义了 `VerticalLayout` 布局标志，并另外提供相机内参、投影矩阵与 Camera-to-Head 变换接口；本项目的上/下左右眼映射由 VIVE Pro 2 实机布局回归固定。这也说明完整几何配准需要的不只是纹理旋转。[Valve OpenVR `openvr.h`](https://github.com/ValveSoftware/openvr/blob/master/headers/openvr.h)，[Valve SteamVR Unity Tracked Camera](https://github.com/ValveSoftware/steamvr_unity_plugin/blob/master/Assets/SteamVR/Scripts/SteamVR_TrackedCamera.cs)

## 运行状态诊断

| State | 含义 | 处理 |
|---|---|---|
| `Disabled` | 透视关闭 | 按 `P` 或调用公开方法 |
| `WaitingForSteamVr` | OpenVR 相机服务尚未就绪 | 先启动 SteamVR，确认 OpenVR Loader 已启用 |
| `CameraUnavailable` | SteamVR 未向应用暴露 HMD 相机 | 在 SteamVR 启用 Camera 并重启 SteamVR |
| `WaitingForFrame` | 已获取服务，尚未收到首帧 | 等待数秒；仍无图像则用 Room View 验证硬件 |
| `Streaming` | 帧序号持续变化，代码正在合成真实视频 | Console 必须出现 `Passthrough Streaming: LIVE`；当前项目应显示 `SinglePassInstanced x2/all-slices`，且必须与 OpenVR 初始化日志一致 |
| `FrameStalled` | 帧序号超过 1 秒未变化 | 关闭再开启透视；必要时重启 SteamVR |
| `Error` | Shader 或组件初始化失败 | 检查 Unity Console 和项目导入完整性 |

当 SteamVR 未初始化时，透视组件不会主动反复访问 `SteamVR.instance`；它保持 `WaitingForSteamVr` 并把相机获取重试限制为每 2 秒一次。这样可避免 OpenVR 失败后连续连接/断开 compositor 管道。此状态下应退出 Play Mode、等待 SteamVR Ready，再重新进入，而不是连续点击透视按钮。

## 技术边界

- 当前实现是**实验级背景合成**，不是 SteamVR Compositor 提供的系统级 passthrough layer。
- OpenVR 返回视频纹理和采集时 HMD 位姿，但本版本没有完成双目相机内参、眼–相机外参和逐眼重投影；真实视频与虚拟物体可能存在视差、尺度和边缘配准误差。
- 它没有真实环境深度，因此真实物体不能正确遮挡虚拟手；虚拟几何始终按照 Unity 深度覆盖在视频上。
- 相机曝光、延迟、帧率和视场角与人眼不同，快速转头时可能出现明显滞后。
- 当前背景路径面向项目固定的 **Built-in Render Pipeline + OpenVR**，当前实机配置为 `Single Pass Instanced`，代码同时保留 `MultiPass` 路径。若修改 Stereo Rendering Path、图形 API 或 XR 后端，必须重新执行双眼验收。[Unity 2019.4 Single Pass Instanced](https://docs.unity3d.com/2019.4/Documentation/Manual/SinglePassInstancing.html)
- 不应在当前 Unity 2019/纯 PC VIVE Pro 2 分支中直接改用 `XR_HTC_passthrough`：HTC 当前支持表把纯 PC VIVE Pro series 标为不支持 passthrough，现行 VIVE OpenXR 插件还要求更新的 Unity 基线。[HTC VIVE OpenXR：Passthrough 支持范围](https://developer.vive.com/resources/openxr/unity/tutorials/passthrough/)
- 代码编译成功只能验证 API、Shader 与程序集路径正确；相机权限、USB 链路、SteamVR 服务和实际光学观感必须在连接 VIVE Pro 2 后以 `Streaming: LIVE` 日志和头显画面共同验收。
- 该功能不能替代安全监护。移动、抓取真实物体和涉及机械设备的实验仍应保留实体边界和现场保护措施。

## 与 VR 视角录像联动

透视进入 `Streaming` 后按 `F9`，Unity Recorder 会录制 Game View 中已完成合成的画面，因此 MP4 应同时包含真实背景、虚拟手和虚拟物体。录像只保存项目配置的 VR View 视角，不会把上下堆叠的原始双目纹理直接写入 MP4。录像操作、输出位置与边界见 [VR_VIEW_RECORDING.md](VR_VIEW_RECORDING.md)。

## 后续升级路径

修正上下方向后，近距离仍可能出现接缝、重影或虚实位置不重合。这不是继续旋转纹理能够解决的问题。当前背景合成尚未使用 OpenVR 提供的 `GetCameraIntrinsics`、`GetCameraProjection`、Camera-to-Head 变换和相机帧采集姿态来做逐眼重投影，也没有真实场景深度。

若实验要求厘米级虚实配准或真实遮挡，应把下一阶段拆为三项独立标定：双目相机内参、相机到 HMD/眼坐标系外参、真实场景深度；随后再加入捕获姿态到显示姿态的时间重投影。仅提高视频分辨率、交换左右眼或旋转画面不会解决这些几何问题。

## 本版本验证状态

- **自动回归已覆盖**：VIVE Pro 2 实测 `1224×1840 VerticalStereo` 识别、`_ProjectionParams.x=-1` 的独立目标投影修正、Valve `(1,-1,0,1)` frameBounds、完整左右眼顶部/底部映射、X 方向不镜像、逐眼 180° 不越区、`MultiPass x1 → SinglePassInstanced x2/all-slices` 的延迟初始化重建，以及缓存模式与 Unity 当前 XR 模式一致。
- **基础透视仍需实机复测**：保持 `per-eye Normal`，确认左右眼各自只有一个画面、方向直立、文字不镜像、相机侧别正确且连续帧稳定。`O` 只能记录诊断差异，不能作为通过条件。
- **几何配准不在本版本验收声明内**：近距离真实物体与虚拟物体的视差、真实遮挡和快速转头时的时延必须独立记录，不能回归为“透视方向失败”。
