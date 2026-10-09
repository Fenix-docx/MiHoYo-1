# 评审任务 B：多组件协同运动 + Gooey 融合联动

你在评审一个 Unity 6 项目（uGUI）的"UI 2D 运动模块"设计方案，焦点是**多组件协同运动**与**与 Gooey 融合视觉的联动**。

仓库根目录：E:\Projects_Unity\MiHoYo
设计文档（评审对象）：E:\Projects_Unity\MiHoYo\docs\ui-motion-module-design.md
重点看 §2、§3.2、§7（L0–L4 五级耦合模型）、§8.2、§8.3、§7.7 的组合示例。

请实际打开并阅读源码，不要只信文档转述：
- Assets/Scripts/GooeyUIGroup.cs（CPU 遮罩：sprite 剪影栅格化 → 1/downsample 覆盖度缓冲 → 两次可分离盒式模糊≈高斯 → 阈值 smoothstep(T±S) → mask；LateUpdate 每帧重算；blurRadius 实测推荐 70~90px、threshold 0.55~0.60；有 sprite 缓存）
- Assets/Scripts/GooeyWarpEffect.cs（warpBlend ≈ 融合颈部半径、warpBand）
- Assets/Scripts/SdfBlendField.cs、Assets/Scripts/SdfFieldNode.cs、Assets/Scripts/SdfUiSpace.cs（p 空间换算）
- Assets/Scripts/GooeyUIGroup.cs 里的调参注释与"为什么不用相机→RenderTexture"的记录

## 评审焦点

1. L0（Stagger 时间编排）/ L1（Rig 装配体 + softness 软编队）/ L2（FollowChain 弧长回溯环形缓冲）/ L3（SpringCoupling 二阶阻尼 + 距离约束 PBD relax）/ L4（向量场驱动）这套五级模型：分级是否正交？有没有级别划分错误、级别之间会打架、或者漏掉了 UI 里真实存在的协同形态？
2. L1 的 softness 软编队公式 "w_i = lerp(1, w_i^authored, softness); p_i = T(w_i 加权位姿)·o_i" 是否良定义？"加权位姿"对旋转/缩放怎么插值才不出错（矩阵插值/双四元数/极分解）？文档这里是否含糊到无法实现？
3. L2 的"按弧长回溯而非按时间延迟"是否正确、环形缓冲容量估算（"最大总间距/最小位移步长 + 余量"、128~512 样本）是否够？leader 停止、方向反转、leader 瞬移（Seek/Handoff）时会怎样？
4. L3 的数值稳定性方案（临界阻尼弹簧解析闭式解 vs dt 钳制+子步、"收敛判据 + 冻结 + 吸附回目标"、ω/ζ 配方表）是否可靠？ω/ζ 那组数值（26/1.0、22/0.85、16/0.55、12/0.35）在这个项目的 UI 尺度下合理吗？PBD 距离约束与弹簧叠加会不会互相打架（弹簧是软的、约束是硬的）？约束层"默认关闭、opt-in"是否会让默认路径缺少必要保障？
5. L4 场驱动复用 SdfBlendField 的 p 空间：技术上真的可行吗？SDF 梯度在 UI 空间（含非 1 缩放、pivot、嵌套 RectTransform）下怎么算才正确？"视觉与运动同源"这个卖点是否成立，还是过度承诺？
6. §8.3 的 Gooey 联动三个点是否成立：
   (a) 用运动上报的"间距曲线"对 blurRadius 做补偿来消除融合桥通断闪烁 —— 在 blur+threshold 的实际算法下这真能消除闪烁吗，还是会引入更糟的形变/呼吸感？
   (b) 速度 → warpBlend 拉伸是否会产生视觉抖动或与 GooeyUIGroup 内部对 warpBlend 的驱动冲突？
   (c) "整组静止 → Gooey 跳过遮罩重算"的脏标记：GooeyUIGroup 现在的逐帧重算里有哪些部分本来就依赖每帧刷新，加脏标记会不会导致视觉不更新（例如 sprite 变化、分辨率变化、Canvas 缩放变化）？
7. §7.7 的组合示例（面板直线滑入 + 4 图标弧线扇开 + 弹簧维持间距 + 终点吸附回槽位 + Gooey 反馈）在工程上是否可落地？各子系统同时作用时的执行顺序（采样 → 协同 → 约束 → 绑定）是否会把彼此的结果破坏掉（例如约束层推挤后弹簧又拉回，形成振荡）？

## 输出要求

只读评审，不要修改任何文件。用中文输出，代码标识符保留英文。严格按下面结构：

1. 结论（≤3 行：这套协同模型该不该这么做、最大的风险是什么）
2. 严重问题（按严重度排序；每条给出：问题 / 为什么错 / 反例或反证 / 具体改法）
3. 文档与仓库实际代码不符之处（文件名+行号）
4. 数学与数值问题（公式、收敛性、量纲/尺度分析）
5. 遗漏场景
6. 过度设计（可以砍掉的）+ 更简单的替代方案
7. 如果由你实现，你会怎么改（具体到接口签名与数据结构）

必须批判，不要附和。能给具体证据（代码行、公式、可复现场景）的优先。
