Shader "Minecraft/Transparent Blocks" {

    Properties {
        _MainTex ("Block Texture Atlas", 2D) = "white" {}
    }

    SubShader {

        Tags {
            "Queue" = "AlphaTest"
            "IgnoreProjector" = "True"
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
        }

        LOD 100

        Pass {

            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Lighting Off

            HLSLPROGRAM

            #pragma vertex vertFunction
            #pragma fragment fragFunction

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
            CBUFFER_END

            // Global light uniforms — outside CBUFFER.
            float GlobalLightLevel;
            float minGlobalLightLevel;
            float maxGlobalLightLevel;

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct appdata {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct v2f {
                float4 vertex : SV_POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            v2f vertFunction(appdata v) {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.uv     = TRANSFORM_TEX(v.uv, _MainTex);
                o.color  = v.color;
                return o;
            }

            half4 fragFunction(v2f i) : SV_Target {

                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                // Discard transparent pixels (cutout alpha test).
                clip(col.a - 0.5);

                float worldLight = (maxGlobalLightLevel - minGlobalLightLevel)
                                   * GlobalLightLevel + minGlobalLightLevel;

                float brightness = clamp(i.color.a * worldLight,
                                         minGlobalLightLevel, maxGlobalLightLevel);

                col.rgb *= brightness;
                return col;
            }

            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
