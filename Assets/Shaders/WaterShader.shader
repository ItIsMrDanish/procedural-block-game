Shader "Minecraft/WaterShader" {

    Properties {

        _MainTex ("First Texture", 2D) = "white" {}
        _SecondaryTex ("Second Texture", 2D) = "white" {}
    }

    SubShader {

        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass {

            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            Lighting Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

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
            TEXTURE2D(_SecondaryTex);
            SAMPLER(sampler_SecondaryTex);

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

                // Animate UV scrolling for water movement
                float2 animUV = i.uv;
                animUV.x += _SinTime.x * 0.5;

                half4 tex1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, animUV);
                half4 tex2 = SAMPLE_TEXTURE2D(_SecondaryTex, sampler_SecondaryTex, animUV);
                half4 col = lerp(tex1, tex2, 0.5 + (_SinTime.w * 0.5));

                // Water vertex alpha is always 0 (it's a transparent mesh, not a light value).
                // Use only world light level for water brightness.
                float worldLight = (maxGlobalLightLevel - minGlobalLightLevel) * GlobalLightLevel + minGlobalLightLevel;
                float brightness = clamp(worldLight, minGlobalLightLevel, maxGlobalLightLevel);

                col.rgb *= brightness;
                col.a = 0.65;
                return col;
            }

            ENDHLSL
        }
    }
}
