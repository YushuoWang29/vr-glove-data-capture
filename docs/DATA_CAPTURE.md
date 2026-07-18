# 统一实验数据采集

## 目标与能力边界

**统一实验数据采集系统**把手部骨骼、关节角、物体位姿、头显位姿、任务事件、Hi5 传感器健康状态和 VR 视角视频组织到同一个 session/trial 中。所有机器可读数据共享同一个主机单调时钟和 trial 相对时间轴。

### 已实现的数据流

| 数据流 | 数据源 | 采样/触发方式 | 输出文件 |
|---|---|---|---|
| **帧时间基准** | `Stopwatch` 单调时钟 + Unity 帧 | 默认 60 Hz | `streams/frame_timing.csv` |
| **手部骨骼姿态** | Hi5 解算后的左右手可视骨骼 Transform | 默认 60 Hz | `streams/hand_bones.csv` |
| **关节角** | 骨骼相对父骨骼的 local rotation | 默认 60 Hz | `streams/joint_angles.csv` |
| **物体位姿与速度** | `PickPlaceTaskObject` 或 `CaptureTrackedObject` | 默认 60 Hz | `streams/objects.csv` |
| **头显/相机位姿** | `Camera.main` | 默认 60 Hz | `streams/devices.csv` |
| **Hi5 模块状态** | 厂商 SDK 的在线、电量、信号和磁场质量字段 | 默认 60 Hz | `streams/hi5_sensor_status.csv` |
| **任务与人工事件** | 任务事件总线、F11 或控制窗口 | 事件触发 | `events/events.csv` |
| **VR 第一视角视频** | Unity Recorder Game View | trial 启停联动，30 fps | `video/vr_view.mp4` |
| **九轴原始 IMU** | 可插拔 `IRawImuProvider` | 由供应方原始数据率决定 | 可用时生成 `streams/imu_raw.csv` |

### 九轴 IMU 的客观限制

仓库当前使用的 **Hi5 2.0 Foundation SDK 1.1.0.23** 在 Unity 层公开了 BVH/骨骼姿态，以及每个传感器模块的磁场质量、电量、信号和在线状态；当前 `HI5_Device` 的本地接口声明中没有读取三轴加速度、三轴角速度和三轴磁场向量的函数。因此：

1. `hand_bones.csv` 和 `joint_angles.csv` 是**厂商融合解算后的运动学结果**，不是九轴原始值。
2. 系统不会通过位置差分或旋转差分伪造 `imu_raw.csv`。
3. 未安装原始数据供应方时，trial 的 `manifest.json` 写入 `rawImuStatus: unavailable_vendor_api`，且不生成容易误用的空 `imu_raw.csv`。
4. 获得厂商原始数据 API、串口/无线协议或独立采集进程后，只需实现 `IRawImuProvider` 并注册到 `RawImuProviderRegistry`，现有 session、时间戳和导出结构无需改动。

## 操作流程

### 1. 打开采集控制窗口

在 Unity 执行：

```text
Tools > VR Glove Data Capture > Capture Control
```

填写以下字段后再进入 Play Mode：

| 字段 | 示例 | 命名规则/用途 |
|---|---|---|
| **Participant ID** | `P001` | 使用匿名编号，不写姓名 |
| **Session Label** | `pilot-a` | 同一批实验的标签 |
| **Task ID** | `ball-to-bucket` | 当前 trial 的任务类型 |
| **Condition** | `baseline` | 实验条件或算法版本 |
| **Operator ID** | `OP01` | 可选的实验员编号 |
| **Sample Rate** | `60` | 1–120 Hz；默认 60 Hz |
| **Notes** | `right tracker strap v2` | 只写不会破坏匿名化的备注 |

配置保存在本机 Unity EditorPrefs 中，不进入 Git，也不会修改 Scene。

### 2. 开始和结束 trial

1. 启动 SteamVR、Hi5 运行时并完成 V-pose 校准。
2. 打开任务场景并进入 Play Mode。
3. 确认控制窗口显示 Hi5 Bones 数量不为 0。
4. 点击 **Start Trial**，或让 Game View 获得焦点后按 **F12**。
5. 完成操作；需要额外标签时按 **F11**，或在控制窗口输入标签后点击 **Add Event Marker**。
6. 再次按 **F12** 或点击 **Stop & Finalize Trial**。
7. 等待 Console 出现 `TRIAL FINALIZED`；若启用视频，同时确认 `VR VIEW RECORDING SAVED`。

**不要在 trial 录制期间强制结束 Unity。** 若进程正常收到退出事件，系统会尽量以 `ABORTED` 标记收尾；操作系统崩溃或断电时会保留 `.partial` 和 `INCOMPLETE`，避免损坏文件被误认为完整数据。

### 3. 连续采集多个 trial

- 每次 F12 启停产生一个递增的 `trial-0001`、`trial-0002`。
- 修改 Task ID 或 Condition 后，下一 trial 使用新元数据，但仍归入当前 session。
- 点击 **Start New Session on Next Trial** 后，下一 trial 创建新的时间戳 session。
- `Captures/LATEST_TRIAL.txt` 指向最近一次开始的 trial；控制窗口也可以直接打开最新目录。

## 导出目录与命名

数据写到仓库根目录的 `Captures/`，该目录被 Git 忽略：

