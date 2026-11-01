using UnityEngine;

public class EditorStartHeight : MonoBehaviour
{
    public float editorHeight = 1.6f;

    void Start()
    {
#if UNITY_EDITOR
        var p = transform.position;
        transform.position = new Vector3(p.x, editorHeight, p.z);
#endif
    }
}