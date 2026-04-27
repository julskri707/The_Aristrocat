Shader "Custom/URP/FieldFurrowFade"
{
    Properties
    {
        _BaseMap("Base Map (Acker / Furche)", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)

        _GroundMap("Ground Map (Gras / Boden)", 2D) = "white" {}
        _GroundColor("Ground Color", Color) = (1,1,1,1)

        _EdgeTint("Edge Tint (unused)", Color) = (0.43, 0.29, 0.18, 1)
        _EdgeFadeWidth("Edge Fade Width", Float) = 0.55

        _Smoothness("Smoothness", Range(0,1)) = 0.15
        _Metallic("Metallic", Range(0,1)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Geometry"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv0        : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv0        : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half4  color      : COLOR;
                float  fogFactor  : TEXCOORD2;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_GroundMap);
            SAMPLER(sampler_GroundMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _GroundMap_ST;
                float4 _BaseColor;
                float4 _GroundColor;
                float4 _EdgeTint;
                float  _EdgeFadeWidth;
                float  _Smoothness;
                float  _Metallic;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs nInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = posInputs.positionCS;
                OUT.normalWS = normalize(nInputs.normalWS);
                OUT.uv0 = IN.uv0;

                // Ohne Vertex-Color-Kanal liefert Unity oft (0,0,0,0) → Feld unsichtbar auf Gras
                half4 vc = IN.color;
                if (dot(vc, half4(1,1,1,1)) < 0.001h)
                    vc = half4(1, 1, 1, 1);
                OUT.color = vc;

                OUT.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uvSoil = TRANSFORM_TEX(IN.uv0, _BaseMap);
                float2 uvGround = TRANSFORM_TEX(IN.uv0, _GroundMap);

                half4 texSoil = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvSoil) * _BaseColor;
                half4 texGround = SAMPLE_TEXTURE2D(_GroundMap, sampler_GroundMap, uvGround) * _GroundColor;

                half edgeField = saturate(IN.color.r);
                half ridgeMask = saturate(IN.color.g);
                half grooveMixOn = IN.color.b > 0.5h;
                half soilWeight = edgeField * (grooveMixOn ? ridgeMask : 1.h);

                half3 albedo = lerp(texGround.rgb, texSoil.rgb, soilWeight);

                Light mainLight = GetMainLight();
                half3 N = normalize(IN.normalWS);
                half NdotL = saturate(dot(N, mainLight.direction));
                half3 ambient = SampleSH(N);
                half3 diffuse = mainLight.color * (0.2h + 0.8h * NdotL);
                half3 lit = albedo * (ambient + diffuse);

                lit = MixFog(lit, IN.fogFactor);
                return half4(lit, 1);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
