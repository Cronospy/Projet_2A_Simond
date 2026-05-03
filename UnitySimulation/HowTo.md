## 1. Prerequisites

Before starting, ensure you have the following:
* **Unity 2022.3 LTS** or newer.
* **Python 3.9+** with `ultralytics`, `opencv-python`, and `numpy` installed.
* **Folder Structure**: Create a folder named `ServerExchange` with three subfolders: `input`, `output`, and `debug`.
* **Trained Weights**: A YOLO model (e.g., `last.pt`) located in your `brain` folder.

---

## Step 1: Start the Python Inference Server

The Python server acts as the "brain," processing images sent from Unity.

1.  Open `server_filebased.py`.
2.  Update the constants at the top:
    * `UNITY_EXCHANGE_DIR`: Path to your `ServerExchange` folder.
    * `MODEL_WEIGHTS_PATH`: Path to your trained `.pt` weights.
3.  Run the script:
    ```bash
    python server_filebased.py
    ```
    *The server will now wait for images to appear in the `input` folder.*

---

## Step 2: Unity Scene Setup

### A. Landscape Generation
1.  Attach `LowPolyLandscape.cs` to an empty GameObject.
2.  Assign your **Spawn Layers** (prefabs for trees, rocks, bushes).
3.  Set the **Map Width/Length** (e.g., 1000m).
4.  The landscape generates automatically on Play.

### B. Drone and Target
1.  **Drone**: Use the `DronePrefab`. Ensure it has `DroneController.cs` for movement and `DroneCamera.cs` for capturing frames.
2.  **Target**: Use the `Male Athlete` ragdoll prefab. Attach `RagdollRandomizer.cs` to it to randomize skin tone and clothing colors for better AI training.

---

## Step 3: Running Different Modes

### Mode A: Automated Dataset Generation
Use this mode to create a synthetic dataset for fine-tuning.
1.  Open the scene with the `DatasetGenerator` component.
2.  Configure **Total Scenes** and **Orbits Per Scene**.
3.  Press **Play**.
4.  **Process**: 
    * Unity generates a landscape and spawns a ragdoll.
    * The drone orbits the victim and takes pictures.
    * It automatically calculates YOLO labels (`.txt`) using raycasting.
    * Files are saved to your `Unity Dataset` folder.

### Mode B: Search Mission Simulation
Use this mode to test how well your AI finds people in a "live" mission.
1.  Attach `DroneSearchMission.cs` to a manager object.
2.  Assign the `landscape`, `ragdollPrefab`, and `dronePrefab`.
3.  Press **Play**.
4.  **Process**:
    * The drone spawns and begins an automated orbit search.
    * `DroneCamera.cs` sends frames to the Python server.
    * The script waits for a `.json` response in the `output` folder.
    * If the AI detects a person, the mission marks a "Success" and moves to the next iteration.

### Mode C: Manual Flight
1.  Ensure `DroneController.cs` and `DroneCamera.cs` are active on the drone.
2.  **Controls**:
    * `WASD`: Pitch and Roll.
    * `Space / Left Shift`: Climb and Descend.
    * `Q / E`: Yaw (Rotate).
3.  The drone will automatically send frames to the AI server every 1 second (configurable in `captureInterval`).

---

## Troubleshooting

* **"No Objects Found"**: Check the `ServerExchange/debug` folder. If you see images but no boxes, the model confidence might be too high. Lower `CONFIDENCE_THRESHOLD` in `server_filebased.py`.
* **Performance Lag**: If Unity stutters, increase the `captureInterval` in `DroneCamera.cs`.
* **Domain Shift**: If the MV finds real people but misses Low-Poly ones, run **Mode A** to generate more synthetic data and fine-tune your model.