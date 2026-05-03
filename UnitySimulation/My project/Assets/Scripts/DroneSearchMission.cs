using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;

// Requires: LowPolyLandscape.cs, DroneController.cs, DroneCamera.cs in project
// Python server (server_filebased.py) must be running BEFORE Play is pressed.

[RequireComponent(typeof(MonoBehaviour))]
public class DroneSearchMission : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════
    // SCENE REFERENCES
    // ════════════════════════════════════════════════════════════════

    [Header("Scene References")]
    public LowPolyLandscape landscape;
    public GameObject ragdollPrefab;
    public GameObject dronePrefab;

    // ════════════════════════════════════════════════════════════════
    // MISSION SETTINGS
    // ════════════════════════════════════════════════════════════════

    [Header("Mission Settings")]
    [Tooltip("Number of independent search iterations to run")]
    public int totalIterations = 10;

    [Tooltip("Frames per second the drone captures photos (supports fractional: 0.5 = 1 photo every 2 s)")]
    public float captureFPS = 1.0f;

    [Tooltip("Photo resolution sent to the Python server")]
    public int photoWidth  = 1024;
    public int photoHeight = 1024;

    [Tooltip("Stop sweep as soon as a True-Positive is confirmed (faster iterations)")]
    public bool stopOnFirstConfirmedFind = false;

    [Tooltip("Max seconds to wait for a JSON result from the Python server per frame")]
    public float serverTimeoutSeconds = 8f;

    // ════════════════════════════════════════════════════════════════
    // PATHS  (mirror your server_filebased.py constants)
    // ════════════════════════════════════════════════════════════════

    [Header("Exchange Paths")]
    public string exchangeRoot = @"D:\Unity Projects\My project\Assets\ServerExchange";
    public string statsOutputFolder = @"D:\Unity Projects\My project\Assets\MissionStats";

    // ════════════════════════════════════════════════════════════════
    // DRONE SWEEP (LAWNMOWER) SETTINGS
    // ════════════════════════════════════════════════════════════════

    [Header("Drone Sweep Path")]
    [Tooltip("Flight altitude above terrain surface")]
    public float droneAltitude = 25f;

    [Tooltip("Width of each sweep strip (meters). Should cover camera FOV footprint at droneAltitude.")]
    public float sweepStripWidth = 30f;

    [Tooltip("Lateral move speed between waypoints (m/s)")]
    public float droneMoveSpeed = 12f;

    [Tooltip("Margin from map edge where the sweep starts/ends (meters)")]
    public float mapEdgeMargin = 15f;

    // ════════════════════════════════════════════════════════════════
    // RAGDOLL / SPAWN SETTINGS  (copied from DatasetGenerator)
    // ════════════════════════════════════════════════════════════════

    [Header("Ragdoll Settings")]
    public float spawnHeightAboveTerrain = 5f;
    public float maxSettleTime           = 6f;
    public float settleVelocityThreshold = 0.08f;
    public float fallThroughYThreshold   = -30f;

    // ════════════════════════════════════════════════════════════════
    // VERIFICATION SETTINGS
    // ════════════════════════════════════════════════════════════════

    [Header("True-Positive Verification")]
    [Tooltip("Partial bone name strings matching your ragdoll skeleton")]
    public string[] bodyPartSearchNames = new string[]
    {
        "Head", "Neck", "Chest", "Spine", "Hips",
        "UpperArm", "LowerArm", "Hand",
        "Thigh", "Calf", "Foot"
    };

    [Tooltip("Minimum visible bone raycasts to confirm a True Positive")]
    public int minVisiblePartsForTP = 2;

    [Tooltip("Extra distance check: if drone is further than this from ragdoll, skip verification (too far)")]
    public float maxVerificationDistance = 120f;

    // ════════════════════════════════════════════════════════════════
    // PRIVATE STATE
    // ════════════════════════════════════════════════════════════════

    private string inputDir;
    private string outputDir;

    private GameObject spawnedRagdoll;
    private GameObject spawnedDrone;
    private Camera     droneCam;

    // Per-frame timing
    private float captureTimer    = 0f;
    private float captureInterval = 1f; // = 1 / captureFPS, recalculated at start

    private List<IterationStats>  allStats    = new List<IterationStats>();
    private GlobalStats           globalStats = new GlobalStats();

    // ════════════════════════════════════════════════════════════════
    // DATA STRUCTURES
    // ════════════════════════════════════════════════════════════════

    [Serializable]
    public class IterationStats
    {
        public int    iterationIndex;
        public int    landscapeSeed;
        public int    totalPhotosTaken;
        public int    serverDetections;    // raw detections returned by YOLO
        public int    truePositives;       // confirmed by raycast
        public int    falsePositives;      // YOLO fired but raycast says no
        public int    missedDetections;    // person visible (raycast) but YOLO missed
        public bool   personFoundByDrone;  // at least one TP
        public int    firstTPPhotoIndex;   // which photo index produced first TP (-1 = never)
        public float  firstTPDistanceM;    // drone-to-ragdoll distance at first TP (m)
        public float  sweepCompletionPct;  // 0-100%: how much of map was covered when found
        public float  iterationDurationSec;
        public string ragdollWorldPos;
        public string notes;               // e.g. "settle_timeout", "fell_through"
    }

    [Serializable]
    public class GlobalStats
    {
        public int   totalIterations;
        public int   successfulIterations;   // those that completed without error
        public int   iterationsPersonFound;
        public int   totalPhotos;
        public int   totalServerDetections;
        public int   totalTruePositives;
        public int   totalFalsePositives;
        public int   totalMissedDetections;
        public float avgPhotosToFirstTP;
        public float avgFirstTPDistanceM;
        public float precisionPct;           // TP / (TP + FP) * 100
        public float recallPct;              // TP / (TP + FN) * 100
        public float f1Score;
        public string generatedAt;
    }

    // Lightweight JSON result from server
    [Serializable]
    private class ServerResult
    {
        public string   status;
        public DetObj[] objects;
    }

    [Serializable]
    private class DetObj
    {
        public string name;    // class name — server uses "class" but Unity's JsonUtility can't use reserved words
        public float  conf;
        public DetBox box;
    }

    [Serializable]
    private class DetBox
    {
        public int x, y, w, h;
    }

    // ════════════════════════════════════════════════════════════════
    // UNITY LIFECYCLE
    // ════════════════════════════════════════════════════════════════

    void Start()
    {
        captureInterval = 1f / Mathf.Max(captureFPS, 0.001f);

        inputDir  = Path.Combine(exchangeRoot, "input");
        outputDir = Path.Combine(exchangeRoot, "output");
        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(statsOutputFolder);

        // Clear stale files from a previous run
        ClearDirectory(inputDir);
        ClearDirectory(outputDir);

        Debug.Log($"[Mission] Starting {totalIterations} iterations. captureFPS={captureFPS} → interval={captureInterval:F2}s");
        StartCoroutine(RunAllIterations());
    }

    // ════════════════════════════════════════════════════════════════
    // TOP-LEVEL LOOP
    // ════════════════════════════════════════════════════════════════

    IEnumerator RunAllIterations()
    {
        for (int i = 0; i < totalIterations; i++)
        {
            Debug.Log($"[Mission] ══════════ Iteration {i + 1}/{totalIterations} ══════════");

            IterationStats stats = new IterationStats
            {
                iterationIndex  = i,
                firstTPPhotoIndex = -1,
                firstTPDistanceM  = -1f
            };

            bool ok = false;
            int  retries = 0;

            while (!ok && retries < 6)
            {
                retries++;
                yield return StartCoroutine(RunIteration(stats, result => ok = result));

                if (!ok)
                {
                    Debug.LogWarning($"[Mission] Iteration {i + 1} attempt {retries} failed, retrying…");
                    CleanupIteration();
                    yield return new WaitForSeconds(0.15f);
                }
            }

            if (!ok) stats.notes += " | FAILED_ALL_RETRIES";

            allStats.Add(stats);
            CleanupIteration();

            Debug.Log($"[Mission] Iteration {i + 1} done. TP={stats.truePositives} FP={stats.falsePositives} Photos={stats.totalPhotosTaken}");
            yield return new WaitForSeconds(0.2f);
        }

        FinalizeAndSaveStats();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // ════════════════════════════════════════════════════════════════
    // SINGLE ITERATION
    // ════════════════════════════════════════════════════════════════

    IEnumerator RunIteration(IterationStats stats, Action<bool> result)
    {
        float startTime = Time.time;

        // ── 1. Generate landscape ─────────────────────────────────
        int seed = UnityEngine.Random.Range(0, 999999);
        landscape.seed = seed;
        landscape.Generate();
        stats.landscapeSeed = seed;
        yield return null;

        // ── 2. Spawn ragdoll ───────────────────────────────────────
        if (!FindSpawnPosition(out Vector3 spawnPos))
        {
            stats.notes += " | NO_GROUND";
            result(false); yield break;
        }

        Quaternion rndYaw = Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0);
        spawnedRagdoll = Instantiate(ragdollPrefab, spawnPos, rndYaw);
        stats.ragdollWorldPos = spawnPos.ToString("F1");

        yield return StartCoroutine(WaitForSettle(stats));

        if (spawnedRagdoll == null || spawnedRagdoll.transform.position.y < fallThroughYThreshold)
        {
            stats.notes += " | FELL_THROUGH";
            result(false); yield break;
        }

        Debug.Log($"[Mission] Ragdoll settled at {spawnedRagdoll.transform.position:F1}");

        // ── 3. Spawn drone at map edge ─────────────────────────────
        Vector3 droneStart = GetDroneStartPosition();
        spawnedDrone = Instantiate(dronePrefab, droneStart, Quaternion.identity);
        DisableDroneAutonomous(spawnedDrone);

        droneCam = spawnedDrone.GetComponentInChildren<Camera>();
        if (droneCam == null)
        {
            stats.notes += " | NO_DRONE_CAM";
            result(false); yield break;
        }

        // Lock aspect ratio so viewport math works perfectly for square images
        droneCam.aspect = (float)photoWidth / (float)photoHeight;

        yield return new WaitForSeconds(0.1f);

        // ── 4. Execute lawnmower sweep ────────────────────────────
        yield return StartCoroutine(SweepMap(stats));

        stats.iterationDurationSec = Time.time - startTime;
        result(true);
    }

    // ════════════════════════════════════════════════════════════════
    // LAWNMOWER SWEEP
    // ════════════════════════════════════════════════════════════════

    IEnumerator SweepMap(IterationStats stats)
    {
        List<Vector3> waypoints = BuildSweepWaypoints();
        if (waypoints.Count == 0) yield break;

        int   totalWaypoints    = waypoints.Count;
        int   waypointIndex     = 0;
        float timeSinceCapture  = captureInterval; // capture immediately at first waypoint

        bool missionDone = false;

        while (waypointIndex < totalWaypoints && !missionDone)
        {
            Vector3 target = waypoints[waypointIndex];

            // ── Move drone to next waypoint ────────────────────────
            while (Vector3.Distance(spawnedDrone.transform.position, target) > 0.5f)
            {
                spawnedDrone.transform.position = Vector3.MoveTowards(
                    spawnedDrone.transform.position,
                    target,
                    droneMoveSpeed * Time.deltaTime
                );

                // Drone always looks forward (along movement direction)
                Vector3 dir = (target - spawnedDrone.transform.position);
                if (dir.sqrMagnitude > 0.01f)
                    spawnedDrone.transform.rotation = Quaternion.LookRotation(dir.normalized);

                // Capture tick while moving
                timeSinceCapture += Time.deltaTime;
                if (timeSinceCapture >= captureInterval)
                {
                    timeSinceCapture = 0f;
                    yield return StartCoroutine(CaptureAndEvaluate(stats));

                    if (stats.personFoundByDrone && stopOnFirstConfirmedFind)
                    {
                        float pct = (float)waypointIndex / totalWaypoints * 100f;
                        stats.sweepCompletionPct = pct;
                        missionDone = true;
                        Debug.Log($"[Mission] Person confirmed! Stopping sweep at {pct:F0}% completion.");
                        break;
                    }
                }

                yield return null;
            }

            waypointIndex++;
        }

        if (!missionDone)
            stats.sweepCompletionPct = 100f;
    }

    // ════════════════════════════════════════════════════════════════
    // BUILD LAWNMOWER WAYPOINT LIST
    // ════════════════════════════════════════════════════════════════
    // Pattern:  start at (-halfW, altitude, -halfL)
    //           go to    ( halfW, altitude, -halfL)   ← strip 0, left→right
    //           shift Z by sweepStripWidth
    //           go to    (-halfW, altitude, -halfL+strip)  ← strip 1, right→left
    //           … until Z > halfL

    List<Vector3> BuildSweepWaypoints()
    {
        List<Vector3> wps = new List<Vector3>();

        float halfW = landscape.mapWidth  * 0.5f - mapEdgeMargin;
        float halfL = landscape.mapLength * 0.5f - mapEdgeMargin;

        float startZ    = -halfL;
        float endZ      =  halfL;
        bool  leftToRight = true;

        for (float z = startZ; z <= endZ + sweepStripWidth; z += sweepStripWidth)
        {
            float clampedZ = Mathf.Min(z, endZ);

            // Sample terrain height at this Z strip (center X)
            float groundY  = SampleGroundY(0f, clampedZ);
            float altitude = groundY + droneAltitude;

            Vector3 wp1 = new Vector3(leftToRight ? -halfW : halfW, altitude, clampedZ);
            Vector3 wp2 = new Vector3(leftToRight ?  halfW : -halfW, altitude, clampedZ);

            wps.Add(wp1);
            wps.Add(wp2);

            leftToRight = !leftToRight;
        }

        Debug.Log($"[Mission] Sweep: {wps.Count} waypoints, {wps.Count / 2} strips, stripW={sweepStripWidth}m");
        return wps;
    }

    // ════════════════════════════════════════════════════════════════
    // CAPTURE ONE FRAME  →  SERVER  →  VERIFY
    // ════════════════════════════════════════════════════════════════

    IEnumerator CaptureAndEvaluate(IterationStats stats)
    {
        yield return new WaitForEndOfFrame();
 
        // ── Render to texture ──────────────────────────────────────
        RenderTexture rt = new RenderTexture(photoWidth, photoHeight, 24, RenderTextureFormat.ARGB32);
        droneCam.targetTexture = rt;
        droneCam.Render();
        droneCam.targetTexture = null;
 
        RenderTexture.active = rt;
        Texture2D img = new Texture2D(photoWidth, photoHeight, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, photoWidth, photoHeight), 0, 0);
        img.Apply();
        RenderTexture.active = null;
        rt.Release();
        Destroy(rt);
 
        // ── Write to server input folder ──────────────────────────
        string fname = $"mission_{DateTime.Now.Ticks}.jpg";
        string imgPath = Path.Combine(inputDir, fname);
        File.WriteAllBytes(imgPath, img.EncodeToJPG(90));
        Destroy(img);
 
        stats.totalPhotosTaken++;
 
        // ── Know ground truth for this frame BEFORE server responds ─
        bool ragdollVisibleNow = IsRagdollVisibleFromCamera(droneCam, out int visibleParts);
 
        // ── Wait for server JSON ───────────────────────────────────
        string jsonName = Path.GetFileNameWithoutExtension(fname) + ".json";
        string jsonPath = Path.Combine(outputDir, jsonName);
 
        float elapsed = 0f;
        while (!File.Exists(jsonPath) && elapsed < serverTimeoutSeconds)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
 
        if (!File.Exists(jsonPath))
        {
            Debug.LogWarning($"[Mission] Server timeout for {fname} — skipping evaluation.");
            stats.notes += " | SERVER_TIMEOUT";
            yield break;
        }
 
        // ── Parse JSON — safe read with FileShare to avoid sharing violation ─
        // Python may still have the file handle open while flushing.
        // Retry up to 10 times with 50 ms gaps before giving up.
        string raw = null;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using (var fs = new FileStream(jsonPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new System.IO.StreamReader(fs, System.Text.Encoding.UTF8))
                    raw = sr.ReadToEnd();
 
                if (!string.IsNullOrWhiteSpace(raw)) break; // got something — done
            }
            catch (IOException)
            {
                // File still locked — wait one frame and retry
            }
            yield return new WaitForSeconds(0.05f);
        }
 
        // Now safe to delete (Python already removed input; output is ours)
        try { File.Delete(jsonPath); } catch { /* ignore if already gone */ }
 
        // server_filebased.py uses "class" key — Unity JsonUtility can't handle it.
        // We rename the key before parsing.
        raw = raw.Replace("\"class\":", "\"name\":");
 
        ServerResult serverResult = null;
        try { serverResult = JsonUtility.FromJson<ServerResult>(raw); }
        catch (Exception e) { Debug.LogWarning($"[Mission] JSON parse error: {e.Message}"); yield break; }
 
        if (serverResult == null || serverResult.objects == null) yield break;
 
        // Filter: only person-class detections
        int personDetections = serverResult.objects
            .Count(o => o.name.ToLower().Contains("person") || o.name == "0");
 
        if (personDetections == 0)
        {
            // YOLO found nothing — was the person actually visible? → Missed detection
            if (ragdollVisibleNow)
                stats.missedDetections++;
            yield break;
        }
 
        // ── YOLO fired — verify each detection ────────────────────
        stats.serverDetections += personDetections;
 
        foreach (var det in serverResult.objects)
        {
            if (!det.name.ToLower().Contains("person") && det.name != "0") continue;
 
            bool isTP = VerifyDetection(droneCam, det);
 
            if (isTP)
            {
                stats.truePositives++;
 
                if (!stats.personFoundByDrone)
                {
                    stats.personFoundByDrone  = true;
                    stats.firstTPPhotoIndex   = stats.totalPhotosTaken;
                    stats.firstTPDistanceM    = Vector3.Distance(
                        spawnedDrone.transform.position,
                        GetRagdollCenter());
                    Debug.Log($"<color=cyan>[Mission] ✔ TRUE POSITIVE! Distance={stats.firstTPDistanceM:F1}m Photo#{stats.totalPhotosTaken}</color>");
                }
            }
            else
            {
                stats.falsePositives++;
                Debug.Log($"<color=yellow>[Mission] ✘ False positive. VisibleParts={visibleParts}</color>");
            }
        }
    }
    // ════════════════════════════════════════════════════════════════
    // VERIFY DETECTION  (two-layer: distance + raycast)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns true if the detection bounding box corresponds to the actual ragdoll.
    /// Layer 1 — Distance gate: if drone is too far, YOLO result is untrustworthy.
    /// Layer 2 — Raycast: cast from camera to each body part.
    /// </summary>
    bool VerifyDetection(Camera cam, DetObj det)
    {
        if (spawnedRagdoll == null || cam == null) return false;

        // ── Layer 1: distance gate ────────────────────────────────
        float dist = Vector3.Distance(cam.transform.position, GetRagdollCenter());
        if (dist > maxVerificationDistance) return false;

        // ── Layer 2: bounding box overlap check ───────────────────
        // Project ragdoll body parts to viewport and compare with YOLO bbox.
        // If the ragdoll projects into or near the YOLO bbox → plausible TP.
        Rect detPixelRect = new Rect(det.box.x, det.box.y, det.box.w, det.box.h);

        List<Transform> parts = GetBodyPartTransforms();
        int insideBbox  = 0;
        int visibleRays = 0;

        foreach (Transform bp in parts)
        {
            if (bp == null) continue;
            Vector3 vp = cam.WorldToViewportPoint(bp.position);
            if (vp.z <= 0f) continue;

            // Pixel position (Y flipped for image-space)
            Vector2 px = new Vector2(vp.x * photoWidth, (1f - vp.y) * photoHeight);

            // Is this bone pixel inside the YOLO bounding box? (with 20px tolerance)
            Rect expandedRect = new Rect(
                detPixelRect.x - 20, detPixelRect.y - 20,
                detPixelRect.width + 40, detPixelRect.height + 40);

            if (expandedRect.Contains(px))
                insideBbox++;

            // Raycast occlusion
            Vector3 dir  = bp.position - cam.transform.position;
            Ray     ray  = new Ray(cam.transform.position, dir.normalized);
            if (Physics.Raycast(ray, out RaycastHit hit, dir.magnitude - 0.05f))
            {
                if (IsRagdollDescendant(hit.collider.gameObject))
                    visibleRays++;
            }
            else
            {
                visibleRays++; // unobstructed = visible
            }
        }

        // TP requires: at least minVisiblePartsForTP bones visible AND at least 1 inside bbox
        return (visibleRays >= minVisiblePartsForTP) && (insideBbox >= 1);
    }

    /// <summary>
    /// Quick check: is the ragdoll visible from this camera without YOLO context?
    /// Used for ground-truth missed-detection counting.
    /// </summary>
    bool IsRagdollVisibleFromCamera(Camera cam, out int visibleCount)
    {
        visibleCount = 0;
        if (spawnedRagdoll == null || cam == null) return false;

        foreach (Transform bp in GetBodyPartTransforms())
        {
            if (bp == null) continue;
            Vector3 vp = cam.WorldToViewportPoint(bp.position);
            if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) continue;

            Vector3 dir = bp.position - cam.transform.position;
            Ray     ray = new Ray(cam.transform.position, dir.normalized);

            bool vis = true;
            if (Physics.Raycast(ray, out RaycastHit hit, dir.magnitude - 0.05f))
                vis = IsRagdollDescendant(hit.collider.gameObject);

            if (vis) visibleCount++;
        }

        return visibleCount >= minVisiblePartsForTP;
    }

    // ════════════════════════════════════════════════════════════════
    // STATISTICS  —  FINALIZE & SAVE
    // ════════════════════════════════════════════════════════════════

    void FinalizeAndSaveStats()
    {
        // ── Per-iteration JSON ────────────────────────────────────
        string runId    = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string iterPath = Path.Combine(statsOutputFolder, $"iterations_{runId}.json");
        File.WriteAllText(iterPath,
            "[\n" + string.Join(",\n", allStats.Select(JsonUtility.ToJson)) + "\n]");

        // ── Global aggregation ────────────────────────────────────
        globalStats.totalIterations        = totalIterations;
        globalStats.successfulIterations   = allStats.Count;
        globalStats.iterationsPersonFound  = allStats.Count(s => s.personFoundByDrone);
        globalStats.totalPhotos            = allStats.Sum(s => s.totalPhotosTaken);
        globalStats.totalServerDetections  = allStats.Sum(s => s.serverDetections);
        globalStats.totalTruePositives     = allStats.Sum(s => s.truePositives);
        globalStats.totalFalsePositives    = allStats.Sum(s => s.falsePositives);
        globalStats.totalMissedDetections  = allStats.Sum(s => s.missedDetections);
        globalStats.generatedAt            = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // Averages (only over iterations where person was found)
        var foundIters = allStats.Where(s => s.personFoundByDrone).ToList();
        globalStats.avgPhotosToFirstTP =
            foundIters.Count > 0 ? (float)foundIters.Average(s => s.firstTPPhotoIndex) : -1f;
        globalStats.avgFirstTPDistanceM =
            foundIters.Count > 0 ? (float)foundIters.Average(s => s.firstTPDistanceM)  : -1f;

        // Precision / Recall / F1
        int tp = globalStats.totalTruePositives;
        int fp = globalStats.totalFalsePositives;
        int fn = globalStats.totalMissedDetections;

        globalStats.precisionPct = (tp + fp) > 0 ? (float)tp / (tp + fp) * 100f : 0f;
        globalStats.recallPct    = (tp + fn) > 0 ? (float)tp / (tp + fn) * 100f : 0f;

        float p = globalStats.precisionPct / 100f;
        float r = globalStats.recallPct    / 100f;
        globalStats.f1Score = (p + r) > 0 ? 2f * p * r / (p + r) : 0f;

        string globalPath = Path.Combine(statsOutputFolder, $"global_{runId}.json");
        File.WriteAllText(globalPath, JsonUtility.ToJson(globalStats, true));

        // ── CSV (for matplotlib / Excel) ──────────────────────────
        string csvPath = Path.Combine(statsOutputFolder, $"iterations_{runId}.csv");
        var csv = new System.Text.StringBuilder();
        csv.AppendLine(
            "iteration,seed,photos,server_detections,true_positives,false_positives," +
            "missed_detections,person_found,first_tp_photo,first_tp_dist_m," +
            "sweep_completion_pct,duration_sec,ragdoll_pos,notes");

        foreach (var s in allStats)
        {
            csv.AppendLine(string.Join(",",
                s.iterationIndex,
                s.landscapeSeed,
                s.totalPhotosTaken,
                s.serverDetections,
                s.truePositives,
                s.falsePositives,
                s.missedDetections,
                s.personFoundByDrone ? 1 : 0,
                s.firstTPPhotoIndex,
                s.firstTPDistanceM.ToString("F1"),
                s.sweepCompletionPct.ToString("F1"),
                s.iterationDurationSec.ToString("F1"),
                $"\"{s.ragdollWorldPos}\"",
                $"\"{s.notes?.Trim()}\""
            ));
        }
        File.WriteAllText(csvPath, csv.ToString());

        // ── Console summary ────────────────────────────────────────
        Debug.Log("═══════════════════════════════════════════");
        Debug.Log($"[Mission] MISSION COMPLETE");
        Debug.Log($"[Mission] Iterations       : {globalStats.successfulIterations}/{totalIterations}");
        Debug.Log($"[Mission] Person found     : {globalStats.iterationsPersonFound}");
        Debug.Log($"[Mission] Total photos     : {globalStats.totalPhotos}");
        Debug.Log($"[Mission] True  positives  : {tp}");
        Debug.Log($"[Mission] False positives  : {fp}");
        Debug.Log($"[Mission] Missed detections: {fn}");
        Debug.Log($"[Mission] Precision        : {globalStats.precisionPct:F1}%");
        Debug.Log($"[Mission] Recall           : {globalStats.recallPct:F1}%");
        Debug.Log($"[Mission] F1 Score         : {globalStats.f1Score:F3}");
        Debug.Log($"[Mission] Avg photos→TP    : {globalStats.avgPhotosToFirstTP:F1}");
        Debug.Log($"[Mission] Avg dist at TP   : {globalStats.avgFirstTPDistanceM:F1}m");
        Debug.Log($"[Mission] Stats saved to   : {statsOutputFolder}");
        Debug.Log("═══════════════════════════════════════════");
    }

    // ════════════════════════════════════════════════════════════════
    // HELPERS  (shared with DatasetGenerator pattern)
    // ════════════════════════════════════════════════════════════════

    IEnumerator WaitForSettle(IterationStats stats)
    {
        float timer = 0f;
        while (timer < maxSettleTime)
        {
            timer += Time.deltaTime;
            if (spawnedRagdoll == null) yield break;

            if (spawnedRagdoll.transform.position.y < fallThroughYThreshold)
            {
                stats.notes += " | FELL_THROUGH";
                yield break;
            }

            Rigidbody[] rbs = spawnedRagdoll.GetComponentsInChildren<Rigidbody>();
            float maxVel = rbs.Max(rb => rb.linearVelocity.magnitude);

            if (maxVel < settleVelocityThreshold && timer > 1.0f)
            {
                Debug.Log($"[Mission] Ragdoll settled in {timer:F1}s");
                yield break;
            }
            yield return null;
        }
        stats.notes += " | SETTLE_TIMEOUT";
    }

    bool FindSpawnPosition(out Vector3 pos)
    {
        pos = Vector3.zero;
        float halfW = landscape.mapWidth  * 0.5f - 15f;
        float halfL = landscape.mapLength * 0.5f - 15f;

        for (int i = 0; i < 20; i++)
        {
            float x = UnityEngine.Random.Range(-halfW, halfW);
            float z = UnityEngine.Random.Range(-halfL, halfL);
            float castY = landscape.heightMultiplier * 2f + 100f;

            if (Physics.Raycast(new Ray(new Vector3(x, castY, z), Vector3.down), out RaycastHit hit, castY * 2f))
            {
                pos = new Vector3(x, hit.point.y + spawnHeightAboveTerrain, z);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Drone starts at (-halfW, altitude, -halfL) — bottom-left corner of the map.
    /// </summary>
    Vector3 GetDroneStartPosition()
    {
        float x = -(landscape.mapWidth  * 0.5f - mapEdgeMargin);
        float z = -(landscape.mapLength * 0.5f - mapEdgeMargin);
        float y  = SampleGroundY(x, z) + droneAltitude;
        return new Vector3(x, y, z);
    }

    /// <summary>
    /// Raycast down at (x, z) to find terrain height. Falls back to heightMultiplier if no hit.
    /// </summary>
    float SampleGroundY(float x, float z)
    {
        float castY = landscape.heightMultiplier * 2f + 100f;
        if (Physics.Raycast(new Ray(new Vector3(x, castY, z), Vector3.down), out RaycastHit hit, castY * 2f))
            return hit.point.y;
        return landscape.heightMultiplier;
    }

    void DisableDroneAutonomous(GameObject drone)
    {
        var dc = drone.GetComponentInChildren<DroneController>();
        if (dc != null) dc.enabled = false;

        var cam = drone.GetComponentInChildren<DroneCamera>();
        if (cam != null) cam.enabled = false;

        var rb = drone.GetComponentInChildren<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
    }

    List<Transform> GetBodyPartTransforms()
    {
        var result = new List<Transform>();
        if (spawnedRagdoll == null) return result;

        foreach (Transform t in spawnedRagdoll.GetComponentsInChildren<Transform>(true))
        {
            foreach (string name in bodyPartSearchNames)
            {
                if (t.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(t);
                    break;
                }
            }
        }

        // Fallback: all rigidbodies
        if (result.Count == 0)
            foreach (Rigidbody rb in spawnedRagdoll.GetComponentsInChildren<Rigidbody>())
                result.Add(rb.transform);

        return result;
    }

    Vector3 GetRagdollCenter()
    {
        if (spawnedRagdoll == null) return Vector3.zero;
        var rbs = spawnedRagdoll.GetComponentsInChildren<Rigidbody>();
        if (rbs.Length == 0) return spawnedRagdoll.transform.position;
        Vector3 sum = Vector3.zero;
        foreach (var rb in rbs) sum += rb.position;
        return sum / rbs.Length;
    }

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

    void CleanupIteration()
    {
        if (spawnedRagdoll != null) { Destroy(spawnedRagdoll); spawnedRagdoll = null; }
        if (spawnedDrone   != null) { Destroy(spawnedDrone);   spawnedDrone   = null; }
        droneCam = null;
        ClearDirectory(inputDir);
        ClearDirectory(outputDir);
    }

    static void ClearDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (string f in Directory.GetFiles(dir)) try { File.Delete(f); } catch { }
    }
}