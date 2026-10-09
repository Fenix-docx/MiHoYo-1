# Gooey UI 修复验证

Unity 6000.3.24f1 / URP，编辑器内验证。

修改文件：Assets/Scripts/GooeyUIGroup.cs、Assets/Scripts/GooeySdf.cs、Assets/A_UI/SdfFusionUI.shader、Assets/Editor/GooeyUIGroupPhase2Tests.cs。

- 显示层 UV 先还原其 uvRect，再由实际四角映入与源 Image 一致的屏幕坐标。避免 Game View DPI / _ScreenParams 与 C# Screen 坐标不一致造成偏移。
- Simple Image 的透明裁边、preserveAspect、overrideSprite 和镜像半尺寸按 Unity 实际绘制规则处理。
- SDF 使用线性 RGBAHalf 数据；按内区两轴尺寸映射 padding，边界外增加距离。
- 完整平滑并集控制逐像素覆盖；美术按最终内部距离重新采样，使旧描边随融合表面移动，颈部继承内部颜色。
- 相同 Sprite 的不同长宽比使用独立缓存；恢复隐藏组件的原 alpha，保持 raycastTarget。

最终 EditMode 测试 5/5 Passed，job_id: 92db6bc3f901448ab0a5ee02cd57ab1b。包含 GPU 实际像素的对齐、连通、分离与描边/内部颜色回归。控制台错误 0，ShaderHasError=False。

实拍图：
- gooey-final-fusion.png：近距离，BlendK=64，中心间距146，连续融合并保留各自美术。
- gooey-far-verified.png：同参数，中心间距280，正常分离。
- gooey-original-images.png / gooey-separate-alignment.png：同位置下原始 Image 与融合层（K=1）对比。

两侧轮廓像素包围盒（左上坐标）：
- 原始：左 (275,181)-(401,281)，右 (421,181)-(546,281)
- 融合：左 (275,181)-(400,280)，右 (421,181)-(546,280)
- 最大差异 1 像素，无位置漂移。

截图位于工程 .dsh-scratch/gooey-fix，未创建 Assets/Screenshots 内容。测试布局仅在 Play 中改变，结束后已退出 Play，未保存/覆盖用户场景布局。用户现有两个 RectTransform 宽度为负数，故文字竖排和精灵镜像属于当前布局；本次保持其状态。
