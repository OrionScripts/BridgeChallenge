using UnityEditor;
using UnityEditorInternal;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(BridgeBuilderAuthoring), true)]
public class BridgeBuilderAuthoringEditor : Editor
{
    // Cached GUIContent avoids per-repaint allocations in the inspector.
    private static readonly GUIContent BeginDragPlacementButton = new(
        "Begin Drag Placement",
        "Click and drag to place start and end points.");
    private static readonly GUIContent ExitDragPlacementButton = new(
        "Exit Drag Placement",
        "Return to editing the latest authored bridge endpoint handles.");

    // Tracks the "place a brand-new bridge" click-drag flow from the inspector button.
    private bool isDraggingNewBridge;
    // Tracks edits made through the scene handles on an existing bridge.
    private bool isDraggingExistingPoint;
    // PositionHandle consumes mouse events internally, so we guard against scheduling the same commit multiple times while waiting for Unity to release the hot control.
    private bool commitScheduledForExistingPoint;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        BridgeBuilderAuthoring tool = (BridgeBuilderAuthoring)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Bridge Authoring", EditorStyles.boldLabel);

        GUIContent placementButton = tool.IsPlacing ? ExitDragPlacementButton : BeginDragPlacementButton;

        if (GUILayout.Button(placementButton))
        {
            Undo.RecordObject(tool, tool.IsPlacing ? "Exit Bridge Placement" : "Begin Bridge Placement");

            if (tool.IsPlacing)
                tool.EndPlacement();
            else
                tool.BeginPlacement();

            ForceSceneRefresh();
        }

        if (GUILayout.Button("Rebuild Bridge"))
        {
            Undo.RecordObject(tool, "Rebuild Bridge");
            tool.RebuildBridge();
            ForceSceneRefresh();
        }

