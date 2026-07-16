# VR 第一视角视频录制

## 功能范围

项目使用 **Unity Recorder `2.2.0-preview.4`** 在 **Unity 2019.4 Editor Play Mode** 中录制 Game View。该视图是头显渲染的桌面镜像，因此适合记录操作者视角下的标定、虚拟手运动、交互过程以及已启用的视频透视合成结果。

默认参数：

| 参数 | 当前值 | 说明 |
|---|---:|---|
| 快捷键 | `F9` | 第一次开始，第二次停止 |
| 格式 | MP4 | Unity Recorder 的 Windows MP4 编码器 |
| 分辨率 | 1920 × 1080 | Game View 输出分辨率 |
| 录像帧率 | 30 fps | 不强制把 HMD 渲染频率限制为 30 Hz |
| 码率档位 | High | Recorder 的高码率预设 |
| 音频 | Unity 场景音频 | 不自动采集 Windows 麦克风 |
| 输出目录 | `Unity Project/vr-glove-data-capture/Recordings/` | 已被 Git 忽略，不会误提交实验视频 |

## 使用步骤

1. 打开 Game View，并进入现有 Hi5 示例场景的 Play Mode。
2. 完成手套校准并确认虚拟手正常运动。
3. 若要录制混合现实画面，先按 `P`，等待 Console 出现 `Passthrough Streaming: LIVE`，并在头显或 Game View 中确认真实背景可见。
4. 保持 **Game View 获得键盘焦点**，按 `F9` 开始录像。
5. Console 出现 `VR VIEW RECORDING STARTED (F9 to stop)` 后执行实验动作。
6. 再按 `F9` 停止并封装 MP4；不要在停止前强制结束 Unity 进程。
7. Console 出现 `VR VIEW RECORDING SAVED` 后，打开日志给出的绝对路径检查视频。

也可使用 Unity 菜单 `Tools > VR Glove Data Capture > Toggle VR View Recording` 开始或停止录制。退出 Play Mode 时，代码会主动停止录像并完成文件封装。

## 验收标准

1. 开始录制时只出现一条 `RECORDING STARTED` 日志。
2. 停止后出现 `RECORDING SAVED`，对应 `.mp4` 文件存在且大小大于 0。
3. 视频可正常播放，时长与实验操作时间基本一致。
4. 头部转动方向、虚拟手姿态和交互物体变化与操作者体验一致。
5. 开启透视时，视频同时包含真实相机背景与虚拟前景。
6. 录制期间头显帧率没有因 30 fps 视频设置被锁定；实现中 `CapFrameRate = false`。

## 技术边界

- Unity Recorder 在该 Unity 版本中属于预览包；Unity 2019.4 官方列出的对应版本为 `2.2.0-preview.4`。它适合研究记录，但应先进行短片验证再开始正式实验。[Unity 2019.4 Recorder 手册](https://docs.unity3d.com/cn/2019.4/Manual/com.unity.recorder.html)
- 当前记录的是 **Game View/HMD 桌面镜像**，不是分别保存左右眼的原始立体纹理，也不是 SteamVR Compositor 的无损输出。
- Game View 的镜像模式由 Unity、OpenVR 和当前窗口设置共同决定，可能显示单眼、裁剪视图或并排视图；正式实验前应固定 Game View 布局并记录配置。
- MP4 包含 Unity 场景音频，但不保证包含实验员语音。需要语音时，应使用独立音频采集并用共同时间标记同步。
- 录像会消耗 GPU、CPU 和磁盘带宽。正式采集前应测试持续录像时长、掉帧和文件大小，并将视频存放在获批准的研究数据位置。
- `Recordings/` 被 Git 忽略；研究原始视频不应提交到公开仓库。

## 故障诊断

| 现象 | 原因 | 处理 |
|---|---|---|
| 按 `F9` 无日志 | Game View 未获得焦点，或不在 Play Mode | 点击 Game View 后重试，或使用 Tools 菜单 |
| 提示仅 Editor 可用 | 当前运行的是 Windows build | 回到 Unity Editor Play Mode；本版本未实现 Player 端编码器 |
| 开始失败 | Recorder 包未加载、Game View 无有效输出或编码器初始化失败 | 等待 Package Manager 完成，检查 Console 第一条异常 |
| 视频没有透视背景 | 录像开始前透视未进入 `Streaming` | 先完成 `P` 键透视验收，再开始录像 |
| 文件无法播放或为零字节 | Unity 被强制关闭，MP4 未完成封装 | 正常按 `F9` 或退出 Play Mode，让 `StopRecording()` 完成 |
| 头显明显掉帧 | 分辨率/编码负载过高 | 先缩短录像、关闭无关窗口；必要时后续将输出降到 1280 × 720 |

## 代码位置

```text
Assets/VRGloveDataCapture/Runtime/Capture/VrViewRecordingHotkey.cs
Assets/VRGloveDataCapture/Editor/VrViewRecorderEditorBridge.cs
Packages/manifest.json
```
