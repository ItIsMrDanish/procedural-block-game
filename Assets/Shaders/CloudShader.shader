Shader "Minecraft/CloudShader" {

    Properties {

        _Color ("Main Color", Color) = (1,1,1,1)
    }

    SubShader {

        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass {

            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            ZWrite Off
            Lighting Off
            Blend SrcAlpha OneMinusSrcAlpha

            Stencil {

                Ref 1
                Comp Greater
                Pass IncrSat
            }

            HLSLPROGRAM
            #pragma vertex vertFunction
            #pragma fragment fragFunction
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            struct appdata {

                float4 vertex : POSITION;
            };

            struct v2f {

                float4 vertex : SV_POSITION;
            };

            v2f vertFunction(appdata v) {

                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                return o;
            }

            half4 fragFunction(v2f i) : SV_Target {
                return _Color;
            }

            ENDHLSL
        }
    }
}