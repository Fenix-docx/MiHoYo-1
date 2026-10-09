# 评审任务 C：导航排队 / 异步化 / 写入权仲裁

你在评审一个 Unity 6 项目（uGUI）的"UI 2D 运动模块"设计方案，焦点是**与现有导航/面板体系的集成、异步化与导航排队、写入权仲裁**。

仓库根目录：E:\Projects_Unity\MiHoYo
设计文档（评审对象）：E:\Projects_Unity\MiHoYo\docs\ui-motion-module-design.md
重点看 §2、§8.1、§8.2、§8.4、§8.5（导航排队，这是刚定的核心决策）、§8.6、§13 测试 12–20、§15。

**必须实际打开并逐行阅读这些源码**（评审要建立在真实代码上，尤其要判断文档对现有行为的描述是否准确）：
- Assets/A_UI/UI/Package/Navigation/UINavigator.cs（410 行：navStack、Push/Back/Replace/ResetToRoot/CloseAll、SetOpen、Navigated 事件、nodeByPage/pageById 派生缓存、RebuildCache/EnsureCache、RemoveNode 清栈残留、restrictToDirectChildren 校验）
- Assets/A_UI/UI/Package/Navigation/UINavButton.cs（注意它是否消费 Push/Back 的返回值）
- Assets/A_UI/UI/Package/Navigation/UINavNode.cs、UINavigationLocator.cs、Editor/UINavigationValidator.cs、Editor/UINavigationEditorWindow.cs
- Assets/A_UI/UI/Package/UIbase.cs（现在是同步 SetActive，IsOpen === activeSelf）
- Assets/A_UI/UI/Package/UIManager.cs（PanelId 注册、重复报错风格）
- Assets/A_UI/UI/Package/Subpanel.cs
- Assets/Editor/GooeyUIGroupPhase2Tests.cs（项目测试风格）

## 评审焦点

1. §8.5 的导航排队设计：不变量"栈推进与视觉推进同拍"是否真的能成立？请沿着 UINavigator.cs 的真实代码逐条推演：Push 里的 closeParentOnPush 分支、Back 的栈深判断、Replace 的先 Pop 再 Push、ResetToRoot 的 while 清栈、CloseAll 的遍历关闭 —— 异步化 + 排队之后各自会发生什么？有没有哪个方法在排队模型下语义崩掉？
2. 合并规则表（同 target 折叠 / Back ×N 的 fastForwardThreshold=3 / ToRoot 清空队列 / maxQueue=8 丢最新）是否有反例？用户真实操作序列里哪些会得到意外结果？"丢弃最新一条"是否真的比"丢弃最旧"更好？
3. 看门狗（deadline = expectedDuration*3+0.5s，判定顺序 IsDone → 句柄失效 → unscaledTime 兜底）是否足够？expectedDuration 从哪来、可靠吗？ForceComplete 把面板置终态时如何保证与运动模块内部状态一致（会不会出现"看门狗置了终态但运动轨道还在跑并继续写 anchoredPosition"）？
4. §8.5 宣称的 API 契约变化（返回值语义变"已接受"、Current 滞后、Navigated 改为出队时触发、新增 IsNavigating/NavigationIdle）：这会不会破坏现有调用方？请把所有调用点找出来核实（不只 UINavButton）。"UINavButton 零改动"这个结论对吗？
5. 输入屏蔽方案（转移期间离开面板 CanvasGroup.blocksRaycasts=false）：如果面板没有 CanvasGroup 怎么办？与 Button/Selectable 的 interactable、与 EventSystem 当前选中项（selectedGameObject）、与键盘/手柄导航（InputSystem）会不会出现"选中已屏蔽按钮"的悬空状态？
6. §8.1 的写入权仲裁（LayoutGroup 用 LayoutElement.ignoreLayout、Animator 由 Validator 报错、DOTween 靠静态声明自律、ScrollRect 要求不在滚动内容下、ContentSizeFitter 只允许改位置）：这套"静态声明 + 报错"的策略够不够？有没有更可靠的运行时手段？`ignoreLayout` 的还原时机、以及运动中改变 rect 尺寸引发 LayoutRebuilder 重建的时序问题？
7. §8.2 采样相位结论（运动统一在 Update 推进，靠"Update 全早于 LateUpdate"保证与 GooeyUIGroup 的相位关系）是否正确？请核实 GooeyUIGroup 的实际更新位置与它读取 rect 的时机，判断有没有一帧延迟/抖动。
8. §13 的导航测试 12–20 是否足以覆盖这套设计？哪些关键场景没被测试覆盖？P0 把这些全塞进去是否现实？

## 输出要求

只读评审，不要修改任何文件。用中文输出，代码标识符保留英文。严格按下面结构：

1. 结论（≤3 行：排队这个决策对不对、集成方案最大的风险是什么）
2. 严重问题（按严重度排序；每条给出：问题 / 为什么错 / 反例或反证（最好给出具体的调用序列）/ 具体改法）
3. 文档与仓库实际代码不符之处（文件名+行号，逐条核实文档里对现有代码的每一处断言）
4. 遗漏场景（真实用户操作序列 + 现有代码路径）
5. 过度设计（可以砍掉的）+ 更简单的替代方案（含"根本不该异步化/不该排队"的反方论证，如果你认为成立）
6. 如果由你实现，你会怎么改（具体到接口签名与调用序列）

必须批判，不要附和。这一轮的重点是"用真实代码把设计里想当然的地方打穿"。
