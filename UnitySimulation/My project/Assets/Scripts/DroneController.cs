using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class DroneController : MonoBehaviour
{
    [Header("Engine Power")]
    public float moveSpeed = 20.0f;
    public float climbSpeed = 20.0f;
    
    [Header("Handling")]
    public float airDrag = 2.0f;        
    public float hoverStrength = 1.0f; 
    
    [Header("Visuals")]
    public Transform droneModel;        
    public float tiltAmount = 30.0f;    
    public float tiltSpeed = 5.0f;      

    [Header("Visual Effects (Shake)")]
    public float shakeAmount = 5.0f;    // How hard it shakes during turns
    public float shakeSpeed = 15.0f;    // How fast it vibrates
    public float idleShake = 0.5f;      // Slight vibration when hovering

    private Rigidbody rb;
    private float hInput, vInput, throttle;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.linearDamping = 0.5f; 
        rb.angularDamping = 2.0f;
    }

    void Update()
    {
        // 1. Get Inputs
        hInput = Input.GetAxis("Horizontal"); 
        vInput = Input.GetAxis("Vertical");   
        
        throttle = 0;
        if (Input.GetKey(KeyCode.Space)) throttle = 1;
        else if (Input.GetKey(KeyCode.LeftShift)) throttle = -1;

        // 2. Handle Visual Tilt & Shake
        if (droneModel != null)
        {
            // A. Base Tilt (Banking)
            float targetPitch = vInput * tiltAmount; 
            float targetRoll = -hInput * tiltAmount; 

            // B. Calculate Turbulence (Shake)
            // Shake increases when we press keys (maneuvering)
            float inputMagnitude = Mathf.Abs(hInput) + Mathf.Abs(vInput) + Mathf.Abs(throttle);
            float currentShake = idleShake + (inputMagnitude * shakeAmount);

            // Perlin Noise generates smooth, random-looking "wobble"
            float noiseX = (Mathf.PerlinNoise(Time.time * shakeSpeed, 0) - 0.5f) * currentShake;
            float noiseZ = (Mathf.PerlinNoise(0, Time.time * shakeSpeed) - 0.5f) * currentShake;

            // C. Apply Combined Rotation
            // We add the noise to the target pitch/roll
            Quaternion targetRot = Quaternion.Euler(targetPitch + noiseX, 0, targetRoll + noiseZ);
            
            // Smoothly interpolate
            droneModel.localRotation = Quaternion.Lerp(droneModel.localRotation, targetRot, Time.deltaTime * tiltSpeed);
        }
    }

    void FixedUpdate()
    {
        // 3. Physics (Same as before)
        float gravityComp = -Physics.gravity.y * rb.mass * hoverStrength;
        rb.AddForce(Vector3.up * gravityComp);

        rb.AddForce(Vector3.up * throttle * climbSpeed);

        Vector3 targetVel = (transform.forward * vInput + transform.right * hInput) * moveSpeed;
        Vector3 velDifference = targetVel - new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
        rb.AddForce(velDifference * airDrag);
    }
}