        if (GUILayout.Button("Clear Editor Bridges"))
        {
            Undo.RecordObject(tool, "Clear Editor Bridges");
            tool.ClearBridge();
            ForceSceneRefresh();
        }
    }

    private void OnSceneGUI()
    {
        BridgeBuilderAuthoring tool = (BridgeBuilderAuthoring)target;
        Event e = Event.current;

        if (!tool.IsPlacing)
        {
            // Existing endpoints remain editable when we are not in "begin placement" mode.
            DrawEndpointHandles(tool);
            return;
        }

        DrawPlacementGuide(tool);

        // While placing a new bridge we take ownership of scene clicks so regular object selection does not fight the tool interaction.
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if (e.button != 0)
            return;

        if (e.type == EventType.MouseDown)
        {
            if (TryGetMouseGroundPoint(e.mousePosition, out Vector3 point))
            {
                Undo.RecordObject(tool.StartPoint, "Place Bridge Start");
                Undo.RecordObject(tool.EndPoint, "Place Bridge End");

                // First click establishes both endpoints at the same position so the user immediately gets a valid drag origin and preview state.
                tool.SetStart(point);
                tool.SetEnd(point);
                tool.PreviewBridge();

                isDraggingNewBridge = true;
                ForceSceneRefresh();
                e.Use();
            }
        }

        if (e.type == EventType.MouseDrag && isDraggingNewBridge)
        {
            if (TryGetMouseGroundPoint(e.mousePosition, out Vector3 point))
            {
                Undo.RecordObject(tool.EndPoint, "Drag Bridge End");

                tool.SetEnd(point);
                tool.PreviewBridge();

                ForceSceneRefresh();
                e.Use();
            }
        }

        if (e.type == EventType.MouseUp && isDraggingNewBridge)
        {
            if (TryGetMouseGroundPoint(e.mousePosition, out Vector3 point))
            {
                Undo.RecordObject(tool.EndPoint, "Commit Bridge End");

                tool.SetEnd(point);
                isDraggingNewBridge = false;
                // Delay the final commit until the current OnSceneGUI cycle has finished.
                // This avoids stale scene-view rendering while Unity is still processing the drag.
                ScheduleCommit(tool);
                e.Use();
            }
        }
    }

    private void DrawPlacementGuide(BridgeBuilderAuthoring tool)
    {
        if (!isDraggingNewBridge || tool.StartPoint == null || tool.EndPoint == null)
            return;

        // During a new drag placement, the cyan preview mesh is the editable thing.
        // Drawing the persistent endpoint handles at the same time makes them look disconnected from the preview, because they belong to the previous bridge.
        Handles.color = Color.yellow;
        Handles.DrawLine(tool.StartPoint.position, tool.EndPoint.position);
        Handles.SphereHandleCap(
            0,
            tool.StartPoint.position,
            Quaternion.identity,
            HandleUtility.GetHandleSize(tool.StartPoint.position) * 0.12f,
            EventType.Repaint);
        Handles.SphereHandleCap(
            0,
            tool.EndPoint.position,
            Quaternion.identity,
            HandleUtility.GetHandleSize(tool.EndPoint.position) * 0.12f,
            EventType.Repaint);
    }

    private void DrawEndpointHandles(BridgeBuilderAuthoring tool)
    {
        if (tool.StartPoint == null || tool.EndPoint == null)
            return;

        EditorGUI.BeginChangeCheck();

        Handles.color = Color.green;
        Vector3 newStart = Handles.PositionHandle(tool.StartPoint.position, Quaternion.identity);

        Handles.color = Color.cyan;
        Vector3 newEnd = Handles.PositionHandle(tool.EndPoint.position, Quaternion.identity);

        Handles.color = Color.yellow;
        Handles.DrawLine(tool.StartPoint.position, tool.EndPoint.position);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(tool.StartPoint, "Move Bridge Start");
            Undo.RecordObject(tool.EndPoint, "Move Bridge End");

            tool.SetStart(newStart);
            tool.SetEnd(newEnd);

            // Existing bridge edits preview against the active authored bridge so placing multiple editor bridges does not collapse back to one bridge.
            tool.PreviewCurrentBridgeEdit();

            isDraggingExistingPoint = true;
            commitScheduledForExistingPoint = false;
            ForceSceneRefresh();
        }

        // PositionHandle can swallow MouseUp, so hotControl returning to 0 is the reliable signal that the user has actually released the gizmo.
        if (isDraggingExistingPoint && !commitScheduledForExistingPoint && GUIUtility.hotControl == 0)
        {
            isDraggingExistingPoint = false;
            commitScheduledForExistingPoint = true;
            ScheduleCommit(tool);
        }
    }

    private static void ScheduleCommit(BridgeBuilderAuthoring tool)
    {
        if (tool == null)
            return;

        EditorApplication.delayCall += () =>
        {
            if (tool == null)
                return;

            // Commit via the runtime-facing authoring API so all finalization behavior lives in one place, including recentering of the authoring object.
            tool.CommitBridge();
            EditorUtility.SetDirty(tool);
            EditorUtility.SetDirty(tool.gameObject);
            EditorSceneManager.MarkSceneDirty(tool.gameObject.scene);
            ForceSceneRefresh(deferred: true);
        };
    }

    private static void ForceSceneRefresh(bool deferred = false)
    {
        // QueuePlayerLoopUpdate helps the scene redraw reflect hierarchy/material changes that happened during editor tooling rather than runtime simulation.
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
        InternalEditorUtility.RepaintAllViews();

        if (!deferred)
            return;

        // A second pass on the next editor tick smooths over cases where Unity is still unwinding a handle interaction when the first repaint request is made.
        EditorApplication.delayCall += DeferredRepaintAllViews;
    }

    private static void DeferredRepaintAllViews()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
        InternalEditorUtility.RepaintAllViews();
    }

    private bool TryGetMouseGroundPoint(Vector2 mousePosition, out Vector3 point)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
        // The current authoring tool assumes bridges are placed on a flat XZ plane at y = 0.
        // If the tool later evolves to support arbitrary terrain, this is the seam to replace.
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

        if (groundPlane.Raycast(ray, out float enter))
        {
            point = ray.GetPoint(enter);
            return true;
        }

        point = default;
        return false;
    }
}
