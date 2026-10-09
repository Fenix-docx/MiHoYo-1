# 评审任务 A：核心运动引擎与几何/数值

你在评审一个 Unity 6 项目（uGUI + HDRP/URP 共存）的"UI 2D 运动模块"设计方案。

仓库根目录：E:\Projects_Unity\MiHoYo
设计文档（评审对象）：E:\Projects_Unity\MiHoYo\docs\ui-motion-module-design.md
请实际打开并阅读相关源码，不要只信文档的转述：
- Assets/Scripts/GooeyUIGroup.cs（Gooey 融合，LateUpdate 每帧 CPU 重算遮罩）
- Assets/Scripts/SdfUiSpace.cs（UI ↔ p 空间换算约定）
- Assets/Scripts/GooeyWarpEffect.cs
- Assets/A_UI/UI/Package/UIbase.cs、UIManager.cs、Navigation/UINavigator.cs、Navigation/UINavButton.cs、Navigation/UINavNode.cs
- 项目测试惯例：Assets/Editor/GooeyUIGroupPhase2Tests.cs、Assets/Slime/Tests/Editor/

## 评审焦点（只评这一块，别的轴有别人评）

核心运动引擎与几何/数值：
1. "位置是时间的纯函数 p=f(t)，动力学只做后处理"这个核心模型是否成立？在 UI 场景下有什么它做不到或做得很别扭的事？
2. 时间重映射 u=Ease(t/dur) 与几何 p=Path(u) 正交：过冲外推（u<0 / u>1 沿端点切线延长）的设计是否正确、够不够？
3. 弧长参数化的实现约定（每段 LUT 采样数 Line:2 / Bezier:32 / Arc 解析、二分+插值、"std/mean<2%"的验收标准）是否站得住？采样数、误差界、边界情况（退化段、零长段、控制点共线）有什么问题？
4. AnchorRef 延迟解析、BindSpace/PathSpace 分离、跨父级 Reparent（SetParent(worldPositionStays:true) 会改写 localScale/localRotation）——这套设计有没有坑没被写出来？
5. 数据结构选型（blittable struct + switch 分发 vs interface；SoA；"托管数组足够，Burst 可选"）与 §9.3 的性能预算量级估算（200 元素 × 4 通道 ≈ 0.03–0.08 ms/帧）是否可信？有没有算错或漏掉的大头（RectTransform 写入本身、Canvas 重建/合批、SetParent、LayoutRebuilder）？
6. §10 中断/跳过/回放：Handoff（捕获当前位置与速度、C1 匹配）、Skip 必须触发 OnComplete、状态化通道在 Seek 时"固定步长重演 + 步数上限"是否可行？重演成本被低估了吗？
7. §12 文件清单与 §14 P0–P4 阶段划分是否合理，P0 的范围是否真的"最小闭环"？

## 输出要求

只读评审，不要修改任何文件。用中文输出，代码标识符保留英文。严格按下面结构：

1. 结论（≤3 行：这个核心模型该不该这么做）
2. 严重问题（按严重度排序；每条给出：问题 / 为什么错 / 反例或反证 / 具体改法）
3. 文档与仓库实际代码不符之处（给出文件名+行号）
4. 数学与数值问题（给出公式或误差分析）
5. 遗漏场景（文档没提但一定会遇到的）
6. 过度设计（可以砍掉的）+ 更简单的替代方案
7. 如果由你实现，你会怎么改（具体到接口签名与数据结构）

必须批判，不要附和。能给具体证据（代码行、公式、可复现场景）的优先。
