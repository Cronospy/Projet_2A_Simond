using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;

/// <summary>
/// YOLO Dataset Generator for person detection in forest.
/// 
/// Pipeline per scene:
///   1. Generate random low-poly landscape (random seed)
///   2. Spawn ragdoll "Male Athlete Better" above terrain
///   3. Wait for ragdoll to settle (fall + physics)
///   4. Safety check: did ragdoll fall through terrain? → retry
///   5. Spawn drone, orbit ragdoll at varying angles/heights
///   6. Each orbit position: render frame → raycast to body parts → label
///   7. Save JPG + YOLO .txt label (class cx cy w h)
///   8. Repeat for N scenes → one unified dataset
///
/// Drone auto-capture (DroneCamera.cs) is DISABLED here — we control capture manually.
/// DroneController.cs is also DISABLED — drone is teleported, not physics-driven.
/// </summary>
public class DatasetGenerator : MonoBehaviour
{
    // ================================================================
    // SCENE REFERENCES
    // ================================================================

    [Header("Scene References")]
    [Tooltip("The LowPolyLandscape component in the scene")]
    public LowPolyLandscape landscape;

    [Tooltip("Prefab: 'Male Athlete Better' ragdoll")]
    public GameObject ragdollPrefab;

    [Tooltip("Prefab: Drone (must contain a Camera child and optionally DroneCamera/DroneController)")]
    public GameObject dronePrefab;

    // ================================================================
    // DATASET SETTINGS
    // ================================================================

    [Header("Dataset Settings")]
    [Tooltip("How many scenes to generate in total")]
    public int totalScenes = 10;

    [Tooltip("How many orbit positions (= photos) per scene")]
    public int photosPerScene = 40;

    [Tooltip("Photo resolution — should match your YOLO training crop size")]
    public int photoWidth  = 1024;
    public int photoHeight = 1024;

    [Tooltip("Root folder for the output dataset")]
    public string datasetRootPath = @"D:\Unity Projects\My project\Assets\Dataset";

    // ================================================================
    // RAGDOLL SETTINGS
    // ================================================================

    [Header("Ragdoll Settings")]
    [Tooltip("Meters above detected terrain surface to spawn the ragdoll")]
    public float spawnHeightAboveTerrain = 5f;

    [Tooltip("Max seconds to wait for the ragdoll to stop moving")]
    public float maxSettleTime = 6f;

    [Tooltip("Ragdoll is 'settled' when ALL rigidbody velocities are below this (m/s)")]
    public float settleVelocityThreshold = 0.08f;

    [Tooltip("If ragdoll root Y drops below this value → it fell through → retry scene")]
    public float fallThroughYThreshold = -30f;

    [Tooltip("If ragdoll root Y is above this after settling → something went wrong → retry")]
    public float maxAllowedY = 200f;

    // ================================================================
    // DRONE FLIGHT SETTINGS
    // ================================================================
    
    [Header("Drone Grid Flight Settings")]
    [Tooltip("Flight height based on the optimal 1 km^2 search model")]
    public float flightHeight = 10f;

    [Tooltip("Total width of the search area (meters)")]
    public float gridWidth = 30f;

    [Tooltip("Total length of the search area (meters)")]
    public float gridLength = 30f;

    [Tooltip("Distance between parallel passes based on camera frustum at H=10")]
    public float trackSpacing = 4.07f;

    // ================================================================
    // VISIBILITY SETTINGS
    // ================================================================

    [Header("Visibility / Raycasting")]
    [Tooltip("Minimum number of visible body-part raycasts to count as 'person in photo'")]
    public int minVisiblePartsToLabel = 2;

    [Tooltip(
        "Partial name strings to find body-part bones in the ragdoll hierarchy.\n" +
        "Adjust to match your 'Male Athlete Better' bone names exactly.")]
    public string[] bodyPartSearchNames = new string[]
    {
        "Head", "Neck",
        "Chest", "Spine",
        "Hip",
        "Arm_Upper", "Arm_Lower", "Hand",
        "Leg_Upper", "Leg_Lower", "Foot"
    };

