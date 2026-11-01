Shader "Custom/DepthNoise"
{
    Properties
    {
        [Header(Geometric Distortion)]
        _GeometricDistortion("Distortion Amount", Range(0, 5)) = 1.0

        [Header(Structured Light Pattern)]
        _PatternTex ("Dot Pattern", 2D) = "white" {}
        _ApplyPattern ("Apply Pattern Mask", Range(0, 1)) = 1.0

        [Header(Other Noise Types)]
        _MainTex ("Texture", 2D) = "white" {}
        _AxialNoiseScale ("Axial Noise Scale (z^2)", Range(0, 1)) = 0.1
        _HoleNoiseScale ("Hole Noise Frequency", Range(1, 200)) = 50.0
        _HoleThreshold ("Hole Threshold", Range(0, 1)) = 0.95
        _QuantizationLevels ("Quantization Levels", Range(16, 1024)) = 256
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            sampler2D _MainTex;
            sampler2D _PatternTex; // New sampler for the dot pattern
            float _ApplyPattern; // New property to control the effect
            float _GeometricDistortion;
            float _AxialNoiseScale;
            float _HoleNoiseScale;
            float _HoleThreshold;
            float _QuantizationLevels;

            // --- UTILITY FUNCTIONS ---

            // Simple pseudo-random hash function (uniform distribution)
            float rand(float2 co)
            {
                return frac(sin(dot(co.xy, float2(12.9898, 78.233))) * 43758.5453);
            }

            // Box-Muller transform to get a normally distributed random number (for Gaussian shift)
            float2 rand_normal(float2 uv) {
                float u1 = rand(uv);
                float u2 = rand(uv + float2(2.718, 3.141)); // Use different seeds
                
                // Prevent log(0) which results in infinity
                u1 = max(1e-6, u1);

                float r = sqrt(-2.0 * log(u1));
                float angle = 2.0 * UNITY_PI * u2;
                
                return float2(r * cos(angle), r * sin(angle));
            }


            fixed4 frag (v2f i) : SV_Target
            {
                // --- 1. APPLY GAUSSIAN SHIFT (GEOMETRIC DISTORTION) ---
                float2 randomOffset = rand_normal(i.uv + _Time.y) * _GeometricDistortion / _ScreenParams.xy;
                float2 distortedUV = i.uv + randomOffset;

                float z = tex2D(_MainTex, distortedUV).r;

                if (z >= 1.0)
                {
                    return fixed4(1,1,1,1);
                }

                // --- 2. APPLY OTHER NOISE TYPES ---
                float randomVal = rand(i.uv + _Time.y);
                float noiseStrength = _AxialNoiseScale * z * z;
                z += (randomVal - 0.5) * noiseStrength;

                z = floor(z * _QuantizationLevels) / _QuantizationLevels;

                float holeRandomVal = rand(i.uv * _HoleNoiseScale); 
                if (holeRandomVal > _HoleThreshold)
                {
                    z = 1.0;
                }

               // --- 3. APPLY STRUCTURED LIGHT PATTERN ---
                // Sample the dot pattern texture
                float pattern = tex2D(_PatternTex, i.uv).r;

                // Check if the current pixel is part of the background (where the pattern is black)
                if (pattern < 0.5)
                {
                    // If it is a background pixel, use a random value to decide whether to discard it.
                    // The probability of discarding is controlled by the _ApplyPattern slider.
                    if (rand(i.uv * 2.0) < _ApplyPattern) // Use a different seed for this rand
                    {
                        z = 1.0; // Discard the pixel by setting depth to invalid
                    }
                }

                // Clamp the final value to ensure it's in the 0-1 range
                z = saturate(z);

                return fixed4(z, z, z, 1.0);
            }
            ENDCG
        }
    }
}