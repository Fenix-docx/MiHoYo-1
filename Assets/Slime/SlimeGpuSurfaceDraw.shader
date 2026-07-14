Shader "Slime/GPU Surface Draw"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.72, 0.95, 1.0, 0.65)
        _Opacity ("Opacity", Range(0.02, 1.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "GpuSurface"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct SurfaceVertexGpu
            {
                float3 Position;
                float3 Normal;
            };

            StructuredBuffer<SurfaceVertexGpu> _SurfaceVertices;

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Opacity;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                SurfaceVertexGpu vertex = _SurfaceVertices[vertexID];

                Varyings output;
                output.positionWS = vertex.Position;
                output.normalWS = normalize(vertex.Normal);
                output.positionCS = TransformWorldToHClip(vertex.Position);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewWS = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                half fresnel = pow(saturate(1.0 - dot(normalWS, viewWS)), 3.0);
                half3 lit = _BaseColor.rgb * (0.45h + 0.55h * fresnel);
                return half4(lit, saturate(_BaseColor.a * _Opacity));
            }
            ENDHLSL
        }
    }
}
