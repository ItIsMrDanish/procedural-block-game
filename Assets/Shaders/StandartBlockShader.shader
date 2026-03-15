Shader "Minecraft/Blocks" {

    Properties {

        _MainTex ("Block Texture Atlas", 2D) = "white" {}
    }

    SubShader {

        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass {

            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            Lighting Off

            HLSLPROGRAM
            #pragma vertex vertFunction
            #pragma fragment fragFunction
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            float GlobalLightLevel;
            float minGlobalLightLevel;
            float maxGlobalLightLevel;

            v2f vertFunction(appdata v) {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 fragFunction(v2f i) : SV_Target {
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                // i.color.a = per-voxel light (0=dark, 1=full sunlight from Lighting.cs)
                // GlobalLightLevel = day/night (0=night, 1=day) set by World.SetGlobalLightValue
                // worldLight remaps GlobalLightLevel into [min, max] range
                float worldLight = (maxGlobalLightLevel - minGlobalLightLevel) * GlobalLightLevel + minGlobalLightLevel;

                // Multiply per-voxel light by world light, clamp to valid range
                float brightness = clamp(i.color.a * worldLight, minGlobalLightLevel, maxGlobalLightLevel);

                col.rgb *= brightness;
                return col;
            }

            ENDHLSL
        }
    }
}
