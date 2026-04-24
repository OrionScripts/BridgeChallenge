using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public enum BridgeSegmentType
{
    Start,
    Middle,
    Filler,
    End
}

[DisallowMultipleComponent]
public class BridgeSegment : MonoBehaviour
{
    // SegmentType lets BridgeBuilder determine whether an existing slot can be reused when the desired bridge layout changes.
    [SerializeField] private BridgeSegmentType segmentType;
    // Authored world-space length of this segment along the local +X axis.
    [SerializeField, Min(0f)] private float length;
    [SerializeField] private bool includeInactiveChildren = false;

    public BridgeSegmentType SegmentType => segmentType;
    public float Length => length;

    [ContextMenu("Measure And Apply Length")]
    private void MeasureAndApplyLength()
    {
        // Using renderer bounds makes the workflow simple for artists: update the mesh, then run this command to refresh the segment's authored length. Useful for related math calculations.
        Renderer[] renderers = GetComponentsInChildren<Renderer>(includeInactiveChildren);

        if (renderers == null || renderers.Length == 0)
        {
            Debug.LogWarning($"{name}: No Renderer found to measure.", this);
            return;
        }

        Bounds combinedBounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
        {
            combinedBounds.Encapsulate(renderers[i].bounds);
        }

        // Bridge kit is authored to extend along +X.
        length = combinedBounds.size.x;

#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
#endif
    }
}