    [Tooltip("Padding in pixels added around the computed bounding box")]
    public float bboxPaddingPx = 12f;

    // ================================================================
    // PRIVATE STATE
    // ================================================================

    private int     currentScene    = 0;
    private int     totalPhotos     = 0;
    private int     totalWithPerson = 0;

    private GameObject spawnedRagdoll;
    private GameObject spawnedDrone;
    private Camera     droneOrbitCam;

    private string imagesFolder;
    private string labelsFolder;

    // ================================================================
    // UNITY LIFECYCLE
    // ================================================================

    void Start()
    {
        // ── Setup output directories ──────────────────────────────
        imagesFolder = Path.Combine(datasetRootPath, "images");
        labelsFolder = Path.Combine(datasetRootPath, "labels");
        Directory.CreateDirectory(imagesFolder);
        Directory.CreateDirectory(labelsFolder);

        // ── Write YOLO data.yaml ──────────────────────────────────
        string yaml =
            $"path: {datasetRootPath.Replace('\\', '/')}\n" +
            "train: images\n" +
            "val: images\n" +
            "nc: 1\n" +
            "names: ['person']\n";
        File.WriteAllText(Path.Combine(datasetRootPath, "data.yaml"), yaml);
        File.WriteAllText(Path.Combine(datasetRootPath, "classes.txt"), "person");

        Debug.Log($"[DatasetGen] Output: {datasetRootPath}");
        Debug.Log($"[DatasetGen] Scenes: {totalScenes} | Photos/scene: {photosPerScene}");

        StartCoroutine(RunAllScenes());
    }

    // ================================================================
    // MAIN PIPELINE — ALL SCENES
    // ================================================================

    IEnumerator RunAllScenes()
    {
        while (currentScene < totalScenes)
        {
            Debug.Log($"[DatasetGen] ══════ Scene {currentScene + 1}/{totalScenes} ══════");

            bool sceneOk = false;
            int  attempts = 0;

            while (!sceneOk && attempts < 8)
            {
                attempts++;
                yield return StartCoroutine(RunSingleScene(ok => sceneOk = ok));

                if (!sceneOk)
                {
                    Debug.LogWarning($"[DatasetGen] Scene {currentScene + 1} attempt {attempts} failed — retrying.");
                    CleanupScene();
                    yield return new WaitForSeconds(0.1f);
                }
            }

            if (!sceneOk)
                Debug.LogError($"[DatasetGen] Scene {currentScene + 1} failed after {attempts} attempts — skipped.");

            currentScene++;
        }

        // ── Final summary ─────────────────────────────────────────
        int withoutPerson = totalPhotos - totalWithPerson;
        Debug.Log("═══════════════════════════════════════════");
        Debug.Log($"[DatasetGen] DATASET COMPLETE");
        Debug.Log($"[DatasetGen] Total photos   : {totalPhotos}");
        Debug.Log($"[DatasetGen] With person    : {totalWithPerson}");
        Debug.Log($"[DatasetGen] Without person : {withoutPerson}");
        Debug.Log($"[DatasetGen] Saved to       : {datasetRootPath}");
        Debug.Log("═══════════════════════════════════════════");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // ================================================================
    // SINGLE SCENE PIPELINE
    // ================================================================

    IEnumerator RunSingleScene(Action<bool> result)
    {
        landscape.seed = UnityEngine.Random.Range(0, 999999);
        landscape.Generate();
        yield return null;

        Vector3 spawnPos;
        bool foundGround = FindSpawnPosition(out spawnPos);

        if (!foundGround)
        {
            result(false);
            yield break;
        }

        Quaternion randomYaw = Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0);
        spawnedRagdoll = Instantiate(ragdollPrefab, spawnPos, randomYaw);

        bool settled = false;
        bool fellThrough = false;
        yield return StartCoroutine(WaitForSettle((s, f) => { settled = s; fellThrough = f; }));

        if (fellThrough)
        {
            result(false);
            yield break;
        }

        float finalY = spawnedRagdoll.transform.position.y;
        if (finalY > maxAllowedY)
        {
            result(false);
            yield break;
        }

        Vector3 droneStart = GetRagdollCenter() + new Vector3(0f, flightHeight, 0f);
        spawnedDrone = Instantiate(dronePrefab, droneStart, Quaternion.identity);

        DroneController dc = spawnedDrone.GetComponentInChildren<DroneController>();
        if (dc != null) dc.enabled = false;

        DroneCamera droneCapture = spawnedDrone.GetComponentInChildren<DroneCamera>();
        if (droneCapture != null) droneCapture.enabled = false;

        foreach (Rigidbody rb in spawnedDrone.GetComponentsInChildren<Rigidbody>())
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        foreach (Collider col in spawnedDrone.GetComponentsInChildren<Collider>())
        {
            col.enabled = false;
        }

        droneOrbitCam = spawnedDrone.GetComponentInChildren<Camera>();
        if (droneOrbitCam == null)
        {
            result(false);
            yield break;
        }

        // CRITICAL FIX: Lock the camera aspect ratio to match your dataset photos.
        // This prevents Field of View calculations from using your wide editor screen.
        droneOrbitCam.aspect = (float)photoWidth / (float)photoHeight;

        yield return new WaitForSeconds(0.2f);

        yield return StartCoroutine(FlyGridAndCapture());

        CleanupScene();
        result(true);
    }

