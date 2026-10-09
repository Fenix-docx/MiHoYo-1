# SDF UI 平滑融合

## 两个独立 UI GameObject 的融合

`UI/SdfBlendUI` 不能直接让两个独立 `Image` 在两个 Draw Call 中互相读取 SDF。因此使用一个公共融合面执行最终的 `smin`：

```text
Canvas
├── SDF_A       Image/RawImage + SdfUiController
├── SDF_B       Image/RawImage + SdfUiController
└── BlendSurface Image/RawImage + SdfUiBlendGroup
```

### 配置步骤

1. 在 Canvas 下创建两个独立的 `Image` 或 `RawImage`，分别命名为 `SDF_A` 和 `SDF_B`。
2. 给两个对象分别挂载 `Assets/Scripts/SdfUiController.cs`。
3. 分别设置两个对象的 `Radius`；半径单位是融合面的半高比例，例如 `0.42`。
4. 创建第三个 `Image` 或 `RawImage`，命名为 `BlendSurface`。
5. 创建一个 Material，将 Shader 设置为 `UI/SdfBlendUI`，把材质挂到 `BlendSurface`，并同时拖到 `SdfUiBlendGroup` 的 `Blend Material` 字段。
6. 给 `BlendSurface` 挂载 `Assets/Scripts/SdfUiBlendGroup.cs`。如果没有手动绑定材质，控制器会运行时通过 `Shader.Find("UI/SdfBlendUI")` 创建实例；只有找不到 Shader 时才会报错。
7. 将 `SDF_A` 和 `SDF_B` 的 `SdfUiController` 分别拖到 `Shape 1` 和 `Shape 2`。
8. `Auto Fit Surface` 默认开启，控制器会让融合面覆盖两个对象的包围区域；也可以关闭后手动设置融合面大小。
9. 调节 `Blend K`：值越大，两个独立对象之间的融合越黏；值越小，边界越接近硬切换。

运行时，`SdfUiBlendGroup` 会隐藏两个源 Image 的实际绘制，只把它们作为独立 SDF 数据源；最终画面由公共 `BlendSurface` 输出。两个源对象仍然可以独立移动、缩放和通过代码修改 Transform。

### 动态控制

勾选任一 `SdfUiController` 的 `Drive Position With Pointer`，对应的 SDF 中心会跟随鼠标。也可以直接修改两个源对象的 `RectTransform.position`、`anchoredPosition` 或 `SdfUiController.Radius`。

`BlendSurface` 支持 `Mask`、`RectMask2D`、Stencil 和 UI Alpha Clip。Canvas 使用 `Screen Space - Overlay` 或 `Screen Space - Camera` 均可。

> 融合 Shader 在公共融合面中将两个独立对象的位置转换到同一坐标系，然后执行 `smin(sd1, sd2, BlendK)`。这与在单个 GameObject 内预设两个圆心不同。
