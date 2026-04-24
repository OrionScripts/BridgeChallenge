using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class RuntimeBridgePlacement : MonoBehaviour
{
    // Kept in sync with RuntimeIsometricCamera defaults so the placement script can repair an existing camera that already has the component attached.
    private static readonly Vector3 IsometricTargetPosition = new(0f, 0f, 6f);
    private const float IsometricDistance = 32f;
    private const float IsometricPitch = 60f;
    private const float IsometricYaw = 45f;
    private const float IsometricOrthographicSize = 18f;

    [Header("References")]
    [SerializeField] private BridgeBuilder builder;
    [SerializeField] private Camera placementCamera;

    [Header("Placement")]
    [SerializeField] private float placementPlaneY = 0f;
    [SerializeField, Min(0.01f)] private float minimumCommitDistance = 2f;

    [Header("Feedback")]
    [SerializeField] private Color guideColor = new(0.15f, 0.85f, 1f, 0.9f);
    [SerializeField] private Color invalidGuideColor = new(1f, 0.45f, 0.1f, 0.75f);
    [SerializeField, Min(0.01f)] private float guideWidth = 0.05f;

    [Header("Endpoint Handles")]
    [SerializeField] private Color startHandleColor = new(0.2f, 1f, 0.45f, 1f);
    [SerializeField] private Color endHandleColor = new(1f, 0.65f, 0.15f, 1f);
    [SerializeField] private Color selectedHandleColor = new(0.15f, 0.85f, 1f, 1f);
    [SerializeField, Min(0.1f)] private float handleRadius = 0.25f;
    [SerializeField, Min(0f)] private float handleHeight = 0.25f;
    [SerializeField, Min(8f)] private float handleScreenDiameter = 20f;

    [Header("HUD Feedback")]
    [SerializeField, Min(0.25f)] private float toastDuration = 1.35f;
    [SerializeField, Range(0f, 1f)] private float confirmSoundVolume = 0.28f;

    private Vector3 dragStart;
    private Vector3 dragEnd;
    private LineRenderer guideLine;
    private Material guideMaterial;
    private Material startHandleMaterial;
    private Material endHandleMaterial;
    private Material selectedHandleMaterial;
    // Runtime bridges are tracked outside BridgeBuilder so each bridge keeps its own editable endpoints and handle objects.
    private readonly List<RuntimeBridgeRecord> committedBridges = new();
    // Reused scratch list to avoid allocations when wrapping scene-authored bridges.
    private readonly List<BridgeBuilder.BuiltBridge> discoveredAuthoredBridges = new();
    private RuntimeBridgeRecord editingBridge;
    private InteractionMode interactionMode;
    // Stored at edit start so a cancelled drag can restore the bridge exactly.
    private Vector3 editStartBeforeDrag;
    private Vector3 editEndBeforeDrag;
    private int bridgeNameIndex;
    private bool isEditMode;
    private GUIStyle hudButtonStyle;
    private GUIStyle counterStyle;
    private GUIStyle toastStyle;
    private Texture2D toastBackgroundTexture;
    private Rect editModeButtonRect;
    private Rect clearButtonRect;
    private string toastMessage;
    private float toastUntilTime;
    private AudioSource feedbackAudioSource;
    private AudioClip bridgePlacedClip;
    private AudioClip bridgeEditedClip;

    public bool IsPlacing => interactionMode == InteractionMode.Placing;
    public bool IsEditing => interactionMode == InteractionMode.EditingStart || interactionMode == InteractionMode.EditingEnd;

    private void Awake()
    {
        if (placementCamera == null)
            placementCamera = Camera.main;

        ConfigurePlacementCamera();
        CreateGuideLine();
        CreateEndpointHandles();
        CreateFeedbackAudio();
        AdoptAuthoredBridges();
        SetGuideVisible(false);
        SetHandlesVisible(false);
    }

    private void OnDestroy()
    {
        if (guideMaterial != null)
            Destroy(guideMaterial);

        if (startHandleMaterial != null)
            Destroy(startHandleMaterial);

        if (endHandleMaterial != null)
            Destroy(endHandleMaterial);

        if (selectedHandleMaterial != null)
            Destroy(selectedHandleMaterial);

        if (bridgePlacedClip != null)
            Destroy(bridgePlacedClip);

        if (bridgeEditedClip != null)
            Destroy(bridgeEditedClip);

        if (toastBackgroundTexture != null)
            Destroy(toastBackgroundTexture);
    }

    private void Update()
    {
        if (builder == null || placementCamera == null)
            return;

        // Keyboard cancel/clear wins before pointer handling so the active drag cannot also commit in the same frame.
        if (WasClearPressed())
        {
            ClearAllRuntimeBridges();
            return;
        }

        if (WasCancelPressed())
        {
            CancelPlacement();
            return;
        }

        if (!TryGetPointerState(out PointerState pointerState))
            return;

        if (pointerState.IsCameraOrbitGesture)
            return;

        // The HUD is drawn in IMGUI, so pointer blocking has to be done manually.
        if (pointerState.WasPressedThisFrame && IsScreenPositionOverHud(pointerState.ScreenPosition))
            return;

        if (!TryGetPlanePoint(pointerState.ScreenPosition, out Vector3 planePoint))
            return;

        // Edit mode intentionally exposes all endpoint handles at once.
        // Bridge body selection is avoided because the art prefabs do not ship with colliders.
        if (isEditMode)
        {
            if (pointerState.WasPressedThisFrame && TryBeginHandleDrag(pointerState.ScreenPosition))
                return;
        }
        else if (pointerState.WasPressedThisFrame)
        {
            BeginPlacement(planePoint);
        }

        if (interactionMode == InteractionMode.None)
            return;

        if (pointerState.IsPressed)
            DragActiveInteraction(planePoint);

        if (pointerState.WasReleasedThisFrame)
            CommitActiveInteraction();
    }

    private void LateUpdate()
    {
        // The camera can zoom after Update, so handle scale is refreshed late to keep endpoint affordances a stable screen size.
        if (committedBridges.Count > 0)
            UpdateEndpointHandles();
    }

    private void OnGUI()
    {
        EnsureHudStyles();
        DrawBridgeCounter();
        DrawToast();

        // IMGUI keeps the prototype lightweight and avoids requiring a Canvas prefab.
        const float width = 180f;
        const float height = 42f;
        const float gap = 12f;
        float totalWidth = width * 2f + gap;
        editModeButtonRect = new Rect(
            (Screen.width - totalWidth) * 0.5f,
            Screen.height - height - 18f,
            width,
            height);
        clearButtonRect = new Rect(
            editModeButtonRect.xMax + gap,
            editModeButtonRect.y,
            width,
            height);

        string label = isEditMode ? "Exit Edit Bridges" : "Edit Bridges";
        Color previousColor = GUI.backgroundColor;
        GUI.backgroundColor = isEditMode ? new Color(0.15f, 0.85f, 1f, 1f) : Color.white;

        if (GUI.Button(editModeButtonRect, label, hudButtonStyle))
            SetEditMode(!isEditMode);

        GUI.backgroundColor = new Color(1f, 0.72f, 0.35f, 1f);

        if (GUI.Button(clearButtonRect, "Clear Bridges", hudButtonStyle))
            ClearAllRuntimeBridges();

        GUI.backgroundColor = previousColor;
    }

    private void EnsureHudStyles()
    {
        if (hudButtonStyle != null)
            return;

        hudButtonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold
        };

        counterStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        toastStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(16, 16, 8, 8),
            normal = { textColor = Color.white }
        };

        toastBackgroundTexture = CreateSolidTexture(new Color(0.12f, 0.55f, 0.26f, 1f));
        toastStyle.normal.background = toastBackgroundTexture;
        toastStyle.hover.background = toastBackgroundTexture;
        toastStyle.active.background = toastBackgroundTexture;
    }

    private void DrawBridgeCounter()
    {
        GUI.Label(new Rect(18f, 16f, 180f, 32f), $"Bridges: {committedBridges.Count}", counterStyle);
    }

    private void DrawToast()
    {
        if (string.IsNullOrEmpty(toastMessage) || Time.time >= toastUntilTime)
            return;

        float width = Mathf.Min(320f, Screen.width - 36f);
        Rect toastRect = new(
            (Screen.width - width) * 0.5f,
            Screen.height - 112f,
            width,
            38f);

        GUI.Box(toastRect, toastMessage, toastStyle);
    }

    private Texture2D CreateSolidTexture(Color color)
    {
        // IMGUI skins can ignore GUI.backgroundColor for box art, so the toast owns a tiny solid texture to keep its success color predictable.
        Texture2D texture = new(1, 1, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    private void SetEditMode(bool isEnabled)
    {
        if (isEditMode == isEnabled)
            return;

        // Switching modes should leave no half-built preview or half-applied endpoint edit.
        CancelPlacement();
        isEditMode = isEnabled;

        if (!isEditMode)
        {
            ClearSelectedHandleVisual();
        }

        UpdateHandleVisibility();
    }

    private bool IsScreenPositionOverHud(Vector2 screenPosition)
    {
        Vector2 guiPosition = new(screenPosition.x, Screen.height - screenPosition.y);
        return editModeButtonRect.Contains(guiPosition) || clearButtonRect.Contains(guiPosition);
    }

    private void ClearAllRuntimeBridges()
    {
        bool hadEditableBridges = committedBridges.Count > 0;
        CancelPlacement();
        builder.ClearPreview();
        ClearRuntimeBridges();
        // With no bridges left, edit mode has no valid targets.
        isEditMode = false;
        SetHandlesVisible(false);
        SetGuideVisible(false);

        if (hadEditableBridges)
            ShowToast("Bridges Cleared");
    }

    private void BeginPlacement(Vector3 point)
    {
        interactionMode = InteractionMode.Placing;
        dragStart = point;
        dragEnd = point;
        SetHandlesVisible(false);
        builder.ClearPreview();
    }

    private bool TryBeginHandleDrag(Vector2 screenPosition)
    {
        if (committedBridges.Count == 0)
            return false;

        if (!TryGetHandleUnderPointer(screenPosition, out RuntimeBridgeRecord selectedBridge, out bool isStartHandle))
            return false;

        editingBridge = selectedBridge;
        dragStart = editingBridge.Start;
        dragEnd = editingBridge.End;
        interactionMode = isStartHandle
            ? InteractionMode.EditingStart
            : InteractionMode.EditingEnd;

        editStartBeforeDrag = dragStart;
        editEndBeforeDrag = dragEnd;
        // Snapshot before editing so Escape/right-click can restore the bridge.
        SetSelectedHandleVisual(editingBridge, isStartHandle);
        builder.ClearPreview();
        return true;
    }

    private void DragActiveInteraction(Vector3 point)
    {
        if (interactionMode == InteractionMode.Placing)
        {
            PreviewPlacement(point);
            return;
        }

        PreviewEndpointEdit(point);
    }

    private void PreviewPlacement(Vector3 point)
    {
        dragEnd = point;
        UpdateGuideLine(dragStart, dragEnd);

        bool canBuild = CanBuildCurrentSpan();
        SetGuideColor(canBuild ? guideColor : invalidGuideColor);

        if (!canBuild)
        {
            builder.ClearPreview();
            return;
        }

        if (!builder.BuildPreview(dragStart, dragEnd))
        {
            SetGuideColor(invalidGuideColor);
            Debug.LogWarning(
                $"RuntimeBridgePlacement: Bridge preview failed at {Vector3.Distance(dragStart, dragEnd):0.00} units. Required minimum is {builder.MinimumBuildDistance:0.00} units.",
                this);
        }
    }

    private void CommitPlacement()
    {
        if (!CanBuildCurrentSpan())
        {
            CancelPlacement();
            return;
        }

        interactionMode = InteractionMode.None;
        SetGuideVisible(false);
        builder.ClearPreview();

        RuntimeBridgeRecord bridgeRecord = CreateRuntimeBridgeRecord(dragStart, dragEnd);

        if (bridgeRecord != null)
        {
            committedBridges.Add(bridgeRecord);
            ShowToast("Bridge Placed!");
            PlayConfirmSound(bridgePlacedClip);
        }

        UpdateHandleVisibility();
    }

    private void PreviewEndpointEdit(Vector3 point)
    {
        if (interactionMode == InteractionMode.EditingStart)
            dragStart = point;
        else if (interactionMode == InteractionMode.EditingEnd)
            dragEnd = point;

        bool canBuild = CanBuildCurrentSpan();
        SetGuideColor(canBuild ? guideColor : invalidGuideColor);
        UpdateGuideLine(dragStart, dragEnd);
        UpdateEndpointHandles();

        // Endpoint edits modify the real bridge mesh for immediate feedback, but are marked as preview so Commit/Cancel can decide whether to keep them.
        if (canBuild)
        {
            builder.UpdateRuntimeBridge(editingBridge.Bridge, dragStart, dragEnd, isPreview: true);
            return;
        }
    }

    private void CommitEndpointEdit()
    {
        if (!CanBuildCurrentSpan())
        {
            CancelPlacement();
            return;
        }

        interactionMode = InteractionMode.None;
        SetGuideVisible(false);
        ClearSelectedHandleVisual();
        editingBridge.Start = dragStart;
        editingBridge.End = dragEnd;
        builder.UpdateRuntimeBridge(editingBridge.Bridge, dragStart, dragEnd, isPreview: false);
        ShowToast("Bridge Updated");
        PlayConfirmSound(bridgeEditedClip);
        UpdateEndpointHandles();
        UpdateHandleVisibility();
        editingBridge = null;
    }

    private void CommitActiveInteraction()
    {
        if (interactionMode == InteractionMode.Placing)
        {
            CommitPlacement();
            return;
        }

        if (IsEditing)
            CommitEndpointEdit();
    }

    private void CancelPlacement()
    {
        if (interactionMode == InteractionMode.None)
            return;

        bool wasEditing = IsEditing;
        interactionMode = InteractionMode.None;

        if (wasEditing)
        {
            // Editing previews touch the existing bridge, so cancellation restores the saved endpoints instead of simply clearing a temporary preview.
            dragStart = editStartBeforeDrag;
            dragEnd = editEndBeforeDrag;
            builder.UpdateRuntimeBridge(editingBridge.Bridge, dragStart, dragEnd, isPreview: false);
        }
        else
        {
            builder.ClearPreview();
        }

        SetGuideVisible(false);
        ClearSelectedHandleVisual();
        UpdateHandleVisibility();
        UpdateEndpointHandles();
        editingBridge = null;
    }

    private void CreateGuideLine()
    {
        GameObject guideObject = new("Runtime Placement Guide");
        guideObject.transform.SetParent(transform, false);

        guideLine = guideObject.AddComponent<LineRenderer>();
        guideLine.positionCount = 2;
        guideLine.useWorldSpace = true;
        guideLine.startWidth = guideWidth;
        guideLine.endWidth = guideWidth;
        guideLine.numCapVertices = 6;
        guideLine.numCornerVertices = 4;
        guideLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        guideLine.receiveShadows = false;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        if (shader != null)
        {
            guideMaterial = new Material(shader);
            guideMaterial.color = guideColor;
            guideLine.sharedMaterial = guideMaterial;
        }

        guideLine.startColor = guideColor;
        guideLine.endColor = guideColor;
    }

    private void CreateEndpointHandles()
    {
        startHandleMaterial = CreateUnlitMaterial(startHandleColor);
        endHandleMaterial = CreateUnlitMaterial(endHandleColor);
        selectedHandleMaterial = CreateUnlitMaterial(selectedHandleColor);
    }

    private void CreateFeedbackAudio()
    {
        if (Mathf.Approximately(confirmSoundVolume, 0f))
            return;

        feedbackAudioSource = gameObject.AddComponent<AudioSource>();
        feedbackAudioSource.playOnAwake = false;
        feedbackAudioSource.spatialBlend = 0f;

        // Unity does not include general-purpose runtime UI sounds, so the prototype synthesizes tiny confirmation tones instead of importing assets.
        bridgePlacedClip = CreateConfirmClip("Bridge Placed Confirm", 520f, 780f, 0.12f);
        bridgeEditedClip = CreateConfirmClip("Bridge Edited Confirm", 410f, 615f, 0.09f);
    }

    private AudioClip CreateConfirmClip(string clipName, float lowFrequency, float highFrequency, float duration)
    {
        const int sampleRate = 44100;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float time = i / (float)sampleRate;
            float envelope = Mathf.Exp(-time * 18f);
            float lowTone = Mathf.Sin(2f * Mathf.PI * lowFrequency * time) * 0.55f;
            float highTone = Mathf.Sin(2f * Mathf.PI * highFrequency * time) * 0.45f;
            samples[i] = (lowTone + highTone) * envelope * confirmSoundVolume;
        }

        AudioClip clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private Transform CreateEndpointHandle(string handleName, Material material)
    {
        GameObject handleObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        handleObject.name = handleName;
        handleObject.transform.SetParent(transform, false);
        handleObject.transform.localScale = GetHandleScale();

        Renderer handleRenderer = handleObject.GetComponent<Renderer>();

        if (handleRenderer != null)
            handleRenderer.sharedMaterial = material;

        return handleObject.transform;
    }

    private Material CreateUnlitMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        if (shader == null)
            return null;

        Material material = new(shader)
        {
            color = color
        };

        return material;
    }

    private void UpdateGuideLine(Vector3 start, Vector3 end)
    {
        if (guideLine == null)
            return;

        Vector3 lineLift = Vector3.up * 0.08f;
        guideLine.SetPosition(0, start + lineLift);
        guideLine.SetPosition(1, end + lineLift);
        SetGuideVisible(true);
    }

    private void SetGuideVisible(bool isVisible)
    {
        if (guideLine != null)
            guideLine.enabled = isVisible;
    }

    private void SetGuideColor(Color color)
    {
        if (guideLine == null)
            return;

        guideLine.startColor = color;
        guideLine.endColor = color;

        if (guideMaterial != null)
            guideMaterial.color = color;
    }

    private void UpdateEndpointHandles()
    {
        for (int i = 0; i < committedBridges.Count; i++)
        {
            RuntimeBridgeRecord bridgeRecord = committedBridges[i];
            // During an active edit, handles follow the in-progress endpoints while the record still preserves the last committed positions.
            Vector3 start = bridgeRecord == editingBridge ? dragStart : bridgeRecord.Start;
            Vector3 end = bridgeRecord == editingBridge ? dragEnd : bridgeRecord.End;

            if (bridgeRecord.StartHandle != null)
            {
                bridgeRecord.StartHandle.position = GetHandlePosition(start);
                bridgeRecord.StartHandle.localScale = GetHandleScale();
            }

            if (bridgeRecord.EndHandle != null)
            {
                bridgeRecord.EndHandle.position = GetHandlePosition(end);
                bridgeRecord.EndHandle.localScale = GetHandleScale();
            }
        }
    }

    private Vector3 GetHandleScale()
    {
        float diameter = handleRadius * 2f;

        // Convert a desired pixel diameter into world units for the current orthographic zoom.
        if (placementCamera != null && placementCamera.orthographic && placementCamera.pixelHeight > 0)
        {
            diameter = 2f * placementCamera.orthographicSize * (handleScreenDiameter / placementCamera.pixelHeight);
        }

        return Vector3.one * Mathf.Max(0.01f, diameter);
    }

    private Vector3 GetHandlePosition(Vector3 point)
    {
        point.y = placementPlaneY + handleHeight;
        return point;
    }

    private void SetHandlesVisible(bool isVisible)
    {
        for (int i = 0; i < committedBridges.Count; i++)
        {
            RuntimeBridgeRecord bridgeRecord = committedBridges[i];

            if (bridgeRecord.StartHandle != null)
                bridgeRecord.StartHandle.gameObject.SetActive(isVisible);

            if (bridgeRecord.EndHandle != null)
                bridgeRecord.EndHandle.gameObject.SetActive(isVisible);
        }
    }

    private void UpdateHandleVisibility()
    {
        for (int i = 0; i < committedBridges.Count; i++)
        {
            RuntimeBridgeRecord bridgeRecord = committedBridges[i];
            bool isVisible = isEditMode;

            if (bridgeRecord.StartHandle != null)
                bridgeRecord.StartHandle.gameObject.SetActive(isVisible);

            if (bridgeRecord.EndHandle != null)
                bridgeRecord.EndHandle.gameObject.SetActive(isVisible);
        }
    }

    private bool TryGetHandleUnderPointer(Vector2 screenPosition, out RuntimeBridgeRecord bridgeRecord, out bool isStartHandle)
    {
        Ray ray = placementCamera.ScreenPointToRay(screenPosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, 500f);
        bridgeRecord = null;
        isStartHandle = false;
        float closestDistance = float.PositiveInfinity;

        // Handles are regular sphere primitives with colliders. If both endpoints overlap in screen space, pick the front-most hit.
        for (int i = 0; i < hits.Length; i++)
        {
            Transform hitTransform = hits[i].transform;
            RuntimeBridgeRecord hitRecord = GetBridgeRecordForHandle(hitTransform, out bool hitIsStartHandle);

            if (hitRecord == null)
                continue;

            if (hits[i].distance >= closestDistance)
                continue;

            closestDistance = hits[i].distance;
            bridgeRecord = hitRecord;
            isStartHandle = hitIsStartHandle;
        }

        return bridgeRecord != null;
    }

    private RuntimeBridgeRecord GetBridgeRecordForHandle(Transform handle, out bool isStartHandle)
    {
        for (int i = 0; i < committedBridges.Count; i++)
        {
            RuntimeBridgeRecord bridgeRecord = committedBridges[i];

            if (handle == bridgeRecord.StartHandle)
            {
                isStartHandle = true;
                return bridgeRecord;
            }

            if (handle == bridgeRecord.EndHandle)
            {
                isStartHandle = false;
                return bridgeRecord;
            }
        }

        isStartHandle = false;
        return null;
    }

    private void SetSelectedHandleVisual(RuntimeBridgeRecord bridgeRecord, bool isStartHandle)
    {
        ClearSelectedHandleVisual();
        ApplyHandleMaterial(
            bridgeRecord.StartHandle,
            isStartHandle ? selectedHandleMaterial : startHandleMaterial);
        ApplyHandleMaterial(
            bridgeRecord.EndHandle,
            isStartHandle ? endHandleMaterial : selectedHandleMaterial);
    }

    private void ClearSelectedHandleVisual()
    {
        for (int i = 0; i < committedBridges.Count; i++)
        {
            ApplyHandleMaterial(committedBridges[i].StartHandle, startHandleMaterial);
            ApplyHandleMaterial(committedBridges[i].EndHandle, endHandleMaterial);
        }
    }

    private void ApplyHandleMaterial(Transform handle, Material material)
    {
        if (handle == null || material == null)
            return;

        Renderer handleRenderer = handle.GetComponent<Renderer>();

        if (handleRenderer != null)
            handleRenderer.sharedMaterial = material;
    }

    private void ConfigurePlacementCamera()
    {
        if (placementCamera == null)
            return;

        RuntimeIsometricCamera isometricCamera = placementCamera.GetComponent<RuntimeIsometricCamera>();

        if (isometricCamera == null)
        {
            // Adding the controller is enough when the camera has no existing runtime isometric state to preserve.
            isometricCamera = placementCamera.gameObject.AddComponent<RuntimeIsometricCamera>();
            isometricCamera.ApplyView();
            return;
        }

        // If the component already exists, force the transform and lens directly so play mode starts from the expected placement framing.
        Quaternion viewRotation = Quaternion.Euler(IsometricPitch, IsometricYaw, 0f);
        placementCamera.transform.SetPositionAndRotation(
            IsometricTargetPosition - viewRotation * Vector3.forward * IsometricDistance,
            viewRotation);

        placementCamera.orthographic = true;
        placementCamera.orthographicSize = IsometricOrthographicSize;
    }

    private bool TryGetPlanePoint(Vector2 screenPosition, out Vector3 point)
    {
        Ray ray = placementCamera.ScreenPointToRay(screenPosition);
        // Bridge endpoints are chosen by intersecting the pointer ray with a flat construction plane rather than by relying on scene colliders.
        Plane placementPlane = new(Vector3.up, new Vector3(0f, placementPlaneY, 0f));

        if (placementPlane.Raycast(ray, out float distance))
        {
            point = ray.GetPoint(distance);
            return true;
        }

        point = default;
        return false;
    }

    private bool CanBuildCurrentSpan()
    {
        float requiredDistance = Mathf.Max(minimumCommitDistance, builder.MinimumBuildDistance);
        return Vector3.Distance(dragStart, dragEnd) >= requiredDistance && builder.CanBuild(dragStart, dragEnd);
    }

    private RuntimeBridgeRecord CreateRuntimeBridgeRecord(Vector3 start, Vector3 end)
    {
        BridgeBuilder.BuiltBridge builtBridge = builder.CreateRuntimeBridge(
            start,
            end,
            $"Runtime Bridge {++bridgeNameIndex}");

        if (builtBridge == null)
            return null;

        RuntimeBridgeRecord bridgeRecord = new()
        {
            Bridge = builtBridge,
            Start = start,
            End = end,
            StartHandle = CreateEndpointHandle($"Bridge {bridgeNameIndex} Start Handle", startHandleMaterial),
            EndHandle = CreateEndpointHandle($"Bridge {bridgeNameIndex} End Handle", endHandleMaterial),
            IsRuntimeCreated = true
        };

        bridgeRecord.StartHandle.position = GetHandlePosition(start);
        bridgeRecord.EndHandle.position = GetHandlePosition(end);
        return bridgeRecord;
    }

    private void AdoptAuthoredBridges()
    {
        if (builder == null)
            return;

        builder.GetAuthoredBridges(discoveredAuthoredBridges);

        for (int i = 0; i < discoveredAuthoredBridges.Count; i++)
        {
            BridgeBuilder.BuiltBridge bridge = discoveredAuthoredBridges[i];

            if (!builder.TryGetBridgeEndpoints(bridge, out Vector3 start, out Vector3 end))
                continue;

            // Editor-authored bridge roots are serialized in the scene.
            // At Play startup we wrap them in runtime records so the same endpoint handle editing path works for authored and newly placed bridges.
            RuntimeBridgeRecord bridgeRecord = new()
            {
                Bridge = bridge,
                Start = start,
                End = end,
                StartHandle = CreateEndpointHandle($"Authored Bridge {i + 1} Start Handle", startHandleMaterial),
                EndHandle = CreateEndpointHandle($"Authored Bridge {i + 1} End Handle", endHandleMaterial),
                IsRuntimeCreated = false
            };

            bridgeRecord.StartHandle.position = GetHandlePosition(start);
            bridgeRecord.EndHandle.position = GetHandlePosition(end);
            committedBridges.Add(bridgeRecord);
        }
    }

    private void ClearRuntimeBridges()
    {
        for (int i = committedBridges.Count - 1; i >= 0; i--)
        {
            RuntimeBridgeRecord bridgeRecord = committedBridges[i];

            if (bridgeRecord.StartHandle != null)
                Destroy(bridgeRecord.StartHandle.gameObject);

            if (bridgeRecord.EndHandle != null)
                Destroy(bridgeRecord.EndHandle.gameObject);

            // Runtime-created bridges own their root object. 
            // Authored bridges may share BridgeBuilder's content root, so they are cleared through the builder.
            if (bridgeRecord.Bridge != null && bridgeRecord.Bridge.Root != null && !builder.IsContentRoot(bridgeRecord.Bridge.Root))
                Destroy(bridgeRecord.Bridge.Root.gameObject);
            else if (bridgeRecord.Bridge != null && builder.IsContentRoot(bridgeRecord.Bridge.Root))
                builder.ClearAll();

            committedBridges.RemoveAt(i);
        }

        editingBridge = null;
        bridgeNameIndex = 0;
    }

    private void ShowToast(string message)
    {
        toastMessage = message;
        toastUntilTime = Time.time + toastDuration;
    }

    private void PlayConfirmSound(AudioClip clip)
    {
        if (feedbackAudioSource == null || clip == null || Mathf.Approximately(confirmSoundVolume, 0f))
            return;

        feedbackAudioSource.PlayOneShot(clip, 1f);
    }

    private bool TryGetPointerState(out PointerState pointerState)
    {
        Mouse mouse = Mouse.current;

        if (mouse != null)
        {
            pointerState = new PointerState(
                mouse.position.ReadValue(),
                mouse.leftButton.isPressed,
                mouse.leftButton.wasPressedThisFrame,
                mouse.leftButton.wasReleasedThisFrame,
                Keyboard.current != null && Keyboard.current.altKey.isPressed && mouse.leftButton.isPressed);
            return true;
        }

        Touchscreen touchscreen = Touchscreen.current;

        if (touchscreen != null)
        {
            bool isPressed = touchscreen.primaryTouch.press.isPressed;
            bool wasPressedThisFrame = touchscreen.primaryTouch.press.wasPressedThisFrame;
            bool wasReleasedThisFrame = touchscreen.primaryTouch.press.wasReleasedThisFrame;

            if (!isPressed && !wasPressedThisFrame && !wasReleasedThisFrame)
            {
                pointerState = default;
                return false;
            }

            pointerState = new PointerState(
                touchscreen.primaryTouch.position.ReadValue(),
                isPressed,
                wasPressedThisFrame,
                wasReleasedThisFrame,
                false);
            return true;
        }

        pointerState = default;
        return false;
    }

    private bool WasClearPressed()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard[Key.R].wasPressedThisFrame;
    }

    private bool WasCancelPressed()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && keyboard[Key.Escape].wasPressedThisFrame)
            return true;

        Mouse mouse = Mouse.current;
        return mouse != null && mouse.rightButton.wasPressedThisFrame;
    }

    private readonly struct PointerState
    {
        public PointerState(
            Vector2 screenPosition,
            bool isPressed,
            bool wasPressedThisFrame,
            bool wasReleasedThisFrame,
            bool isCameraOrbitGesture)
        {
            ScreenPosition = screenPosition;
            IsPressed = isPressed;
            WasPressedThisFrame = wasPressedThisFrame;
            WasReleasedThisFrame = wasReleasedThisFrame;
            IsCameraOrbitGesture = isCameraOrbitGesture;
        }

        public Vector2 ScreenPosition { get; }
        public bool IsPressed { get; }
        public bool WasPressedThisFrame { get; }
        public bool WasReleasedThisFrame { get; }
        public bool IsCameraOrbitGesture { get; }
    }

    private enum InteractionMode
    {
        None,
        Placing,
        EditingStart,
        EditingEnd
    }

    private sealed class RuntimeBridgeRecord
    {
        public BridgeBuilder.BuiltBridge Bridge;
        public Vector3 Start;
        public Vector3 End;
        public Transform StartHandle;
        public Transform EndHandle;
        public bool IsRuntimeCreated;
    }
}
