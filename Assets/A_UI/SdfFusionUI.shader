// =============================================================================
//  UI/SdfFusionUI —— 基于纹理边缘的**逐像素 SDF 交集融合**
// -----------------------------------------------------------------------------
//  这是"Sprite 真融合"的着色器：它不画桥、也不形变网格，而是对每个像素做
//      d = smin( d0, d1, ... , _BlendK )
//  其中 di 来自每个 sprite **自己 alpha 烘出来的带 padding 距离场**（见 GooeySdf.cs）。
//
//  为什么必须烘 padding：形状外面的缝隙里 alpha 恒为 0、梯度为 0，
//  现估距离拿不到值 ⇒ smin 无从计算。烘出的 SDF 在 padding 范围内都有正确距离 ✓
//
//  缝隙里的颜色怎么来：先求完整融合场，再沿各自 SDF 梯度把美术坐标映到同一内部深度。
//  原来两条圆形描边也会随融合轮廓向外移动，颈部继承内部颜色，而不是重复画边界像素。
//  颜色/高光/渐变自动连续（不需要任何"取色贴图"）。
//
//  由 Assets/Scripts/GooeyUIGroup.cs 每帧写入 4 个槽位的参数：
//     _RectI    : xy = 裁边后的精灵中心, zw = 有符号半尺寸（共同屏幕坐标）
//     _SdfInfoI : x = padding(texel), y = 内区 texel 宽, z = 1 有效 / 0 无效, w = 内区高
//     _ArtUvI   : xy = 美术 uv 中心, zw = 美术 uv 半尺寸
//     _SdfI / _ArtI / _ArtTintI
// =============================================================================
Shader "UI/SdfFusionUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)

        _BlendK ("Blend K (px)", Range(0.1, 400)) = 40
        _Softness ("Edge Softness (px)", Range(0.05, 20)) = 1.2
        _DisplayUvRect ("Display UV Rect", Vector) = (0, 0, 1, 1)
        _DisplayScreenOrigin ("Display Screen Origin", Vector) = (0, 0, 0, 0)
        _DisplayScreenAxes ("Display Screen Axes", Vector) = (1, 0, 0, 1)

        // 注意：这 4 组槽位参数必须写进 Properties 块，
        // 否则 Material.SetTexture/SetVector 会静默失效（材质层只能写"材质属性"）。
        _Sdf0 ("SDF 0", 2D) = "white" {}
        _Sdf1 ("SDF 1", 2D) = "white" {}
        _Sdf2 ("SDF 2", 2D) = "white" {}
        _Sdf3 ("SDF 3", 2D) = "white" {}
        _Art0 ("Art 0", 2D) = "white" {}
        _Art1 ("Art 1", 2D) = "white" {}
        _Art2 ("Art 2", 2D) = "white" {}
        _Art3 ("Art 3", 2D) = "white" {}
        _Rect0 ("Rect 0", Vector) = (0, 0, 0, 0)
        _Rect1 ("Rect 1", Vector) = (0, 0, 0, 0)
        _Rect2 ("Rect 2", Vector) = (0, 0, 0, 0)
        _Rect3 ("Rect 3", Vector) = (0, 0, 0, 0)
        _SdfInfo0 ("SDF Info 0", Vector) = (0, 1, 0, 0)
        _SdfInfo1 ("SDF Info 1", Vector) = (0, 1, 0, 0)
        _SdfInfo2 ("SDF Info 2", Vector) = (0, 1, 0, 0)
        _SdfInfo3 ("SDF Info 3", Vector) = (0, 1, 0, 0)
        _ArtUv0 ("Art UV 0", Vector) = (0, 0, 0, 0)
        _ArtUv1 ("Art UV 1", Vector) = (0, 0, 0, 0)
        _ArtUv2 ("Art UV 2", Vector) = (0, 0, 0, 0)
        _ArtUv3 ("Art UV 3", Vector) = (0, 0, 0, 0)
        _ArtTint0 ("Art Tint 0", Color) = (1, 1, 1, 1)
        _ArtTint1 ("Art Tint 1", Color) = (1, 1, 1, 1)
        _ArtTint2 ("Art Tint 2", Color) = (1, 1, 1, 1)
        _ArtTint3 ("Art Tint 3", Color) = (1, 1, 1, 1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        _ClipRect ("Clip Rect", Vector) = (-32767, -32767, 32767, 32767)

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Fog { Mode Off }
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "SdfFusionUI"

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;
            float _BlendK;
            float _Softness;
            float4 _DisplayUvRect, _DisplayScreenOrigin, _DisplayScreenAxes;

            sampler2D _Sdf0; sampler2D _Sdf1; sampler2D _Sdf2; sampler2D _Sdf3;
            sampler2D _Art0; sampler2D _Art1; sampler2D _Art2; sampler2D _Art3;
            float4 _Rect0; float4 _Rect1; float4 _Rect2; float4 _Rect3;
            float4 _SdfInfo0; float4 _SdfInfo1; float4 _SdfInfo2; float4 _SdfInfo3;
            float4 _ArtUv0; float4 _ArtUv1; float4 _ArtUv2; float4 _ArtUv3;
            fixed4 _ArtTint0; fixed4 _ArtTint1; fixed4 _ArtTint2; fixed4 _ArtTint3;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            // Decode in the source's coordinate system; x/y padding must use their own dimensions.
            float SampleDistance(sampler2D sdf, float2 px, float4 rect, float4 info)
            {
                float2 inner = max(float2(info.y, info.w), 1.0);
                float2 size = abs(rect.zw) * 2.0;
                float2 q = (px - rect.xy) / rect.zw;
                float2 paddedSize = inner + 2.0 * info.x;
                float2 uv = (0.5 * (q + 1.0) * inner + info.x) / paddedSize;
                float scale = min(size.x / inner.x, size.y / inner.y);
                float sd = (tex2Dlod(sdf, float4(saturate(uv), 0, 0)).r - 0.5) * 2.0 * info.x * scale;
                // A clamped texture alone gives a constant distance arbitrarily far away.
                return sd + length((uv - saturate(uv)) * paddedSize * size / inner);
            }

            float Smin(float a, float b, float k)
            {
                float h = saturate(0.5 + 0.5 * (b - a) / k);
                return lerp(b, a, h) - k * h * (1.0 - h);
            }

            #define GOO_DISTANCE(i)                                                               \
                float sd##i = 1e9;                                                               \
                if (_SdfInfo##i.z > 0.5)                                                         \
                {                                                                                \
                    sd##i = SampleDistance(_Sdf##i, px, _Rect##i, _SdfInfo##i);                   \
                    d = first > 0.5 ? sd##i : Smin(d, sd##i, max(_BlendK, 0.001));                \
                    first = 0.0;                                                                 \
                }

            // Remap the original outline to the merged outline, including pixels inside each source.
            #define GOO_EVAL(i)                                                                   \
            {                                                                                     \
                if (_SdfInfo##i.z > 0.5)                                                          \
                {                                                                                 \
                    float2 q = (px - _Rect##i.xy) / _Rect##i.zw;                                   \
                    float texelSize = min(2.0 * abs(_Rect##i.z) / _SdfInfo##i.y,                   \
                                          2.0 * abs(_Rect##i.w) / _SdfInfo##i.w);                  \
                    float sd = sd##i;                                                             \
                                                                                                  \
                    float2 qArt = q;                                                              \
                    float shift = max(sd - min(d, -0.5 * texelSize), 0.0);                        \
                    if (shift > 0.001)                                                           \
                    {                                                                             \
                        float e = max(texelSize * 0.5, 0.25);                                    \
                        float2 g = float2(                                                        \
                            SampleDistance(_Sdf##i, px + float2(e, 0), _Rect##i, _SdfInfo##i) -    \
                            SampleDistance(_Sdf##i, px - float2(e, 0), _Rect##i, _SdfInfo##i),      \
                            SampleDistance(_Sdf##i, px + float2(0, e), _Rect##i, _SdfInfo##i) -    \
                            SampleDistance(_Sdf##i, px - float2(0, e), _Rect##i, _SdfInfo##i))      \
                            / (2.0 * e);                                                         \
                        float2 proj = px - g * (shift / max(dot(g, g), 1e-4));                    \
                        qArt = clamp((proj - _Rect##i.xy) / _Rect##i.zw, -1.0, 1.0);               \
                    }                                                                             \
                                                                                                  \
                    float2 artUv = _ArtUv##i.xy + qArt * _ArtUv##i.zw;                             \
                    fixed4 art = tex2D(_Art##i, artUv) * _ArtTint##i;                              \
                                                                                                  \
                    if (first > 0.5)                                                             \
                    {                                                                             \
                        colourDistance = sd;                                                     \
                        rgb = art.rgb;                                                            \
                        opacity = _ArtTint##i.a;                                                  \
                    }                                                                             \
                    else                                                                          \
                    {                                                                             \
                        float k = max(_BlendK, 0.001);                                            \
                        float h = saturate(0.5 + 0.5 * (sd - colourDistance) / k);                \
                        colourDistance = lerp(sd, colourDistance, h) - k * h * (1.0 - h);         \
                        rgb = lerp(art.rgb, rgb, h);                                              \
                        opacity = lerp(_ArtTint##i.a, opacity, h);                                \
                    }                                                                             \
                    first = 0.0;                                                                  \
                }                                                                                 \
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float2 displayPosition = (IN.texcoord - _DisplayUvRect.xy) / _DisplayUvRect.zw;
                float2 px = _DisplayScreenOrigin.xy + displayPosition.x * _DisplayScreenAxes.xy
                            + displayPosition.y * _DisplayScreenAxes.zw;

                float d = 1e9;
                float first = 1.0;
                float3 rgb = 0;
                float opacity = 0;

                GOO_DISTANCE(0)
                GOO_DISTANCE(1)
                GOO_DISTANCE(2)
                GOO_DISTANCE(3)

                float aa = max(_Softness, 0.5 * fwidth(d));
                float mask = 1.0 - smoothstep(-aa, aa, d);
                if (mask <= 0.001)
                    return fixed4(0, 0, 0, 0);

                first = 1.0;
                float colourDistance = 1e9;
                GOO_EVAL(0)
                GOO_EVAL(1)
                GOO_EVAL(2)
                GOO_EVAL(3)

                rgb *= IN.color.rgb;

                // The smooth SDF owns coverage, including newly created neck pixels.
                fixed4 col = fixed4(rgb, mask * opacity * IN.color.a);

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif

                return col;
            }
            ENDCG
        }
    }
}
