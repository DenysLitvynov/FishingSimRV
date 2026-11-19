Shader "SereneLake/Video360Panoramico"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Vídeo equirectangular 360", 2D) = "black" {}
        _Rotation ("Rotación", Range(0, 360)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float _Rotation;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float sine, cosine;
                sincos(radians(_Rotation), sine, cosine);
                float3 p = input.positionOS.xyz;
                float3 rotated = float3(cosine * p.x + sine * p.z,
                    p.y, -sine * p.x + cosine * p.z);
                output.positionCS = TransformObjectToHClip(rotated);
                output.direction = p;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 direction = normalize(input.direction);
                // Latitude/longitude mapping, matching Unity's panoramic skybox.
                float2 uv = float2(0.5 - atan2(direction.z, direction.x) / TWO_PI,
                    1.0 - acos(clamp(direction.y, -1.0, 1.0)) / PI);
                return half4(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
