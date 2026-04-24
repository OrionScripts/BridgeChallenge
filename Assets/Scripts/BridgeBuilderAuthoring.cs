using UnityEngine;

[DisallowMultipleComponent]
public class BridgeBuilderAuthoring : MonoBehaviour
{
    // BridgeBuilder owns the actual bridge hierarchy and visual state.
    [SerializeField] private BridgeBuilder builder;
    // These transforms act as the editable endpoints exposed in the scene view.
    [SerializeField] private Transform startPoint;
    [SerializeField] private Transform endPoint;

    public BridgeBuilder Builder => builder;
    public Transform StartPoint => startPoint;
    public Transform EndPoint => endPoint;

    public bool IsPlacing { get; private set; }

    public void BeginPlacement()
    {
        IsPlacing = true;
        // New placement starts from a clean slate so the preview the user sees is always
        // derived solely from the current click-drag gesture.
        builder.ClearAll();
    }

    public void SetStart(Vector3 position)
    {
        if (startPoint != null)
            startPoint.position = position;
    }

    public void SetEnd(Vector3 position)
    {
        if (endPoint != null)
            endPoint.position = position;
    }

    public void PreviewBridge()
    {
        if (!IsValid())
            return;

        // New bridge placement uses a dedicated preview hierarchy so authors can distinguish "in progress" geometry from the finalized bridge.
        builder.BuildPreview(startPoint.position, endPoint.position);
    }

    public void CommitBridge()
    {
        if (!IsValid())
            return;

        IsPlacing = false;
        // Final commit swaps back to the content hierarchy/material set used by the actual bridge.
        builder.Build(startPoint.position, endPoint.position);
        RecenterAuthoringTransform();
    }

    [ContextMenu("Rebuild Bridge")]
    public void RebuildBridge()
    {
        if (!IsValid())
            return;

        builder.Build(startPoint.position, endPoint.position);
        RecenterAuthoringTransform();
    }

    [ContextMenu("Clear Bridge")]
    public void ClearBridge()
    {
        if (builder == null)
            return;

        IsPlacing = false;
        builder.ClearAll();
    }

    private bool IsValid()
    {
        return builder != null && startPoint != null && endPoint != null;
    }

    private void RecenterAuthoringTransform()
    {
        // Keeping the authoring root near the bridge makes the hierarchy and transform gizmo much easier to work with after repeated edits.
        if (builder.TryGetContentCenter(out Vector3 center))
        {
            transform.position = center;
            return;
        }

        // Midpoint fallback covers cases where render bounds are unavailable or the bridge could not be built for the current span.
        transform.position = Vector3.Lerp(startPoint.position, endPoint.position, 0.5f);
    }
}
