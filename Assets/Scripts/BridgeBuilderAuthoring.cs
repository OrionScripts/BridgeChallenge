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

    private BridgeBuilder.BuiltBridge activeAuthoredBridge;
    private int authoredBridgeNameIndex;

    public void BeginPlacement()
    {
        IsPlacing = true;
        // New placement uses the preview hierarchy while already authored bridges remain visible under the builder content root.
        builder.ClearPreview();
    }

    public void EndPlacement()
    {
        IsPlacing = false;
        builder.ClearPreview();
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

    public void PreviewCurrentBridgeEdit()
    {
        if (!IsValid())
            return;

        EnsureActiveAuthoredBridge();

        if (activeAuthoredBridge != null)
        {
            builder.UpdateAuthoredBridge(activeAuthoredBridge, startPoint.position, endPoint.position, isPreview: true);
            return;
        }

        // Fallback keeps the original single-bridge edit flow usable for older scenes or after domain reloads where the transient active bridge record is gone.
        builder.BuildEditPreview(startPoint.position, endPoint.position);
    }

    public void CommitBridge()
    {
        if (!IsValid())
            return;

        if (IsPlacing)
        {
            builder.ClearPreview();
            authoredBridgeNameIndex = Mathf.Max(authoredBridgeNameIndex, builder.AuthoredBridgeCount);
            activeAuthoredBridge = builder.CreateAuthoredBridge(
                startPoint.position,
                endPoint.position,
                $"Editor Bridge {++authoredBridgeNameIndex}");
            RecenterAuthoringTransform();
            // Stay in placement mode so designers can author several bridges without reselecting the tool and pressing the inspector button again.
            IsPlacing = true;
            return;
        }

        IsPlacing = false;
        EnsureActiveAuthoredBridge();
        // Handle edits finalize the currently active authored bridge. If no active bridge record exists, fall back to the legacy single-bridge rebuild path.
        if (activeAuthoredBridge != null)
            builder.UpdateAuthoredBridge(activeAuthoredBridge, startPoint.position, endPoint.position, isPreview: false);
        else
            builder.Build(startPoint.position, endPoint.position);

        RecenterAuthoringTransform();
    }

    [ContextMenu("Rebuild Bridge")]
    public void RebuildBridge()
    {
        if (!IsValid())
            return;

        EnsureActiveAuthoredBridge();

        if (activeAuthoredBridge != null)
            builder.UpdateAuthoredBridge(activeAuthoredBridge, startPoint.position, endPoint.position, isPreview: false);
        else
            builder.Build(startPoint.position, endPoint.position);

        RecenterAuthoringTransform();
    }

    [ContextMenu("Clear Editor Bridges")]
    public void ClearBridge()
    {
        if (builder == null)
            return;

        IsPlacing = false;
        activeAuthoredBridge = null;
        authoredBridgeNameIndex = 0;
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

    private void EnsureActiveAuthoredBridge()
    {
        if (activeAuthoredBridge != null || builder == null)
            return;

        // BuiltBridge records are runtime/editor-session data, while the authored bridge hierarchy is serialized in the scene.
        // Recreate a lightweight record from the latest authored bridge after script/domain reloads.
        activeAuthoredBridge = builder.GetLastAuthoredBridge();
    }
}
