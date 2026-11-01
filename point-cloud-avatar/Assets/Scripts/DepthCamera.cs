using System;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Camera))]
public class DepthCamera : MonoBehaviour
{
    [Header("Render Settings")]
    public int resolutionWidth = 512;
    public int resolutionHeight = 512;
    [Range(1, 4)] public int downsampleFactor = 2;

    public int textureWidth => resolutionWidth / downsampleFactor;
    public int textureHeight => resolutionHeight / downsampleFactor;

    [Header("Shader and Display")]
    public Material depthMaterial;
    public Material noiseMaterial;
    public RawImage colorDisplay;
    public RawImage depthDisplay;

    public bool useNoise = true;

    public RenderTexture colorTexture { get; private set; }
    public RenderTexture cleanDepthTexture { get; private set; }
    public RenderTexture noisyDepthTexture { get; private set; }

    private Camera cam;

    void Start()
    {
        cam = GetComponent<Camera>();
        cam.depthTextureMode = DepthTextureMode.Depth;

        if (textureWidth > 0 && textureHeight > 0)
        {
            colorTexture = new RenderTexture(textureWidth, textureHeight, 24);
            cleanDepthTexture = new RenderTexture(textureWidth, textureHeight, 0, RenderTextureFormat.RFloat);
            noisyDepthTexture = new RenderTexture(textureWidth, textureHeight, 0, RenderTextureFormat.RFloat);
        }
        
        if (colorDisplay != null) colorDisplay.texture = colorTexture;
        if (depthDisplay != null) depthDisplay.texture = noisyDepthTexture;
    }

    void Update()
    {
        if (cam != null)
        {
            cam.aspect = (float)textureWidth / (float)textureHeight;
        }
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        Graphics.Blit(source, colorTexture);
        Graphics.Blit(source, cleanDepthTexture, depthMaterial);

        if (noiseMaterial != null && useNoise)
        {
            Graphics.Blit(cleanDepthTexture, noisyDepthTexture, noiseMaterial);
        }
        else
        {
            Graphics.Blit(cleanDepthTexture, noisyDepthTexture);
        }

        Graphics.Blit(source, destination);
    }
    
    void OnDestroy()
    {
        if (colorTexture != null) colorTexture.Release();
        if (cleanDepthTexture != null) cleanDepthTexture.Release();
        if (noisyDepthTexture != null) noisyDepthTexture.Release();
    }
}