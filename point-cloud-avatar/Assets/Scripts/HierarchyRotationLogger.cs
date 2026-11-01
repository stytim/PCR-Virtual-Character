using UnityEngine;
using System.IO;
using System.Text;

public class HierarchyRotationLogger : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("If true, the log will be generated automatically when the game starts.")]
    public bool runOnStart = true;

    [Tooltip("If true, rotation will be logged in Euler Angles (0-360). If false, Quaternions.")]
    public bool useEulerAngles = true;

    private void Start()
    {
        if (runOnStart)
        {
            ExportHierarchy();
        }
    }

    // Adds a button to the component's context menu in the Inspector
    [ContextMenu("Export Hierarchy to File")]
    public void ExportHierarchy()
    {
        // 1. Initialize StringBuilder
        StringBuilder sb = new StringBuilder();
        
        sb.AppendLine($"Hierarchy Export for: {gameObject.name}");
        sb.AppendLine($"Time: {System.DateTime.Now}");
        sb.AppendLine("----------------------------------------------------");

        // 2. Start the recursive analysis
        AnalyzeNode(transform, sb, 0);

        // 3. Save to file
        SaveToFile(sb.ToString());
    }

    private void AnalyzeNode(Transform currentTransform, StringBuilder sb, int depth)
    {
        // Create indentation based on depth (2 spaces per level)
        string indent = new string('-', depth * 2);
        
        // Get rotation string based on settings
        string rotationString;
        if (useEulerAngles)
        {
            Vector3 rot = currentTransform.localEulerAngles;
            rotationString = $"Rot(x:{rot.x:F2}, y:{rot.y:F2}, z:{rot.z:F2})";
        }
        else
        {
            Quaternion rot = currentTransform.localRotation;
            rotationString = $"Rot(x:{rot.x:F2}, y:{rot.y:F2}, z:{rot.z:F2}, w:{rot.w:F2})";
        }

        // Append line: Indent + Name + Rotation
        sb.AppendLine($"{indent} [{currentTransform.name}] - {rotationString}");

        // Recursive step: Loop through all children
        foreach (Transform child in currentTransform)
        {
            AnalyzeNode(child, sb, depth + 1);
        }
    }

    private void SaveToFile(string content)
    {
        // Clean the filename to remove illegal characters
        string cleanName = CleanFileName(gameObject.name);
        string fileName = $"{cleanName}_Hierarchy.txt";
        
        // Path: Assets/HierarchyLogs/ (Creates folder if it doesn't exist)
        string folderPath = Path.Combine(Application.dataPath, "HierarchyLogs");
        
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        string fullPath = Path.Combine(folderPath, fileName);

        try
        {
            File.WriteAllText(fullPath, content);
            Debug.Log($"<color=green>Success!</color> Hierarchy saved to: <b>{fullPath}</b>");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to save hierarchy: {e.Message}");
        }
    }

    // Helper to ensure the object name is a valid file name
    private string CleanFileName(string fileName)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(c, '_');
        }
        return fileName;
    }
}