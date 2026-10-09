using UnityEngine;

namespace GooeyUI
{
    /// <summary>
    /// 从 sprite 的 alpha 烘一张 **带 padding 的有符号距离场**。
    ///
    /// 为什么必须烘、不能"现估"：
    ///   d ≈ (a - 0.5) / |∇a| 只在 1px 的抗锯齿带里成立；形状**外面**（融合真正发生的缝隙）
    ///   alpha 恒为 0、梯度为 0 ⇒ smin 没有可用的距离。所以要把 alpha 变成"外延到 padding 之外"
    ///   的真实距离场，smin 才有意义。
    ///
    /// 结果贴图约定：R 通道 = 0.5 + sd / (2 * padding)，即 0.5 = 表面、0/1 = ±padding（与 TMP 同款约定）。
    /// </summary>
    public static class GooeySdf
    {
        public struct Baked
        {
            public Texture2D texture;
            public Vector2 innerSize;  // 内区（剪影所在）的 texel 尺寸
            public int padding;        // 每边 padding（texel）
            public bool valid;         // 是否成功烘出贴图
        }

        const float Inf = 1e9f; // 8SSEDT 里用的"无穷大"，足够大即可

        

        /// <summary>
        /// targetAspect = 目标矩形（RectTransform）的宽高比。
        /// Image 会把 sprite **拉伸到矩形**，所以 SDF 的内区也必须按矩形比例烘，
        /// 否则距离场的各向同性假设被破坏、形状会被拉歪（透明兜底还会糊出一圈白）。
        /// 传 0 表示沿用 sprite 自身 textureRect 的比例。
        /// </summary>
        public static Baked Bake(Sprite sprite, int resolution, int padding, float targetAspect)
        {
            var result = new Baked();

            if (sprite == null || sprite.texture == null)
                return result;

            int sw = Mathf.Max(2, Mathf.RoundToInt(sprite.textureRect.width));
            int sh = Mathf.Max(2, Mathf.RoundToInt(sprite.textureRect.height));

            Color32[] src = ReadSpritePixels(sprite, sw, sh);
            if (src == null)
                return result;

            // 内区分辨率（长边 = resolution），比例用目标矩形比例
            float aspect = targetAspect > 0.0001f ? targetAspect : sw / (float)sh;
            int iw;
            int ih;
            if (aspect >= 1f)
            {
                iw = Mathf.Clamp(resolution, 8, 512);
                ih = Mathf.Max(8, Mathf.RoundToInt(iw / Mathf.Max(aspect, 0.0001f)));
            }
            else
            {
                ih = Mathf.Clamp(resolution, 8, 512);
                iw = Mathf.Max(8, Mathf.RoundToInt(ih * aspect));
            }

            padding = Mathf.Clamp(padding, 4, 256);
            int W = iw + padding * 2;
            int H = ih + padding * 2;

            // 二值剪影（box 平均后阈值 0.5）
            bool[] inside = new bool[W * H];
            for (int y = 0; y < ih; y++)
            {
                int sy0 = y * sh / ih;
                int sy1 = Mathf.Max(sy0 + 1, (y + 1) * sh / ih);
                for (int x = 0; x < iw; x++)
                {
                    int sx0 = x * sw / iw;
                    int sx1 = Mathf.Max(sx0 + 1, (x + 1) * sw / iw);

                    float sum = 0f;
                    int cnt = 0;
                    for (int yy = sy0; yy < sy1; yy++)
                    {
                        int baseIdx = yy * sw;
                        for (int xx = sx0; xx < sx1; xx++)
                        {
                            sum += src[baseIdx + xx].a / 255f;
                            cnt++;
                        }
                    }

                    float cov = cnt > 0 ? sum / cnt : 0f;
                    inside[(y + padding) * W + (x + padding)] = cov >= 0.5f;
                }
            }

            // 8SSEDT：分别求"到最近 inside / 到最近 outside"的距离
            float[] dIn = Edt(inside, W, H, true);
            float[] dOut = Edt(inside, W, H, false);

            var px = new Color[W * H];
            for (int i = 0; i < px.Length; i++)
            {
                float sd = dIn[i] - dOut[i];                       // 外正内负
                // EDT measures between pixel centres; the alpha boundary is halfway between them.
                sd -= Mathf.Sign(sd) * 0.5f;
                float n = Mathf.Clamp01(0.5f + sd / (2f * padding));
                px[i] = new Color(n, n, n, 1f);
            }

            // Distances are data, not sRGB colour. Half precision also avoids visible distance steps.
            var tex = new Texture2D(W, H, TextureFormat.RGBAHalf, false, true)
            {
                name = "GooeySDF_" + sprite.name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            tex.SetPixels(px);
            tex.Apply(false);

            result.texture = tex;
            result.innerSize = new Vector2(iw, ih);
            result.padding = padding;
            result.valid = true;
            return result;
        }
        
        public static Baked Bake(Sprite sprite, int resolution, int padding) // 沿用 sprite 自身 textureRect 的比例
        {
            return Bake(sprite, resolution, padding, 0f);
        }

        /// <summary>读 sprite 在贴图里的像素（sprite 贴图默认不可读，用 Blit 取到 RT 再读回）。</summary>
        public static Color32[] ReadSpritePixels(Sprite sprite, int w, int h)
        {
            Texture2D tex = sprite.texture;
            if (tex == null)
                return null;

            Rect tr = sprite.textureRect;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            RenderTexture prev = RenderTexture.active;

            Color32[] result = null;
            try
            {
                Graphics.Blit(
                    tex, rt,
                    new Vector2(tr.width / Mathf.Max(1f, tex.width), tr.height / Mathf.Max(1f, tex.height)),
                    new Vector2(tr.x / Mathf.Max(1f, tex.width), tr.y / Mathf.Max(1f, tex.height)));

                RenderTexture.active = rt;
                var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                read.Apply();
                result = read.GetPixels32();
                if (Application.isPlaying)
                    Object.Destroy(read);
                else
                    Object.DestroyImmediate(read);
            }
            catch
            {
                result = null;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }

            return result;
        }

        /// <summary>8SSEDT（2 遍扫描的近似欧氏距离变换），返回以 texel 为单位的距离。</summary>
        static float[] Edt(bool[] inside, int w, int h, bool toInside)
        {
            int n = w * h;
            int[] gx = new int[n];
            int[] gy = new int[n];
            float[] dist = new float[n];

            for (int i = 0; i < n; i++)
            {
                bool seed = toInside ? inside[i] : !inside[i];
                if (seed)
                {
                    gx[i] = i % w;
                    gy[i] = i / w;
                    dist[i] = 0f;
                }
                else
                {
                    dist[i] = Inf;
                }
            }

            // 第 1 遍：左上 -> 右下
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Compare(gx, gy, dist, w, h, x, y, -1, 0);
                    Compare(gx, gy, dist, w, h, x, y, 0, -1);
                    Compare(gx, gy, dist, w, h, x, y, -1, -1);
                    Compare(gx, gy, dist, w, h, x, y, 1, -1);
                }
            }

            // 第 2 遍：右下 -> 左上
            for (int y = h - 1; y >= 0; y--)
            {
                for (int x = w - 1; x >= 0; x--)
                {
                    Compare(gx, gy, dist, w, h, x, y, 1, 0);
                    Compare(gx, gy, dist, w, h, x, y, 0, 1);
                    Compare(gx, gy, dist, w, h, x, y, 1, 1);
                    Compare(gx, gy, dist, w, h, x, y, -1, 1);
                }
            }

            for (int i = 0; i < n; i++)
                dist[i] = Mathf.Sqrt(dist[i]);

            return dist;
        }

        static void Compare(int[] gx, int[] gy, float[] dist, int w, int h, int x, int y, int ox, int oy)
        {
            int nx = x + ox;
            int ny = y + oy;
            if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                return;

            int i = y * w + x;
            int j = ny * w + nx;

            if (dist[j] >= Inf)
                return;

            int cx = gx[j] - x;
            int cy = gy[j] - y;
            float d = cx * cx + cy * cy;

            if (d < dist[i])
            {
                dist[i] = d;
                gx[i] = gx[j];
                gy[i] = gy[j];
            }
        }
    }
}
