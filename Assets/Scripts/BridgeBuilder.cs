using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class BridgeBuilder : MonoBehaviour
{
    [Header("Segment Prefabs")]
    [SerializeField] private BridgeSegment startSegment;
    [SerializeField] private BridgeSegment middleSegment;
    [SerializeField] private BridgeSegment fillerSegment;
    [SerializeField] private BridgeSegment endSegment;

    [Header("Roots")]
    [SerializeField] private Transform contentRoot;
    [SerializeField] private Transform previewRoot;

    [Header("Preview")]
    [SerializeField] private Material previewMaterial;

    // Reused scratch list for segment layout calculation. This keeps drag-time allocations low.
    private readonly List<SegmentPlacement> segmentPlacements = new();
    // Final bridge content that remains after placement/editing is committed.
    private readonly List<GameObject> finalInstances = new();
    // Separate hierarchy used only while placing a brand new bridge from scratch.
    private readonly List<GameObject> previewInstances = new();
    // Pool removed segments by authored type so drag edits can reuse objects instead of constantly destroying and recreating them.
    private readonly Dictionary<BridgeSegmentType, Stack<GameObject>> pooledInstancesByType = new();

    public void Build(Vector3 pointA, Vector3 pointB)
    {
        if (!TryCreateBuildPlan(pointA, pointB, out List<SegmentPlacement> placements))
        {
            ClearAll();
            return;
        }

        ReconcileInstances(contentRoot, finalInstances, placements, isPreview: false);
        SetRootVisible(contentRoot, true);
        SetRootVisible(previewRoot, false);
        NotifyEditorBridgeChanged(contentRoot);
    }

    public void BuildPreview(Vector3 pointA, Vector3 pointB)
    {
        if (!TryCreateBuildPlan(pointA, pointB, out List<SegmentPlacement> placements))
        {
            ClearPreview();
            SetRootVisible(contentRoot, false);
            return;
        }

        SetRootVisible(contentRoot, false);
        SetRootVisible(previewRoot, true);
        // Reconcile instead of brute-force rebuilding so the preview remains responsive while dragging.
        ReconcileInstances(previewRoot, previewInstances, placements, isPreview: true);
        NotifyEditorBridgeChanged(previewRoot);
    }

    // Existing bridge edits preview on the final content root so the swap back is immediate and we avoid maintaining two copies of a potentially large bridge during handle drags.
    public void BuildEditPreview(Vector3 pointA, Vector3 pointB)
    {
        if (!TryCreateBuildPlan(pointA, pointB, out List<SegmentPlacement> placements))
        {
            ClearAll();
            return;
        }

        ClearPreview();
        ReconcileInstances(contentRoot, finalInstances, placements, isPreview: true);
        SetRootVisible(contentRoot, true);
        SetRootVisible(previewRoot, false);
        NotifyEditorBridgeChanged(contentRoot);
    }

    public void ClearPreview()
    {
        ClearList(previewInstances);
        SetRootVisible(previewRoot, false);
        NotifyEditorBridgeChanged(previewRoot);
    }

    public void ClearAll()
    {
        ClearList(finalInstances);
        SetRootVisible(contentRoot, false);
        ClearPreview();
        NotifyEditorBridgeChanged(contentRoot);
    }

    public bool TryGetContentCenter(out Vector3 center)
    {
        center = default;

        if (contentRoot == null)
            return false;

        // Render bounds produce a more intuitive pivot location than using the raw midpoint, especially when support meshes extend away from the deck.
        Renderer[] renderers = contentRoot.GetComponentsInChildren<Renderer>(true);

        if (renderers.Length == 0)
            return false;

        Bounds combinedBounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
        {
            combinedBounds.Encapsulate(renderers[i].bounds);
        }

        center = combinedBounds.center;
        return true;
    }

    private bool TryCreateBuildPlan(
        Vector3 pointA,
        Vector3 pointB,
        out List<SegmentPlacement> placements)
    {
        // The build plan is pure data: which segment type goes where. Keeping this separate from hierarchy mutation makes the placement math easier to read, test, and optimize.
        placements = segmentPlacements;
        placements.Clear();

        if (!HasValidSetup())
            return false;

        Vector3 span = pointB - pointA;
        float totalDistance = span.magnitude;

        if (totalDistance <= 0.001f)
            return false;

        Vector3 direction = span.normalized;

        float usableDistance = totalDistance - startSegment.Length - endSegment.Length;

        if (usableDistance < 0f)
            return false;

        int middleCount = Mathf.FloorToInt(usableDistance / middleSegment.Length);
        float remainingDistance = usableDistance - middleCount * middleSegment.Length;

        int fillerCount = 0;

        if (fillerSegment.Length > 0.0001f)
        {
            fillerCount = Mathf.FloorToInt(remainingDistance / fillerSegment.Length);
        }

        int leftFillers = fillerCount / 2;
        int rightFillers = fillerCount - leftFillers;

        float usedDistance =
            startSegment.Length +
            middleCount * middleSegment.Length +
            fillerCount * fillerSegment.Length +
            endSegment.Length;

        float slack = totalDistance - usedDistance;

        // Center the authored bridge within the requested span when the exact segment set does not consume the full distance.
        Vector3 cursor = pointA + direction * (slack * 0.5f);
        Quaternion rotation = Quaternion.FromToRotation(Vector3.right, direction);

        placements.Add(new SegmentPlacement(startSegment, cursor, rotation));
        cursor += direction * startSegment.Length;

        for (int i = 0; i < leftFillers; i++)
        {
            placements.Add(new SegmentPlacement(fillerSegment, cursor, rotation));
            cursor += direction * fillerSegment.Length;
        }

        for (int i = 0; i < middleCount; i++)
        {
            placements.Add(new SegmentPlacement(middleSegment, cursor, rotation));
            cursor += direction * middleSegment.Length;
        }

        for (int i = 0; i < rightFillers; i++)
        {
            placements.Add(new SegmentPlacement(fillerSegment, cursor, rotation));
            cursor += direction * fillerSegment.Length;
        }

        placements.Add(new SegmentPlacement(endSegment, cursor, rotation));
        return true;
    }

    private void ReconcileInstances(
        Transform root,
        List<GameObject> spawnedInstances,
        List<SegmentPlacement> placements,
        bool isPreview)
    {
        if (root == null)
        {
            Debug.LogWarning("BridgeBuilder: Missing root.", this);
            return;
        }

        // Reuse live instances in place wherever possible. The expensive operation during dragging is object churn, not the placement math, so this keeps editor interaction much smoother.
        for (int i = 0; i < placements.Count; i++)
        {
            SegmentPlacement placement = placements[i];
            GameObject instanceObject = GetOrCreateInstance(root, spawnedInstances, i, placement.Prefab);

            Transform instanceTransform = instanceObject.transform;
            instanceTransform.SetPositionAndRotation(placement.Position, placement.Rotation);

            ApplyVisualState(instanceObject, isPreview);
        }

        TrimExcessInstances(spawnedInstances, placements.Count);
    }

    private GameObject GetOrCreateInstance(
        Transform root,
        List<GameObject> spawnedInstances,
        int index,
        BridgeSegment prefab)
    {
        if (index >= spawnedInstances.Count)
        {
            GameObject newInstance = GetOrCreatePooledInstance(root, prefab);
            spawnedInstances.Add(newInstance);
            return newInstance;
        }

        GameObject existingObject = spawnedInstances[index];

        if (existingObject == null)
        {
            GameObject recreatedInstance = GetOrCreatePooledInstance(root, prefab);
            spawnedInstances[index] = recreatedInstance;
            return recreatedInstance;
        }

        BridgeSegment existingSegment = existingObject.GetComponent<BridgeSegment>();

        // If the slot now needs a different segment type, replace only that slot rather than tearing down and rebuilding the whole bridge.
        if (existingSegment == null || existingSegment.SegmentType != prefab.SegmentType)
        {
            ReturnInstanceToPool(existingObject);

            GameObject replacementInstance = GetOrCreatePooledInstance(root, prefab);
            spawnedInstances[index] = replacementInstance;
            return replacementInstance;
        }

        return existingObject;
    }

    private GameObject GetOrCreatePooledInstance(Transform root, BridgeSegment prefab)
    {
        Stack<GameObject> pooledInstances = GetPool(prefab.SegmentType);

        while (pooledInstances.Count > 0)
        {
            GameObject pooledObject = pooledInstances.Pop();

            if (pooledObject == null)
                continue;

            pooledObject.hideFlags = HideFlags.None;
            pooledObject.transform.SetParent(root, false);
            pooledObject.SetActive(true);
            return pooledObject;
        }

        BridgeSegment newInstance = Instantiate(prefab, Vector3.zero, Quaternion.identity, root);
        return newInstance.gameObject;
    }

    private void ApplyVisualState(GameObject target, bool isPreview)
    {
        // Cache component lookups/material arrays on the segment root so preview/final swaps are cheap even when many child renderers are involved.
        BridgeSegmentVisualCache visualCache = target.GetComponent<BridgeSegmentVisualCache>();

        if (visualCache == null)
            visualCache = target.AddComponent<BridgeSegmentVisualCache>();

        if (isPreview)
            visualCache.ApplyPreview(previewMaterial);
        else
            visualCache.RestoreFinal();
    }

    private void TrimExcessInstances(List<GameObject> spawnedInstances, int desiredCount)
    {
        // When the bridge shortens, trim only the surplus tail elements.
        for (int i = spawnedInstances.Count - 1; i >= desiredCount; i--)
        {
            ReturnInstanceToPool(spawnedInstances[i]);
            spawnedInstances.RemoveAt(i);
        }
    }

    private void ClearList(List<GameObject> list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            ReturnInstanceToPool(list[i]);
        }

        list.Clear();
    }

    private void ReturnInstanceToPool(GameObject target)
    {
        if (target == null)
            return;

        BridgeSegment segment = target.GetComponent<BridgeSegment>();

        if (segment == null)
        {
            DestroyObject(target);
            return;
        }

        BridgeSegmentVisualCache visualCache = target.GetComponent<BridgeSegmentVisualCache>();

        if (visualCache != null)
            visualCache.RestoreFinal();

        target.transform.SetParent(transform, false);
        target.SetActive(false);
        target.hideFlags = HideFlags.HideInHierarchy;

        GetPool(segment.SegmentType).Push(target);
    }

    private void DestroyObject(GameObject target)
    {
        if (target == null)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
            DestroyImmediate(target);
        else
            Destroy(target);
#else
        Destroy(target);
#endif
    }

    private bool HasValidSetup()
    {
        if (startSegment == null || middleSegment == null || fillerSegment == null || endSegment == null)
        {
            Debug.LogWarning("BridgeBuilder: Missing one or more segment prefabs.", this);
            return false;
        }

        return true;
    }

    private Stack<GameObject> GetPool(BridgeSegmentType segmentType)
    {
        if (!pooledInstancesByType.TryGetValue(segmentType, out Stack<GameObject> pool))
        {
            pool = new Stack<GameObject>();
            pooledInstancesByType.Add(segmentType, pool);
        }

        return pool;
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void NotifyEditorBridgeChanged(Transform root)
    {
#if UNITY_EDITOR
        EditorUtility.SetDirty(this);

        if (root != null)
        {
            EditorUtility.SetDirty(root);
            EditorUtility.SetDirty(root.gameObject);
        }
#endif
    }

    private void SetRootVisible(Transform root, bool isVisible)
    {
        if (root == null)
            return;

        if (root.gameObject.activeSelf == isVisible)
            return;

        root.gameObject.SetActive(isVisible);
        NotifyEditorBridgeChanged(root);
    }

    private readonly struct SegmentPlacement
    {
        public SegmentPlacement(BridgeSegment prefab, Vector3 position, Quaternion rotation)
        {
            Prefab = prefab;
            Position = position;
            Rotation = rotation;
        }

        public BridgeSegment Prefab { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
    }
}

[DisallowMultipleComponent]
public class BridgeSegmentVisualCache : MonoBehaviour
{
    // These caches are populated lazily the first time the segment toggles preview/final state.
    // That keeps setup cost low for finalized bridges that are never edited again in-session.
    private Renderer[] renderers;
    private Collider[] colliders;
    private Material[][] originalSharedMaterials;
    private Material[][] previewSharedMaterials;

    public void ApplyPreview(Material previewMaterial)
    {
        CacheVisualState();

        if (previewMaterial != null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                // Reuse a same-sized shared-material array filled with the preview material rather than allocating a fresh one every drag update.
                Material[] previewMaterials = GetOrCreatePreviewMaterials(i, previewMaterial);
                renderers[i].sharedMaterials = previewMaterials;
            }
        }

        SetCollidersEnabled(false);
    }

    public void RestoreFinal()
    {
        CacheVisualState();

        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].sharedMaterials = originalSharedMaterials[i];
        }

        SetCollidersEnabled(true);
    }

    private void CacheVisualState()
    {
        if (renderers == null)
            renderers = GetComponentsInChildren<Renderer>(true);

        if (colliders == null)
            colliders = GetComponentsInChildren<Collider>(true);

        if (originalSharedMaterials != null)
            return;

        // Copy once so we can always restore authored materials after preview mode.
        originalSharedMaterials = new Material[renderers.Length][];
        previewSharedMaterials = new Material[renderers.Length][];

        for (int i = 0; i < renderers.Length; i++)
        {
            Material[] sharedMaterials = renderers[i].sharedMaterials;
            Material[] originalMaterials = new Material[sharedMaterials.Length];

            for (int materialIndex = 0; materialIndex < sharedMaterials.Length; materialIndex++)
            {
                originalMaterials[materialIndex] = sharedMaterials[materialIndex];
            }

            originalSharedMaterials[i] = originalMaterials;
        }
    }

    private Material[] GetOrCreatePreviewMaterials(int rendererIndex, Material previewMaterial)
    {
        Material[] previewMaterials = previewSharedMaterials[rendererIndex];

        if (previewMaterials != null && previewMaterials.Length == originalSharedMaterials[rendererIndex].Length)
            return previewMaterials;

        int materialCount = originalSharedMaterials[rendererIndex].Length;
        previewMaterials = new Material[materialCount];

        for (int i = 0; i < materialCount; i++)
        {
            previewMaterials[i] = previewMaterial;
        }

        previewSharedMaterials[rendererIndex] = previewMaterials;
        return previewMaterials;
    }

    private void SetCollidersEnabled(bool isEnabled)
    {
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = isEnabled;
        }
    }
}