```text
Captures/
├── LATEST_TRIAL.txt
└── participants/
    └── P001/
        └── sessions/
            └── 20260718T101530.125Z__session-pilot-a/
                ├── session.json
                └── trials/
                    └── trial-0001__task-ball-to-bucket__cond-baseline/
                        ├── manifest.json
                        ├── checksums.sha256
                        ├── COMPLETE
                        ├── streams/
                        │   ├── frame_timing.csv
                        │   ├── hand_bones.csv
                        │   ├── joint_angles.csv
                        │   ├── objects.csv
                        │   ├── devices.csv
                        │   └── hi5_sensor_status.csv
                        ├── events/
                        │   └── events.csv
                        └── video/
                            └── vr_view.mp4
```

所有目录字段都会过滤 Windows 非法字符、合并空白并限制长度，避免人工命名导致无法落盘。采集根目录位于较浅的仓库层级，以规避 Unity 2019 Mono 在 Windows 深路径下的 `FileStream` 失败。

## 时间戳与同步语义

### 统一字段

所有结构化流至少包含以下字段：

| 字段 | 单位/格式 | 语义 |
|---|---|---|
| `sample_id` | 递增整数 | 同一次 `LateUpdate` 写出的多流记录共享该编号 |
| `t_monotonic_ns` | ns | 进程内单调递增主机时间，不受系统时钟校正影响 |
| `t_trial_ns` | ns | 相对 trial 开始的时间，便于直接对齐 |
| `utc_iso8601` | UTC ISO 8601 | 用于跨 session 查找和外部系统粗对齐 |
| `unity_frame` | Unity 帧号 | 定位渲染/物理执行上下文 |

视频启动和停止会写入 `events.csv` 的 `video_recording_started` 与 `video_recording_stopped`。这使视频与数据拥有共同的 trial 原点和主机事件时间。

### 同步精度边界

当前方案实现的是**单机软件时钟统一**，不等于硬件级同步：

- Hi5 当前 Unity 接口没有把每个原始 IMU 包的硬件时间戳交给本项目。
- Unity Recorder 2.2 没有向本项目回传每个编码视频帧的硬件曝光时间。
- 因此骨骼、物体、事件之间可以按同一 Unity 采样时刻对齐；视频通过录制启停事件和固定帧率对齐，但不应宣称达到传感器曝光级或亚毫秒硬同步。
- 安装原始 IMU provider 后，`imu_raw.csv` 同时保留供应方 `source_t_ns`、`source_sequence` 和主机接收采样时间，可进一步估计时钟偏移和漂移。

## 数据字段说明

### 手部与关节

- `hand_bones.csv` 同时保存世界坐标和局部坐标的位置/四元数。
- `joint_angles.csv` 保存局部四元数换算出的有符号 XYZ 欧拉角，范围约为 `[-180°, 180°]`。
- 对机器人模仿学习，建议优先使用四元数或自行定义的关节轴投影；XYZ 欧拉角主要用于检查、可视化和快速原型，存在旋转顺序与万向节锁语义。
- 启用 PIP–DIP 耦合时，这里记录的是**耦合后的最终虚拟骨骼姿态**。

### 物体

- 当前 pick-and-place 场景中的 `PickPlaceTaskObject` 和 `PickPlaceTargetZone` 自动加入记录，使移动物体与目标参照系同时可恢复。
- 新任务中的普通物体需要添加 `CaptureTrackedObject`，并设置稳定的 Object ID 和 Category。
- 有 Rigidbody 时写出线速度和角速度；无 Rigidbody 时速度字段为 0。

### 任务事件

当前自动事件包括：

| `event_type` | 触发条件 | 主要标签 |
|---|---|---|
| `trial_started` | trial 开始 | task、condition |
| `object_entered_target` | 匹配物体进入目标区 | task、Hi5 object name、target |
| `task_completed` | 单个任务成功 | 完成数、总数 |
| `task_set_completed` | 当前任务组全部成功 | 总任务数 |
| `scene_reset` | F8 或原厂复位按钮 | 物体数、目标区数 |
| `manual_marker` | F11 或控制窗口 | 用户标签 |
| `video_recording_started/stopped` | Unity Recorder 启停 | 视频文件路径 |

## 完整性与质量检查

### 每个 trial 的判据

1. 存在 `COMPLETE`，不存在 `INCOMPLETE` 和 `.partial`。
2. `manifest.json` 中 `status` 为 `complete`。
3. `checksums.sha256` 可验证所有最终数据流和视频文件。
4. `frameSamples`、`boneRows`、`objectRows` 与实验时长相符。
5. `rawImuStatus` 必须被纳入数据集质量标签，不能忽略。
6. `hi5_sensor_status.csv` 中关键模块在实验期间应保持 `online=1`，信号和磁场质量不应长时间跌至异常区间。

### 自动化测试

在 Edit Mode 执行：

```text
Tools > VR Glove Data Capture > Data Capture > Run Unified Capture Play Mode Test
```

测试会创建并清理 `AUTOMATED_CAPTURE_TEST` 专用目录，验证：

- session/trial 能够创建和完成；
- 主要 CSV、manifest、校验和及 `COMPLETE` 存在；
- 事件和物体记录可读；
- 最终目录不残留 `.partial`。

## 原始 IMU Provider 接口

外部适配器需要：

1. 实现 `VRGloveDataCapture.Capture.IRawImuProvider`。
2. 给每条数据填入手别、稳定 Sensor ID、源时间戳、源序号和 SI 单位九轴向量。
3. 初始化时调用 `RawImuProviderRegistry.Register(provider)`，退出时调用 `Unregister`。
4. `DrainSamples` 应排空线程安全缓冲区，不要在 Unity 主线程阻塞读取硬件。

`imu_raw.csv` 固定使用 `m/s²`、`rad/s` 和 `T`，避免下游因 `g`、`deg/s`、`µT` 单位混用产生静默错误。
