using UnityEngine;

/// <summary>
/// Randomizes the scale and specific material colors of the ragdoll upon spawning.
/// This applies Domain Randomization to prevent AI overfitting.
/// </summary>
public class RagdollRandomizer : MonoBehaviour
{
    [Header("Scale Randomization")]
    public float minScale = 0.8f;
    public float maxScale = 1.2f;

    [Header("Skinned Mesh Renderers")]
    [Tooltip("Assign the BaseBody object here")]
    public SkinnedMeshRenderer baseBody;
    
    [Tooltip("Assign the Shoe_7 object here")]
    public SkinnedMeshRenderer shoe;
    
    [Tooltip("Assign the Underwear object here")]
    public SkinnedMeshRenderer underwear;
    
    [Tooltip("Assign the Top_Sport object here")]
    public SkinnedMeshRenderer topSport;

    [Header("Skin Tone Gradient")]
    [Tooltip("Gradient ranging from pale to dark brown skin tones")]
    public Gradient skinColorGradient;

    void Start()
    {
        RandomizeScale();
        RandomizeColors();
    }

    void RandomizeScale()
    {
        // Applies a uniform random scale to the entire ragdoll hierarchy
        float randomScale = Random.Range(minScale, maxScale);
        transform.localScale = new Vector3(randomScale, randomScale, randomScale);
    }

    void RandomizeColors()
    {
        // 1. Randomize Skin Tone
        if (baseBody != null)
        {
            // Evaluate a random point on the gradient (0.0 to 1.0)
            Color skinColor = skinColorGradient.Evaluate(Random.Range(0f, 1f));
            SetMaterialColor(baseBody, 0, skinColor);
        }

        // Generate independent random colors for clothing parts
        // ColorHSV generates vivid, highly visible colors
        Color primaryClothColor = Random.ColorHSV(0f, 1f, 0.4f, 1f, 0.3f, 1f); 
        Color secondaryClothColor = Random.ColorHSV(0f, 1f, 0.3f, 1f, 0.2f, 0.9f);
        Color accentColor = Random.ColorHSV(0f, 1f, 0.5f, 1f, 0.1f, 1f);
        Color shoeColor = Random.ColorHSV(0f, 1f, 0.1f, 0.8f, 0.1f, 0.5f); // Darker range for shoes

        // 2. Randomize Shoes
        // Indices based on inspector: [0] Boots, [1] Socks, [2] Tongue
        if (shoe != null)
        {
            SetMaterialColor(shoe, 0, shoeColor);           // Boots
            SetMaterialColor(shoe, 1, Color.white);         // Socks (kept white/gray for realism)
            SetMaterialColor(shoe, 2, secondaryClothColor); // Tongue
        }

        // 3. Randomize Underwear (Shorts)
        // Indices based on inspector: [0] Shorts
        if (underwear != null)
        {
            SetMaterialColor(underwear, 0, secondaryClothColor);
        }

        // 4. Randomize Top Sport (Shirt)
        // Indices based on inspector: [0] Numbers/Boots match, [1] Shirt, [2] Edges
        if (topSport != null)
        {
            SetMaterialColor(topSport, 0, accentColor);       // Numbers
            SetMaterialColor(topSport, 1, primaryClothColor); // Main Shirt
            SetMaterialColor(topSport, 2, accentColor);       // Edges
        }
    }

    /// <summary>
    /// Safely updates the color of a specific material index on a SkinnedMeshRenderer.
    /// Using .materials creates a runtime instance, protecting source assets.
    /// </summary>
    void SetMaterialColor(SkinnedMeshRenderer smr, int materialIndex, Color newColor)
    {
        // Ensure the renderer has enough materials to prevent index out of bounds
        if (smr.materials.Length > materialIndex)
        {
            // Create a copy of the materials array (instantiates them for this object only)
            Material[] mats = smr.materials;
            
            // URP Lit shaders use _BaseColor instead of _Color
            if (mats[materialIndex].HasProperty("_BaseColor"))
            {
                mats[materialIndex].SetColor("_BaseColor", newColor);
            }
            else
            {
                // Fallback for standard/older shaders
                mats[materialIndex].color = newColor; 
            }
            
            // Reassign the modified array back to the renderer to apply changes
            smr.materials = mats;
        }
    }
}