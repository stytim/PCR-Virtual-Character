Shader "Custom/ProceduralMeshShader"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            // Match the struct from the compute shader
            struct Point
            {
                float3 position;
                float4 color;
            };

            // Buffers from the C# script
            StructuredBuffer<Point> _PointsBuffer;
            StructuredBuffer<int> _IndicesBuffer;

            // Model matrix for positioning the mesh in the world
            float4x4 _ModelMatrix;

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
            };

            v2f vert (uint id : SV_VertexID)
            {
                // 1. Use the vertex ID to get the real index from the index buffer.
                int vertexIndex = _IndicesBuffer[id];
                
                // 2. Use the real index to get the point data from the points buffer.
                Point p = _PointsBuffer[vertexIndex];

                v2f o;
                // Transform the vertex position from local to world to clip space
                float4 worldPos = mul(_ModelMatrix, float4(p.position, 1.0));
                o.vertex = mul(UNITY_MATRIX_VP, worldPos);
                o.color = p.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}