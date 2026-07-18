# Hi5 手指外展/内收与关节耦合

## 结论

当前“只能拇指–食指对指、难以拇指–小指对指”的首要软件原因不是 Unity 模型缺少骨骼，而是 **Hi5 2.0 Foundation SDK 默认启用了手指 ADB 固定模式**。本地 SDK 的 `Hi5_Thread_MonoBehaviour.isEnableFingerFixed` 默认值为 `true`，连接时被传给 `HI5_Device.EnableFingerAdbFixed`；厂商示例代码对 `true` 的中文注释为“固定、未开分指”，对 `false` 的注释为“开分指”。

项目现已在运行时自动把该模式设为 `false`，从而让厂商原生解算器输出手指 YAW，也就是 MCP/CMC 层面的外展–内收或拇指对掌所需的平面外旋转。实现位于：

`Assets/VRGloveDataCapture/Runtime/FingerKinematics/Hi5FingerFreedomController.cs`

该实现通过反射访问本地厂商 SDK，因此公共仓库不需要分发 Hi5 源码或二进制，且克隆后未导入 SDK 时仍可编译。

## 为什么九轴不等于每个关节都可独立观测

每个九轴 IMU 通常提供三轴角速度、三轴线加速度和三轴磁场。融合后最直接可靠的量是传感器载体的**方向**；它不会自动提供同一手指上 MCP、PIP、DIP 三个关节各自的独立角度。若一个手指只有一个惯性单元，则多个关节角到一个末端/指骨方向的映射是欠定的，仍然需要人体运动学先验、标定或额外传感器。

因此本项目采用两层处理：

1. **优先保留 Hi5 原生方向解算**，仅解除厂商主动施加的 ADB 固定。
2. **可选 PIP–DIP 约束后处理**，用可调比例消除不可观测自由度，而不声称恢复了真实独立关节角。

最终渲染阶段还可以叠加独立的**手–物体自适应视觉接触**。该层只在手套目标将进入物体时约束可视手，不参与上述运动学解算，也不写回源骨骼。设计、使用和数据隔离说明见 [HAND_OBJECT_CONTACT.md](HAND_OBJECT_CONTACT.md)。

## 自动 A-A 解锁

`Hi5FingerFreedomController` 由 Unity 在场景加载前自动创建，并在检测到本地 Hi5 SDK 后执行三项同步设置：

1. 把场景中 `Hi5_Thread_MonoBehaviour.isEnableFingerFixed` 设为 `false`。
2. 把 `HI5_Manager_Thread.enableFingerAdbFixed` 设为 `false`，避免重连时恢复固定模式。
3. 调用 `HI5_Device.EnableFingerAdbFixed(false)`，使已经运行的原生解算器立即切换。

成功时 Console 输出：

```text
[VRGloveDataCapture] Hi5 finger abduction/adduction is enabled (fixed mode is off).
```

这一层不需要改场景、不需要复制脚本到厂商目录，也不会修改标定流程。

## 可选 PIP–DIP 比例耦合

`FingerJointCoupling` 适合在 DIP 抖动明显、末端弯曲不自然或实验协议要求统一运动学模型时启用。

默认模型为：

```text
theta_DIP_target = clamp(0.67 * theta_PIP, -5 deg, 95 deg)
theta_DIP_output = lerp(theta_DIP_measured, theta_DIP_target, 0.65)
```

它只替换 DIP 绕局部屈伸轴的 twist 分量，其他 swing 分量保持不变，因此不会再次抹掉刚解锁的 A-A 旋转。默认局部屈伸轴为 `+Z`，与 Noitom 示例手骨骼沿 `+X` 延伸的结构相匹配。

### 配置步骤

1. 在 Hierarchy 中找到左、右手的 `Noitom_LeftHand` 和 `Noitom_RightHand` 骨骼根节点。
2. 分别添加 `FingerJointCoupling`。
3. 组件会按 `Noitom_*HandIndex2/3`、`Middle2/3`、`Ring2/3`、`Pinky2/3` 自动绑定。
4. 佩戴手套并完成厂商校准，让手掌处于自然伸直姿势。
5. 在组件菜单执行 `Capture current pose as coupling neutral`。
6. 若弯曲方向反向，把对应手指的 `Flexion Sign` 从 `1` 改为 `-1`。

### 建议参数

| 使用目标 | DIP/PIP 比例 | Coupling Weight | Response Hz |
|---|---:|---:|---:|
| 保留更多厂商测量 | 0.60–0.70 | 0.30–0.50 | 15–20 |
| 默认折中 | 0.67 | 0.65 | 18 |
| 强生理约束/低抖动 | 0.67–0.80 | 0.80–1.00 | 20–30 |

## 验证协议

每次改动参数后至少记录以下动作；不要只凭单个抓取动作判断。

| 动作 | 主要验证量 | 通过标准 |
|---|---|---|
| 五指自然伸直 | 中立位与漂移 | 不出现持续自发分指或 DIP 弯曲 |
| 五指扇形张开/合拢 | MCP A-A | 食、中、环、小指横向间距有连续变化 |
| 拇指–食指对指 | 原有能力回归 | 接触可达且交互碰撞不恶化 |
| 拇指–小指对指 | 新增对掌能力 | 拇指和小指指腹能够明显接近或接触 |
| 握拳与松开 | PIP–DIP 耦合 | DIP 随 PIP 连续弯曲，无跳变或反向折叠 |
| 抓取示例物体 | 交互回归 | 抓取判定、碰撞体和释放仍稳定 |

## 已知边界

- A-A 解锁依赖所安装 Hi5 原生库确实支持 `EnableFingerAdbFixed(false)`；当前本地 Foundation SDK `1.1.0.23` 已暴露该接口。
- 磁环境干扰仍可能影响九轴姿态融合；解锁自由度不会消除磁航向误差。
- 比例耦合是模型假设，不是额外测量。用于科研采集时必须把开关、比例、权重和中立位标定记录到实验元数据。
- 若需要各关节独立真值，必须增加传感器、使用光学手部追踪或引入带可验证误差模型的优化器。
