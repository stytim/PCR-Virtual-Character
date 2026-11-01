Shader "Custom/PointCloudRendererGPU"
{
    Properties
    {
        _PointSize("Point Size", Float) = 5.0
        _Brightness("Brightness", Range(0.5, 3.0)) = 1.0
        _Opacity("Opacity", Range(0.0, 1.0)) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100
        Pass
        {
            ZWrite On
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct Point
            {
                float3 position;
                float4 color;
            };

            StructuredBuffer<Point> _PointsBuffer;
            float4x4 _ModelMatrix;
            float _PointSize;
            float _Brightness;
            float _Opacity;
            int _InterleaveStep;
            int _InterleaveOffset;

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
            };

            static float2 quadOffsets[6] = {
                float2(-0.5, -0.5), // BL
                float2(-0.5,  0.5), // TL
                float2( 0.5,  0.5), // TR
                float2( 0.5,  0.5), // TR
                float2( 0.5, -0.5), // BR
                float2(-0.5, -0.5)  // BL
            };

            v2f vert (uint id : SV_VertexID)
            {
                v2f o;
                
                // Decode particle ID and vertex ID with Interleaving logic
                uint vertexIndex = id % 6;
                uint sequentialParticleIndex = id / 6;
                
                // Map the sequential index (0, 1, 2...) to the scattered index (0, 2, 4... or 1, 3, 5...)
                // _InterleaveStep = 2, Offset = 0: 0->0, 1->2...
                uint particleIndex = sequentialParticleIndex * _InterleaveStep + _InterleaveOffset;

                Point p = _PointsBuffer[particleIndex];

                // Billboard Logic in Vertex Shader
                float3 centerPos = p.position;
                float3 worldCenter = mul(_ModelMatrix, float4(centerPos, 1.0)).xyz;

                float3 up = float3(0, 1, 0);
                float3 look = _WorldSpaceCameraPos - worldCenter;
                look = normalize(look);
                float3 right = cross(up, look);
                if (length(right) < 0.001) right = float3(1, 0, 0);
                right = normalize(right);
                up = cross(look, right);

                float size = _PointSize * 0.005; // Same tuning factor
                float2 offset = quadOffsets[vertexIndex];

                float3 finalPos = worldCenter + (right * offset.x * size * 2.0) + (up * offset.y * size * 2.0); // * 2.0 because offset is 0.5
                
                o.pos = UnityObjectToClipPos(float4(finalPos, 1.0));
                o.color = p.color;
                
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                if (i.color.a < 0.1) discard;
                fixed4 c = i.color;
                c.rgb *= _Brightness;
                c.a = _Opacity;
                return c;
            }
            ENDCG
        }
    }
}