    // ================================================================
    // WAIT FOR RAGDOLL TO SETTLE
    // ================================================================

    IEnumerator WaitForSettle(Action<bool, bool> onComplete)
    {
        bool _settled     = false;
        bool _fellThrough = false;

        float timer = 0f;

        while (timer < maxSettleTime)
        {
            timer += Time.deltaTime;

            if (spawnedRagdoll == null) break;

            float rootY = spawnedRagdoll.transform.position.y;

            // Fall-through check
            if (rootY < fallThroughYThreshold)
            {
                _fellThrough = true;
                break;
            }

            // Velocity check — all child rigidbodies
            Rigidbody[] rbs = spawnedRagdoll.GetComponentsInChildren<Rigidbody>();
            float maxVel = 0f;
            foreach (var rb in rbs)
                maxVel = Mathf.Max(maxVel, rb.linearVelocity.magnitude); // Використовуємо .velocity для сумісності

            // Require 1 second minimum before declaring settled (initial drop)
            if (maxVel < settleVelocityThreshold && timer > 1.0f)
            {
                _settled = true;
                break;
            }

            yield return null;
        }

        onComplete?.Invoke(_settled, _fellThrough);
    }

    // ================================================================
    // DRONE SECTOR FLIGHT & CAPTURE
    // ================================================================

