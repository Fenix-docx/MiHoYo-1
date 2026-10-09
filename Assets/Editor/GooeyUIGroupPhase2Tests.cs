using NUnit.Framework;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

namespace GooeyUI.Tests
{
    /// <summary>
    /// 逐像素 SDF 交集融合的编辑期测试。
    /// 注：早期版本的 "composite（把整个面画进一张贴图）" 与 "warp（网格形变）" 两条路
    /// 已按需求移除，测试同步改为验证 SDF 这条路。
    /// </summary>
    public class GooeyUIGroupPhase2Tests
    {
        [Test]
        public void Fusion_HidesSourceVisuals_WhileKeepingRaycastTargets()
        {
            GameObject root = new GameObject("GooeyFusionCanvas", typeof(RectTransform), typeof(Canvas));
            root.SetActive(false);
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            GameObject displayRoot = new GameObject("GooeyFusionDisplayCanvas", typeof(RectTransform), typeof(Canvas));
            displayRoot.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            RawImage display = new GameObject("FusionLayer", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            display.transform.SetParent(displayRoot.transform, false);

            GooeyUIGroup group = root.AddComponent<GooeyUIGroup>();
            SetDisplayLayer(group, display);

            GameObject first = new GameObject("First", typeof(RectTransform), typeof(Image));
            GameObject second = new GameObject("Second", typeof(RectTransform), typeof(Image));
            first.transform.SetParent(root.transform, false);
            second.transform.SetParent(root.transform, false);

            Image[] images = { first.GetComponent<Image>(), second.GetComponent<Image>() };
            Texture2D art = MakeCircle();
            Sprite sprite = Sprite.Create(art, new Rect(0, 0, 64, 64), Vector2.one * 0.5f);
            for (int i = 0; i < images.Length; i++)
            {
                images[i].sprite = sprite;
                images[i].rectTransform.sizeDelta = new Vector2(120f, 120f);
                images[i].raycastTarget = true;
            }

            images[0].canvasRenderer.SetAlpha(0.7f);
            try
            {
                root.SetActive(true);
                group.Rebuild();
                group.Refresh();
                Assert.That(group.ActiveSourceCount, Is.EqualTo(2));

                for (int i = 0; i < images.Length; i++)
                {
                    Assert.That(images[i].canvasRenderer.GetAlpha(), Is.EqualTo(0f).Within(0.001f),
                        images[i].name + " 应由融合层独占绘制。Active=" + group.ActiveSourceCount);
                    Assert.That(images[i].raycastTarget, Is.True,
                        images[i].name + " 必须仍是交互目标（只关渲染，不动 raycast）。");
                }
                group.enabled = false;
                Assert.That(images[0].canvasRenderer.GetAlpha(), Is.EqualTo(0.7f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(displayRoot);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(art);
            }
        }

        [Test]
        public void BakedSdf_HasCorrectSignAndPadding()
        {
            // 造一个 64×64、中心是实心圆、四周透明的 sprite
            const int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    byte a = (byte)(d <= 20f ? 255 : 0);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            GooeySdf.Baked baked = new GooeySdf.Baked();

            try
            {
                baked = GooeySdf.Bake(sprite, 64, 24, 1f);
                Assert.That(baked.valid, Is.True, "SDF 烘制失败");
                Assert.That(baked.texture, Is.Not.Null);
                Assert.That(baked.texture.width, Is.EqualTo(64 + 48), "宽度应为内区 + 两侧 padding");
                Assert.That(baked.padding, Is.EqualTo(24));
                Assert.That(baked.texture.isDataSRGB, Is.False, "Distance data must not be gamma decoded.");

                Color32[] sdf = baked.texture.GetPixels32();
                int w = baked.texture.width;
                int c = w / 2;

                // 约定：0.5 = 表面、<0.5 在内、>0.5 在外
                float inside = sdf[c * w + c].r / 255f;
                float outside = sdf[2 * w + 2].r / 255f;
                Assert.That(inside, Is.LessThan(0.5f), "圆心应当在内（<0.5）");
                Assert.That(outside, Is.GreaterThan(0.5f), "角落应当在外（>0.5）");
            }
            finally
            {
                if (baked.texture != null) Object.DestroyImmediate(baked.texture);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void DrawingRect_MatchesUnityImage_ForTrimmedSpriteAndPreserveAspect()
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath("41a85c88cc395114e9b6c1b146910e2a");
            Sprite sprite = null;
            foreach (var asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is Sprite) { sprite = (Sprite)asset; break; }
            Assert.That(sprite, Is.Not.Null, "Showcase button sprite missing.");
            var go = new GameObject("DrawingRectTest", typeof(RectTransform), typeof(Image));
            try
            {
                Image image = go.GetComponent<Image>();
                image.sprite = sprite;
                image.rectTransform.pivot = new Vector2(0.2f, 0.8f);
                MethodInfo unityRect = typeof(Image).GetMethod("GetDrawingDimensions", BindingFlags.Instance | BindingFlags.NonPublic);
                foreach (bool preserve in new[] { false, true })
                foreach (float width in new[] { 220f, -220f })
                {
                    image.preserveAspect = preserve;
                    image.rectTransform.sizeDelta = new Vector2(width, 130f);
                    Vector4 expected = (Vector4)unityRect.Invoke(image, new object[] { preserve });
                    Rect actual = GooeyUIGroup.GetSpriteDrawingRect(image, sprite);
                    Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f));
                    Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f));
                    Assert.That(actual.xMax, Is.EqualTo(expected.z).Within(0.001f));
                    Assert.That(actual.yMax, Is.EqualTo(expected.w).Within(0.001f));
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void SharedSprite_DifferentAspects_KeepBothDistanceTexturesAlive()
        {
            var root = new GameObject("CacheTest", typeof(RectTransform), typeof(Canvas));
            root.SetActive(false);
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var displayRoot = new GameObject("CacheDisplay", typeof(RectTransform), typeof(Canvas));
            var display = new GameObject("Display", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            display.transform.SetParent(displayRoot.transform, false);
            var group = root.AddComponent<GooeyUIGroup>();
            SetDisplayLayer(group, display);
            var tex = MakeCircle();
            var sp = Sprite.Create(tex, new Rect(0, 0, 64, 64), Vector2.one * 0.5f);
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    var image = new GameObject("Source", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                    image.transform.SetParent(root.transform, false);
                    // Exercise overrideSprite, which is what Image actually renders.
                    image.overrideSprite = sp;
                    image.rectTransform.sizeDelta = new Vector2(i == 0 ? 120 : 200, 120);
                }
                root.SetActive(true);
                group.Refresh();
                Texture a = group.FusionMaterial.GetTexture("_Sdf0");
                Texture b = group.FusionMaterial.GetTexture("_Sdf1");
                group.Refresh();
                Assert.That(group.ActiveSourceCount, Is.EqualTo(2));
                Assert.That(a != null && b != null, Is.True);
                Assert.That(a, Is.Not.SameAs(b));
                Assert.That(group.FusionMaterial.GetTexture("_Sdf0"), Is.SameAs(a));
                Assert.That(group.FusionMaterial.GetTexture("_Sdf1"), Is.SameAs(b));
            }
            finally
            {
                Object.DestroyImmediate(root); Object.DestroyImmediate(displayRoot);
                Object.DestroyImmediate(sp); Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void ShaderPixels_AlignWithDisplayRect_AndFuseThenSeparate()
        {
            // Exercise the actual shader on the GPU without any camera/RT rendering path.
            // Non-default display UVs and an offset output rect catch the previous coordinate bug.
            var art = MakeCircle();
            var sprite = Sprite.Create(art, new Rect(0, 0, 64, 64), Vector2.one * 0.5f);
            var baked = GooeySdf.Bake(sprite, 64, 64, 1f);
            var mat = new Material(Shader.Find("UI/SdfFusionUI"));
            var target = new RenderTexture(256, 160, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var mesh = new Mesh();
            var read = new Texture2D(256, 160, TextureFormat.RGBA32, false, true);
            RenderTexture previous = RenderTexture.active;
            try
            {
                target.Create();
                mat.SetVector("_DisplayUvRect", new Vector4(0.2f, 0.3f, 0.5f, 0.4f));
                mat.SetVector("_DisplayScreenOrigin", new Vector4(100, 200, 0, 0));
                mat.SetVector("_DisplayScreenAxes", new Vector4(256, 0, 0, 160));
                mat.SetFloat("_Softness", 0.5f);
                for (int i = 0; i < 2; i++)
                {
                    mat.SetTexture("_Sdf" + i, baked.texture);
                    mat.SetTexture("_Art" + i, art);
                    mat.SetVector("_SdfInfo" + i, new Vector4(64, 64, 1, 64));
                    mat.SetVector("_ArtUv" + i, new Vector4(0.5f, 0.5f, 0.5f, 0.5f));
                    mat.SetColor("_ArtTint" + i, Color.white);
                }
                mat.SetVector("_Rect0", new Vector4(192, 280, 32, 32));
                mat.SetVector("_Rect1", new Vector4(264, 280, 32, 32));
                mesh.vertices = new[] { new Vector3(0, 0), new Vector3(0, 160), new Vector3(256, 160), new Vector3(256, 0) };
                mesh.uv = new[] { new Vector2(0.2f, 0.3f), new Vector2(0.2f, 0.7f), new Vector2(0.7f, 0.7f), new Vector2(0.7f, 0.3f) };
                mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                mesh.triangles = new[] { 0, 1, 2, 2, 3, 0 };

                mat.SetFloat("_BlendK", 1);
                RenderPixels(mat, mesh, target, read);
                Assert.That(read.GetPixel(92, 80).r, Is.GreaterThan(0.9f), "Source centre must stay aligned.");
                Assert.That(read.GetPixel(128, 80).r, Is.LessThan(0.05f), "Original alpha gap must be empty.");
                mat.SetFloat("_BlendK", 64);
                RenderPixels(mat, mesh, target, read);
                for (int x = 92; x <= 164; x++)
                    Assert.That(read.GetPixel(x, 80).r, Is.GreaterThan(0.9f), "Neck must be continuous at pixel " + x);
                Assert.That(read.GetPixel(128, 10).r, Is.LessThan(0.05f), "Union must not fill distant background.");

                // A sprite with a dark outline must carry its body colour through the neck.
                // Sampling only the old boundary would leave a dark connector and internal circular seams.
                var ring = art.GetPixels32();
                for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float radius = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(32, 32));
                    if (radius >= 22 && radius < 26)
                        ring[y * 64 + x] = new Color32(0, 0, 255, 255);
                }
                art.SetPixels32(ring); art.Apply();
                RenderPixels(mat, mesh, target, read);
                Assert.That(read.GetPixel(128, 80).r, Is.GreaterThan(0.9f), "Neck must inherit the body colour.");
                Assert.That(read.GetPixel(118, 80).r, Is.GreaterThan(0.9f), "Old internal outline must move with the merged surface.");
                mat.SetVector("_Rect1", new Vector4(328, 280, 32, 32));
                RenderPixels(mat, mesh, target, read);
                Assert.That(read.GetPixel(160, 80).r, Is.LessThan(0.05f), "Moving sources apart must reopen the gap.");
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(mesh); Object.DestroyImmediate(mat); Object.DestroyImmediate(target);
                Object.DestroyImmediate(read); Object.DestroyImmediate(baked.texture);
                Object.DestroyImmediate(sprite); Object.DestroyImmediate(art);
            }
        }

        static void RenderPixels(Material mat, Mesh mesh, RenderTexture target, Texture2D read)
        {
            var cmd = new CommandBuffer();
            try
            {
                cmd.SetRenderTarget(target);
                cmd.ClearRenderTarget(false, true, Color.clear);
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0, 256, 0, 160, -1, 1));
                cmd.DrawMesh(mesh, Matrix4x4.identity, mat);
                Graphics.ExecuteCommandBuffer(cmd);
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, 256, 160), 0, 0);
                read.Apply();
            }
            finally { cmd.Release(); }
        }

        static Texture2D MakeCircle()
        {
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var pixels = new Color32[64 * 64];
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                pixels[y * 64 + x] = new Color32(255, 255, 255,
                    (byte)(Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(32, 32)) < 26 ? 255 : 0));
            tex.SetPixels32(pixels); tex.Apply();
            return tex;
        }

        static void SetDisplayLayer(GooeyUIGroup group, RawImage display)
        {
            FieldInfo field = typeof(GooeyUIGroup).GetField("displayLayer", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "GooeyUIGroup.displayLayer 字段缺失");
            field.SetValue(group, display);
        }
    }
}
