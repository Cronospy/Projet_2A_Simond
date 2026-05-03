using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;
using System.Linq; 

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class LowPolyLandscape : MonoBehaviour
{
    [Header("Map Dimensions (Meters)")]
    public bool centerToWorldOrigin = true;
    public float mapWidth = 1000f; 
    public float mapLength = 1000f;

    [Tooltip("Vertices per meter. 0.5 is usually good for Low Poly.")]
    [Range(0.1f, 2f)]
    public float vertexDensity = 0.5f;

    [Header("Terrain Shape")]
    public float noiseScale = 0.05f;
    public float heightMultiplier = 30f;
    public int octaves = 3;
    [Range(0,1)] public float persistance = 0.5f;
    public float lacunarity = 2f;
    
    [Header("Seed")]
    public int seed;
    public Vector2 offset;

    [Header("Visuals")]
    public Gradient terrainGradient;

    [Header("Spawning")]
    public List<SpawnLayer> spawnLayers;

    [System.Serializable]
    public class SpawnLayer
    {
        public string name;
        public GameObject[] prefabs;
        
        [Tooltip("Objects per 100 square meters. E.g., 5 = sparse, 50 = dense forest.")]
        public float densityPer100sqm = 10f; 

        [Tooltip("Minimum distance between objects in this layer (meters).")]
        public float minDistance = 2.0f;

        [Range(0f, 1f)] public float minHeightPercent = 0.3f;
        [Range(0f, 1f)] public float maxHeightPercent = 0.9f;
        
        public float minScale = 0.8f;
        public float maxScale = 1.2f;

        [Header("Tilt / Rotation")]
        [Tooltip("Maximum tilt angle in degrees (0 = straight up, 180 = full random rotation)")]
        [Range(0f, 180f)] public float maxTiltAngle = 0f; // NEW: Random Tilt
    }

    // Internal params
    private int xSize;
    private int zSize;
    private float meshScale; 

    private Mesh mesh;
    private Vector3[] vertices;
    private Color[] colors;
    private int[] triangles;
    
    private List<GameObject> spawnedObjects = new List<GameObject>();
    private List<Vector3> occupiedPositions = new List<Vector3>();

    private void Start()
    {
        Generate();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.G))
        {
            seed = Random.Range(0, 100000);
            Generate();
        }
    }

    public void Generate()
    {
        CalculateMeshParams();
        ClearSpawnedObjects();
        
        CreateMeshShape();
        UpdateMesh();
        
        SpawnNature();

        Debug.Log($"[Landscape] Map: {mapWidth}x{mapLength}m. Seed: {seed}");
    }

    void CalculateMeshParams()
    {
        xSize = Mathf.RoundToInt(mapWidth * vertexDensity);
        zSize = Mathf.RoundToInt(mapLength * vertexDensity);
        meshScale = 1f / vertexDensity;
    }

    void CreateMeshShape()
    {
        mesh = new Mesh();
        mesh.indexFormat = IndexFormat.UInt32;
        GetComponent<MeshFilter>().mesh = mesh;

        // 1. Grid Generation
        Vector3[] gridVertices = new Vector3[(xSize + 1) * (zSize + 1)];
        
        System.Random prng = new System.Random(seed);
        float offsetX = prng.Next(-100000, 100000) + offset.x;
        float offsetY = prng.Next(-100000, 100000) + offset.y;

        float startX = centerToWorldOrigin ? -mapWidth / 2f : 0;
        float startZ = centerToWorldOrigin ? -mapLength / 2f : 0;

        for (int i = 0, z = 0; z <= zSize; z++)
        {
            for (int x = 0; x <= xSize; x++)
            {
                float xPos = startX + (x * meshScale);
                float zPos = startZ + (z * meshScale);
                float y = GetNoiseHeight(xPos, zPos, offsetX, offsetY);
                gridVertices[i] = new Vector3(xPos, y, zPos);
                i++;
            }
        }

        // 2. Triangles
        int[] gridTriangles = new int[xSize * zSize * 6];
        int vert = 0;
        int tris = 0;

        for (int z = 0; z < zSize; z++)
        {
            for (int x = 0; x < xSize; x++)
            {
                gridTriangles[tris + 0] = vert + 0;
                gridTriangles[tris + 1] = vert + xSize + 1;
                gridTriangles[tris + 2] = vert + 1;
                gridTriangles[tris + 3] = vert + 1;
                gridTriangles[tris + 4] = vert + xSize + 1;
                gridTriangles[tris + 5] = vert + xSize + 2;

                vert++;
                tris += 6;
            }
            vert++;
        }

        // 3. Flat Shading Unpack
        vertices = new Vector3[gridTriangles.Length];
        triangles = new int[gridTriangles.Length];

        for (int i = 0; i < gridTriangles.Length; i++)
        {
            vertices[i] = gridVertices[gridTriangles[i]];
            triangles[i] = i; 
        }

        ApplyColors();
    }

    float GetNoiseHeight(float x, float z, float offsetX, float offsetY)
    {
        float amplitude = 1;
        float frequency = 1;
        float noiseHeight = 0;

        for (int i = 0; i < octaves; i++)
        {
            float sampleX = (x * noiseScale * frequency * 0.1f) + offsetX;
            float sampleZ = (z * noiseScale * frequency * 0.1f) + offsetY;

            float perlinValue = Mathf.PerlinNoise(sampleX, sampleZ) * 2 - 1;
            noiseHeight += perlinValue * amplitude;

            amplitude *= persistance;
            frequency *= lacunarity;
        }

        return noiseHeight * heightMultiplier;
    }

    void ApplyColors()
    {
        colors = new Color[vertices.Length];
        float minHeight = -heightMultiplier;
        float maxHeight = heightMultiplier;
        
        if (vertices.Length > 0)
        {
            float actualMin = vertices.Min(v => v.y);
            float actualMax = vertices.Max(v => v.y);
            if (actualMax > actualMin) { minHeight = actualMin; maxHeight = actualMax; }
        }

        for (int i = 0; i < vertices.Length; i++)
        {
            float height = Mathf.InverseLerp(minHeight, maxHeight, vertices[i].y);
            colors[i] = terrainGradient.Evaluate(height);
        }
    }

    void UpdateMesh()
    {
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.colors = colors;
        mesh.RecalculateNormals(); 
        GetComponent<MeshCollider>().sharedMesh = mesh;
    }

    void SpawnNature()
    {
        System.Random prng = new System.Random(seed);
        occupiedPositions.Clear();

        float areaPerVertex = (mapWidth * mapLength) / vertices.Length;

        foreach (var layer in spawnLayers)
        {
            if (layer.prefabs == null || layer.prefabs.Length == 0) continue;

            // Analyze valid area
            List<int> validIndices = new List<int>();
            for(int i = 0; i < vertices.Length; i++)
            {
                float currentHeight = vertices[i].y;
                float normalizedHeight = Mathf.InverseLerp(-heightMultiplier, heightMultiplier * 1.5f, currentHeight);

                if (normalizedHeight >= layer.minHeightPercent && normalizedHeight <= layer.maxHeightPercent)
                {
                    validIndices.Add(i);
                }
            }

            float validAreaSqM = validIndices.Count * areaPerVertex;
            int targetCount = Mathf.RoundToInt((validAreaSqM / 100f) * layer.densityPer100sqm);

            if (targetCount == 0) continue;

            int spawnedInLayer = 0;
            int maxFailures = 10;
            int attempts = 0;

            while (spawnedInLayer < targetCount && attempts < targetCount * maxFailures)
            {
                attempts++;
                
                int rndIdx = validIndices[prng.Next(validIndices.Count)];
                Vector3 candidatePos = vertices[rndIdx] + transform.position;

                if (IsTooClose(candidatePos, layer.minDistance)) continue;

                // --- SPAWN ---
                GameObject prefab = layer.prefabs[prng.Next(0, layer.prefabs.Length)];
                GameObject obj = Instantiate(prefab, candidatePos, Quaternion.identity);

                // --- 1. Random Yaw (Rotation around Y) ---
                float yaw = prng.Next(0, 360);
                
                // --- 2. Random Tilt (Rotation around X/Z) ---
                // We generate a random tilt within [-maxTilt, maxTilt] range
                float tiltX = (float)(prng.NextDouble() * 2 - 1) * layer.maxTiltAngle; // -1 to 1 * Angle
                float tiltZ = (float)(prng.NextDouble() * 2 - 1) * layer.maxTiltAngle;

                // Combine rotations: First tilt, then rotate around Y
                obj.transform.rotation = Quaternion.Euler(tiltX, yaw, tiltZ);

                // --- 3. Scale ---
                float s = (float)(prng.NextDouble() * (layer.maxScale - layer.minScale) + layer.minScale);
                obj.transform.localScale *= s;

                obj.transform.parent = this.transform;
                spawnedObjects.Add(obj);
                occupiedPositions.Add(candidatePos);
                spawnedInLayer++;
            }
        }
    }

    bool IsTooClose(Vector3 pos, float minDst)
    {
        float sqrMinDst = minDst * minDst;
        foreach (var occupied in occupiedPositions)
        {
            if ((occupied - pos).sqrMagnitude < sqrMinDst) return true;
        }
        return false;
    }

    void ClearSpawnedObjects()
    {
        foreach (var obj in spawnedObjects)
        {
            if (obj != null) Destroy(obj);
        }
        spawnedObjects.Clear();
        occupiedPositions.Clear();
    }
}