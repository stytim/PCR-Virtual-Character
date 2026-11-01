using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class PointCloudGPU : MonoBehaviour
{
    // C# struct to match the layout of the Point struct in the compute shader
    struct Point
    {
        public Vector3 position;
        public Color color;
    }

    [Header("Dependencies")]
    public ComputeShader computeShader;
    public Material pointMaterial;
    public Material meshMaterial;
    public DepthCamera depthCamera;

    [Header("Point Cloud Container")]
    public Transform pointCloudContainer;
    [Range(0.1f, 50f)] public float maxDepthDistance = 10f; // New slider to filter background/shadows
    [Range(0.1f, 20f)] public float pointSize = 5.0f; // New slider for point size
    [Range(0.5f, 3f)] public float brightness = 1.0f;
    [Range(0f, 1f)] public float opacity = 1.0f;
    public bool freezeCapture = false;

    [Header("Visualization Toggles")]
    public bool showPointCloud = true;
    public bool showMesh = false;

    // --- GPU Resources ---
    private ComputeBuffer pointsBuffer;
    private int pointKernelHandle;
    private int pointBufferSize;
    private Camera cam;
    
    // --- CPU Mesh Resources ---
    private Mesh generatedMesh;
    private Point[] pointCloudData; // Array to receive the full Point data from the GPU
    private Vector3[] meshVertices;   // Array for just the vertex positions
    private Color[] meshColors;       // Array for just the vertex colors
    private Vector2[] meshUVs;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;

    private MaterialPropertyBlock props;

    void Start()
    {
        if (depthCamera == null || computeShader == null || pointMaterial == null || meshMaterial == null || pointCloudContainer == null)
        {
            Debug.LogError("Dependencies not set!");
            this.enabled = false;
            return;
        }

        cam = depthCamera.GetComponent<Camera>();
        
        // --- GPU Point Cloud Setup ---
        pointBufferSize = depthCamera.textureWidth * depthCamera.textureHeight;
        // The stride is 28 bytes to match the Point struct (Vector3 + Color)
        pointsBuffer = new ComputeBuffer(pointBufferSize, 28, ComputeBufferType.Default); 
        pointKernelHandle = computeShader.FindKernel("CSMain"); 

        // We do NOT set the buffer on the material globally anymore
        // pointMaterial.SetBuffer("_PointsBuffer", pointsBuffer); 
        
        props = new MaterialPropertyBlock();

        // --- CPU Mesh Setup ---
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.material = meshMaterial;

        generatedMesh = new Mesh
        {
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
        };
        meshFilter.mesh = generatedMesh;

        // Initialize all the arrays
        pointCloudData = new Point[pointBufferSize];
        meshVertices = new Vector3[pointBufferSize];
        meshColors = new Color[pointBufferSize];

        meshUVs = new Vector2[pointBufferSize];
        for (int y = 0; y < depthCamera.textureHeight; y++)
        {
            for (int x = 0; x < depthCamera.textureWidth; x++)
            {
                meshUVs[x + y * depthCamera.textureWidth] = new Vector2(x / (float)depthCamera.textureWidth, y / (float)depthCamera.textureHeight);
            }
        }
    }

    [HideInInspector]
    public bool managedByManager = false; // Set by MultiCameraManager to disable standalone updates

    void Update()
    {
        meshRenderer.enabled = showMesh;

        // If managed by MultiCameraManager, skip automatic updates
        // The manager will call ForceRender() and ForceRenderVisuals() explicitly
        if (managedByManager)
        {
            return;
        }

        // Standalone mode: run everything every frame
        if (!freezeCapture)
        {
            RunComputeShader();
        }

        if (showPointCloud)
        {
            RenderPointCloud();
        }
        
        if (showMesh)
        {
            UpdateMesh();
        }
    }

    /// <summary>
    /// Manually triggers the compute shader for this frame.
    /// Called by MultiCameraManager for time-slicing.
    /// </summary>
    public void ForceRender()
    {
        RunComputeShader();
    }

    // Interleave fields (default to full render)
    private int currentInterleaveStep = 1;
    private int currentInterleaveOffset = 0;

    /// <summary>
    /// Manually triggers the visual rendering (point cloud and/or mesh).
    /// Called by MultiCameraManager to control which cameras render each frame.
    /// Supports interleaved rendering (1=full, 2=half points, 4=quarter).
    /// </summary>
    public void ForceRenderVisuals(int interleaveStep = 1, int interleaveOffset = 0)
    {
        currentInterleaveStep = interleaveStep;
        currentInterleaveOffset = interleaveOffset;

        if (showPointCloud)
        {
            RenderPointCloud();
        }
        
        if (showMesh)
        {
            UpdateMesh();
        }
    }

    void RunComputeShader()
    {
        if (pointsBuffer == null) return;
        
        RenderTexture colorTex = depthCamera.colorTexture;
        RenderTexture depthTex = depthCamera.noisyDepthTexture;
        if (colorTex == null || depthTex == null) return;

        // computeShader.SetMatrix("_InverseProjectionMatrix", cam.projectionMatrix.inverse); // Removed
        
        computeShader.SetFloat("_FarPlane", cam.farClipPlane);
        computeShader.SetFloat("_P00", cam.projectionMatrix[0, 0]);
        computeShader.SetFloat("_P11", cam.projectionMatrix[1, 1]);
        computeShader.SetFloat("_MaxDepth", maxDepthDistance); // Pass the cutoff distance

        computeShader.SetTexture(pointKernelHandle, "_ColorTexture", colorTex);
        computeShader.SetTexture(pointKernelHandle, "_DepthTexture", depthTex);
        computeShader.SetBuffer(pointKernelHandle, "_PointsBuffer", pointsBuffer);
        computeShader.SetInt("_TextureWidth", depthCamera.textureWidth);
        int threadGroups = Mathf.CeilToInt(depthCamera.textureWidth / 8.0f);
        computeShader.Dispatch(pointKernelHandle, threadGroups, threadGroups, 1);
    }
    
    void UpdateMesh()
    {
        if (pointsBuffer == null) return;

        // 1. Get the FULL Point struct data from the GPU. This now works because the strides match.
        pointsBuffer.GetData(pointCloudData);

        // 2. Separate the full data into positions and colors
        for(int i = 0; i < pointBufferSize; i++)
        {
            meshVertices[i] = pointCloudData[i].position;
            meshColors[i] = pointCloudData[i].color;
        }

        // 3. Build the triangles list on the CPU
        List<int> triangles = new List<int>();
        float validPointThreshold = 0.0001f;
        
        int width = depthCamera.textureWidth;
        int height = depthCamera.textureHeight;

        for (int y = 0; y < height - 1; y++)
        {
            for (int x = 0; x < width - 1; x++)
            {
                int i00 = (y * width) + x;
                int i10 = (y * width) + (x + 1);
                int i01 = ((y + 1) * width) + x;
                int i11 = ((y + 1) * width) + (x + 1);

                bool v00 = meshVertices[i00].sqrMagnitude > validPointThreshold;
                bool v10 = meshVertices[i10].sqrMagnitude > validPointThreshold;
                bool v01 = meshVertices[i01].sqrMagnitude > validPointThreshold;
                bool v11 = meshVertices[i11].sqrMagnitude > validPointThreshold;

                if (v00 && v01 && v10) { triangles.Add(i00); triangles.Add(i01); triangles.Add(i10); }
                if (v10 && v01 && v11) { triangles.Add(i10); triangles.Add(i01); triangles.Add(i11); }
            }
        }

        // 4. Apply all the data to the mesh
        generatedMesh.Clear();
        generatedMesh.vertices = meshVertices;
        generatedMesh.colors = meshColors; // Assign the vertex colors
        generatedMesh.uv = meshUVs;
        generatedMesh.SetTriangles(triangles, 0);
        generatedMesh.RecalculateNormals();
        generatedMesh.RecalculateBounds();
    }

    void RenderPointCloud()
    {
        if (props == null || pointsBuffer == null) return;

        // Use MaterialPropertyBlock to pass unique data per instance
        props.SetBuffer("_PointsBuffer", pointsBuffer);
        props.SetMatrix("_ModelMatrix", depthCamera.transform.localToWorldMatrix);
        props.SetFloat("_PointSize", pointSize);
        props.SetFloat("_Brightness", brightness);
        props.SetFloat("_Opacity", opacity);
        // Pass interleave parameters
        props.SetInt("_InterleaveStep", currentInterleaveStep);
        props.SetInt("_InterleaveOffset", currentInterleaveOffset);

        var bounds = new Bounds(pointCloudContainer.position, Vector3.one * 1000);
        
        // Calculate partial vertex count based on interleave step
        // Each point has 6 vertices. Since we interleave points, we divide total points by step.
        // Use Ceiling to ensure we cover any remainder
        int totalPoints = pointBufferSize;
        int pointsToDraw = Mathf.CeilToInt((float)totalPoints / currentInterleaveStep);
        
        // We draw 6 vertices per point (2 triangles per point billboard)
        Graphics.DrawProcedural(pointMaterial, bounds, MeshTopology.Triangles, pointsToDraw * 6, 1, null, props, UnityEngine.Rendering.ShadowCastingMode.Off, true);
    }

    void OnDestroy()
    {
        if (pointsBuffer != null) pointsBuffer.Release();
        if (generatedMesh != null) Destroy(generatedMesh);
    }
}