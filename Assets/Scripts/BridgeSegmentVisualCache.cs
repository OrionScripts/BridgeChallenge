using UnityEngine;

[DisallowMultipleComponent]
public class BridgeSegmentVisualCache : MonoBehaviour
{
    // Populated lazily on the first preview/final swap so finalized bridges that are never edited do not pay setup cost.
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
                // Reuse same-sized material arrays during drag updates.
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

        // Copy once so preview mode can always restore authored materials.
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
