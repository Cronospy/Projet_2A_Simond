using UnityEngine;
using System.IO;
using System.Collections;

public class DroneCamera : MonoBehaviour
{
    // ========================================================================
    // CONSTANTS & SETTINGS
    // ========================================================================
    
    // The exact path to your ServerExchange folder
    private const string EXCHANGE_ROOT_PATH = @"D:\Unity Projects\My project\Assets\ServerExchange";
    
    [Header("Capture Settings")]
    [Tooltip("How often to take a photo (in seconds)")]
    public float captureInterval = 1.0f;
    
    [Tooltip("Resolution must match training crop size for best accuracy")]
    public int photoWidth = 1024;
    public int photoHeight = 1024;

    // ========================================================================

    private Camera droneCam;
    private RenderTexture renderTexture;
    private float timer = 0f;
    private string inputFolder;
    private string outputFolder;
    private int frameCount = 0;

    void Start()
    {
        Debug.Log("<b>[DroneCamera]</b> Initializing System...");

        // 1. Setup Paths
        inputFolder = Path.Combine(EXCHANGE_ROOT_PATH, "input");
        outputFolder = Path.Combine(EXCHANGE_ROOT_PATH, "output");

        // 2. Validate Folders
        if (!Directory.Exists(inputFolder))
        {
            Debug.LogWarning($"[DroneCamera] Input folder missing. Creating: {inputFolder}");
            Directory.CreateDirectory(inputFolder);
        }
        if (!Directory.Exists(outputFolder))
        {
            Directory.CreateDirectory(outputFolder);
        }

        Debug.Log($"[DroneCamera] Targeting Exchange Folder: {EXCHANGE_ROOT_PATH}");

        // 3. Setup Camera
        droneCam = GetComponent<Camera>();
        if (droneCam == null)
        {
            Debug.LogError("[DroneCamera] CRITICAL: No Camera component found on this object!");
            this.enabled = false;
            return;
        }

        // 4. Setup Off-screen Rendering
        // We create the texture but DO NOT assign it to targetTexture yet.
        // This keeps the camera rendering to the main screen by default.
        renderTexture = new RenderTexture(photoWidth, photoHeight, 24);
        
        Debug.Log("[DroneCamera] Initialization Complete. Ready to capture.");
    }

    void Update()
    {
        timer += Time.deltaTime;

        if (timer >= captureInterval)
        {
            StartCoroutine(CaptureAndSaveRoutine());
            timer = 0f;
        }
    }

    IEnumerator CaptureAndSaveRoutine()
    {
        // Wait for EndOfFrame to ensure all game logic is done
        yield return new WaitForEndOfFrame();

        // 1. Switch Camera to Texture (Momentarily stop rendering to screen)
        droneCam.targetTexture = renderTexture;
        
        // 2. Force Render
        droneCam.Render();

        // 3. Switch Camera back to Screen (Resume normal view)
        droneCam.targetTexture = null;

        // 4. Read Pixels from the Texture
        RenderTexture.active = renderTexture;
        Texture2D image = new Texture2D(photoWidth, photoHeight, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, photoWidth, photoHeight), 0, 0);
        image.Apply();
        RenderTexture.active = null;

        // 5. Encode to JPG
        byte[] bytes = image.EncodeToJPG();
        Destroy(image); // Free memory immediately

        // 6. Generate Filename (using timestamp)
        frameCount++;
        string fileName = $"frame_{System.DateTime.Now.Ticks}.jpg";
        string fullPath = Path.Combine(inputFolder, fileName);

        // 7. Write to Disk
        try 
        {
            File.WriteAllBytes(fullPath, bytes);
            Debug.Log($"<color=green>[DroneCamera] sent photo #{frameCount}:</color> {fileName}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[DroneCamera] Failed to write file: {e.Message}");
        }
    }
}