    IEnumerator FlyGridAndCapture()
    {
        Vector3 center = GetRagdollCenter();
        float startX = center.x - gridWidth / 2f;
        float endX = center.x + gridWidth / 2f;
        float startZ = center.z + gridLength / 2f;

        // Calculates the required number of parallel sweeps based on grid length and optimal track spacing
        int numTracks = Mathf.CeilToInt(gridLength / trackSpacing) + 1;
        int shotsPerTrack = Mathf.Max(1, photosPerScene / numTracks);
        float stepX = gridWidth / shotsPerTrack;

        int photoCount = 0;

        // Variables for smooth terrain following
        float smoothedY = 0f;
        bool isFirstPoint = true;

        for (int track = 0; track < numTracks; track++)
        {
            if (photoCount >= photosPerScene) break;

            float currentZ = startZ - (track * trackSpacing);
            bool movingRight = (track % 2 == 0);

            float currentYaw = movingRight ? 90f : -90f;

            for (int step = 0; step <= shotsPerTrack; step++)
            {
                if (photoCount >= photosPerScene) break;

                float currentX = movingRight ? 
                    (startX + step * stepX) : 
                    (endX - step * stepX);

                // 1. Raycast from the sky downward to find the true terrain/canopy height
                // We start well above the maximum possible landscape height
                float skyY = landscape.heightMultiplier * 2f + 200f; 
                Vector3 rayOrigin = new Vector3(currentX, skyY, currentZ);
                float targetY = center.y + flightHeight; // Fallback

                if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, skyY * 2f))
                {
                    // Maintain exact 'flightHeight' AGL (Above Ground Level) over terrain/trees
                    targetY = hit.point.y + flightHeight;
                }

                // 2. Apply smoothing to avoid violent camera snaps over individual trees/rocks
                if (isFirstPoint)
                {
                    smoothedY = targetY;
                    isFirstPoint = false;
                }
                else
                {
                    // Drone climbs aggressively to avoid crashing into sudden obstacles, 
                    // but descends smoothly to keep footage stable.
                    if (targetY > smoothedY)
                        smoothedY = Mathf.Lerp(smoothedY, targetY, 0.7f);
                    else
                        smoothedY = Mathf.Lerp(smoothedY, targetY, 0.2f);
                }

                Vector3 desiredPos = new Vector3(currentX, smoothedY, currentZ);
                spawnedDrone.transform.position = desiredPos;
                
                // Forces the camera to look straight down (Nadir view, pitch = 90) while aligning yaw with flight direction
                droneOrbitCam.transform.rotation = Quaternion.Euler(90f, currentYaw, 0f);

                yield return new WaitForEndOfFrame();
                yield return StartCoroutine(CaptureFrame(photoCount));
                
                photoCount++;
            }
        }
    }

    // ================================================================
    // CAPTURE FRAME  →  VISIBILITY CHECK  →  SAVE
    // ================================================================

    IEnumerator CaptureFrame(int frameIndex)
    {
        yield return new WaitForEndOfFrame();

        // ── Render to off-screen texture ──────────────────────────
        RenderTexture rt = new RenderTexture(photoWidth, photoHeight, 24, RenderTextureFormat.ARGB32);
        
        // 1. Assign texture to camera. This forces the camera's aspect ratio to match the texture
        droneOrbitCam.targetTexture = rt;
        droneOrbitCam.Render();

        // 2. Read pixels
        RenderTexture.active = rt;
        Texture2D img = new Texture2D(photoWidth, photoHeight, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, photoWidth, photoHeight), 0, 0);
        img.Apply();
        RenderTexture.active = null;

        // 3. Visibility check via raycasts
        // CRITICAL FIX: We MUST do this while targetTexture is still assigned! 
        // Otherwise, the camera reverts to your Unity Editor screen aspect ratio (e.g., 16:9), 
        // which shifts the bounding boxes and causes false positives for objects that are cropped out.
        CheckVisibility(droneOrbitCam, out bool personVisible, out Rect yoloBBox);

        // 4. Cleanup camera and texture
        droneOrbitCam.targetTexture = null;
        rt.Release();
        Destroy(rt);

        // ── File naming ───────────────────────────────────────────
        string scenePad = currentScene.ToString("D3");
        string framePad = frameIndex.ToString("D4");
        string imgName  = $"scene{scenePad}_frame{framePad}.jpg";
        string imgPath  = Path.Combine(imagesFolder, imgName);
        string lblPath  = Path.Combine(labelsFolder, Path.ChangeExtension(imgName, ".txt"));

        // ── Save image ────────────────────────────────────────────
        try
        {
            byte[] bytes = img.EncodeToJPG(92);
            File.WriteAllBytes(imgPath, bytes);
        }
        catch (Exception e)
        {
            Debug.LogError($"[DatasetGen] Failed to save image: {e.Message}");
        }
        finally
        {
            Destroy(img);
        }

        // ── Save YOLO label ───────────────────────────────────────
        if (personVisible)
        {
            // Format strictly with '.' decimals to prevent YOLO parsing errors
            string line = string.Format(System.Globalization.CultureInfo.InvariantCulture, "0 {0:F6} {1:F6} {2:F6} {3:F6}",
                yoloBBox.x, yoloBBox.y, yoloBBox.width, yoloBBox.height);
            File.WriteAllText(lblPath, line + "\n");

            totalWithPerson++;
            Debug.Log($"<color=green>[DatasetGen] {imgName}  PERSON  bbox=({yoloBBox.x:F3},{yoloBBox.y:F3},{yoloBBox.width:F3},{yoloBBox.height:F3})</color>");
        }
        else
        {
            File.WriteAllText(lblPath, ""); // background sample
            Debug.Log($"<color=grey>[DatasetGen] {imgName}  background</color>");
        }

        totalPhotos++;
    }
    
    // ================================================================
    // VISIBILITY CHECK  —  CORE LOGIC
    //
    // For each body-part bone found in the ragdoll:
    //   1. Project to viewport (frustum check)
    //   2. Raycast from camera toward bone
    //      • If ray is unobstructed OR first hit is part of the ragdoll → VISIBLE
    //      • If blocked by terrain/tree → NOT VISIBLE
    //
    // Bounding box is computed from ALL in-frustum projections (visible or not),
    // matching how a human annotator would draw a box around a partially-hidden person.
    // The person is only LABELLED if minVisiblePartsToLabel bones pass raycast.
    // ================================================================

    void CheckVisibility(Camera cam,
        out bool personVisible,
        out Rect yoloBBox)
    {
        personVisible = false;
        yoloBBox      = Rect.zero;

        if (spawnedRagdoll == null || cam == null) return;

        List<Transform> bodyParts = GetBodyPartTransforms();
        if (bodyParts.Count == 0)
        {
            Debug.LogWarning("[DatasetGen] No body parts found on ragdoll! Check bodyPartSearchNames.");
            return;
        }

        // Pixel coordinates of all in-frustum parts (for bbox)
        List<Vector2> inFrustumPixels = new List<Vector2>();
        int visibleCount = 0;

        foreach (Transform bp in bodyParts)
        {
            if (bp == null) continue;

            Vector3 worldPos    = bp.position;
            Vector3 viewportPos = cam.WorldToViewportPoint(worldPos);

            // Behind camera?
            if (viewportPos.z <= 0f) continue;

            // Outside field of view?
            if (viewportPos.x < 0f || viewportPos.x > 1f ||
                viewportPos.y < 0f || viewportPos.y > 1f) continue;

            // Convert viewport → pixel  (Unity viewport Y is bottom-up → flip for image)
            Vector2 pixel = new Vector2(
                viewportPos.x * photoWidth,
                (1f - viewportPos.y) * photoHeight   // ← flip Y axis
            );
            inFrustumPixels.Add(pixel);

            // ── Raycast occlusion test ──────────────────────────
            Vector3 dir      = worldPos - cam.transform.position;
            float   distance = dir.magnitude;
            Ray     ray      = new Ray(cam.transform.position, dir.normalized);

            bool partVisible = true;

            RaycastHit hit;
            // Stop 5 cm before the target so we don't accidentally miss a thin ragdoll collider
            if (Physics.Raycast(ray, out hit, distance - 0.05f))
            {
                // Hit something — is it part of the ragdoll?
                if (!IsRagdollDescendant(hit.collider.gameObject))
                    partVisible = false;   // blocked by obstacle (terrain/tree/rock)
            }

            if (partVisible)
                visibleCount++;
        }

        // ── Decision ─────────────────────────────────────────────
        personVisible = (visibleCount >= minVisiblePartsToLabel);

        if (inFrustumPixels.Count == 0) return;

        // ── Bounding box from ALL in-frustum projections ─────────
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        foreach (Vector2 p in inFrustumPixels)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }

        // Add padding, clamp to image bounds
        minX = Mathf.Max(0f,          minX - bboxPaddingPx);
        maxX = Mathf.Min(photoWidth,  maxX + bboxPaddingPx);
        minY = Mathf.Max(0f,          minY - bboxPaddingPx);
        maxY = Mathf.Min(photoHeight, maxY + bboxPaddingPx);

        // Convert to YOLO format: center_x, center_y, width, height (0–1 normalized)
        float cx = ((minX + maxX) * 0.5f) / photoWidth;
        float cy = ((minY + maxY) * 0.5f) / photoHeight;
        float bw = (maxX - minX)          / photoWidth;
        float bh = (maxY - minY)          / photoHeight;

        // Clamp all to [0, 1]
        cx = Mathf.Clamp01(cx);
        cy = Mathf.Clamp01(cy);
        bw = Mathf.Clamp(bw, 0.001f, 1f);
        bh = Mathf.Clamp(bh, 0.001f, 1f);

        yoloBBox = new Rect(cx, cy, bw, bh);
    }

    // ================================================================
    // HELPERS
    // ================================================================

    /// <summary>
    /// Finds all bone transforms in the ragdoll whose name contains any of
    /// the bodyPartSearchNames strings (case-insensitive).
    /// Falls back to ALL child Rigidbody transforms if nothing is found.
    /// </summary>
    List<Transform> GetBodyPartTransforms()
    {
        List<Transform> result = new List<Transform>();
        if (spawnedRagdoll == null) return result;

        Transform[] all = spawnedRagdoll.GetComponentsInChildren<Transform>(true);
        foreach (Transform t in all)
        {
            foreach (string partName in bodyPartSearchNames)
            {
                if (t.name.IndexOf(partName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(t);
                    break;
                }
            }
        }

        // Fallback
        if (result.Count == 0)
        {
            Debug.LogWarning("[DatasetGen] No named body parts found — falling back to all Rigidbodies.");
            foreach (Rigidbody rb in spawnedRagdoll.GetComponentsInChildren<Rigidbody>())
                result.Add(rb.transform);
        }

        return result;
    }

    /// <summary>
    /// Checks if a GameObject is the ragdoll itself or any of its descendants.
    /// </summary>
    bool IsRagdollDescendant(GameObject go)
    {
        if (spawnedRagdoll == null || go == null) return false;
        Transform t = go.transform;
        while (t != null)
        {
            if (t.gameObject == spawnedRagdoll) return true;
            t = t.parent;
        }
        return false;
    }

    /// <summary>
    /// Computes the averaged world position of all Rigidbodies in the ragdoll.
    /// Used as the drone's look-at target.
    /// </summary>
    Vector3 GetRagdollCenter()
    {
        if (spawnedRagdoll == null) return Vector3.zero;
        Rigidbody[] rbs = spawnedRagdoll.GetComponentsInChildren<Rigidbody>();
        if (rbs.Length == 0) return spawnedRagdoll.transform.position;

        Vector3 sum = Vector3.zero;
        foreach (Rigidbody rb in rbs) sum += rb.position;
        return sum / rbs.Length;
    }

    /// <summary>
    /// Raycasts downward from a high position to find a valid spawn point
    /// on the terrain surface.
    /// </summary>
    bool FindSpawnPosition(out Vector3 spawnPos)
    {
        spawnPos = Vector3.zero;

        // Increased margin from 15f to 80f to prevent spawning near the unrendered map edges
        float safeMargin = 80f;
        float halfW = landscape.mapWidth  * 0.5f - safeMargin;
        float halfL = landscape.mapLength * 0.5f - safeMargin;

        for (int attempt = 0; attempt < 20; attempt++)
        {
            float x = UnityEngine.Random.Range(-halfW, halfW);
            float z = UnityEngine.Random.Range(-halfL, halfL);

            float castOriginY = landscape.heightMultiplier * 2f + 100f;
            Ray ray = new Ray(new Vector3(x, castOriginY, z), Vector3.down);

            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, castOriginY * 2f))
            {
                spawnPos = new Vector3(x, hit.point.y + spawnHeightAboveTerrain, z);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Destroys spawned ragdoll and drone between scenes.
    /// </summary>
    void CleanupScene()
    {
        if (spawnedRagdoll != null) { Destroy(spawnedRagdoll); spawnedRagdoll = null; }
        if (spawnedDrone   != null) { Destroy(spawnedDrone);   spawnedDrone   = null; }
        droneOrbitCam = null;
    }
}