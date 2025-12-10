Shader "Custom/SkeletonForeground"
{
    Properties
    {
        _Color ("Color", Color) = (0, 1, 0, 1)
        _Opacity ("Opacity", Range(0.0, 1.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent+100"
            "RenderPipeline"="UniversalPipeline"
        }
        LOD 100

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On
        ZTest LEqual
        Cull Back

        Pass
        {
            Name "SkeletonForeground"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Opacity;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color * _Color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 color = input.color;
                color.a *= _Opacity;
                return color;
            }
            ENDHLSL
        }
    }

    // Fallback for systems without URP
    Fallback "Sprites/Default"
}
