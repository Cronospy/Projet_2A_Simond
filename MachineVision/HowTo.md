## 1. Environment Setup (Anaconda)

The project uses **Anaconda** to manage dependencies and system-level libraries (like CUDA). 

1.  **Locate the configuration**: Ensure the `environment.yml` file is located at `C:\Projet_2A_Simond\MachineVision\environment.yml`.
2.  **Create the environment**: Open **Anaconda Prompt** and run:
    ```bash
    cd C:\Projet_2A_Simond\MachineVision
    conda env create -f environment.yml
    ```
3.  **Activate the environment**:
    ```bash
    conda activate drone_project
    ```

---

## 2. Project Structure & Data Placement

For the scripts to function correctly, you must maintain the following directory structure inside `C:\Projet_2A_Simond\MachineVision\drone_project\`:

```text
drone_project/
├── brain/                      # Place base models here (yolo26m.pt, etc.)
│   └── drone_project/          # Training runs and weights are stored here
├── input_images/               # ALL datasets go here
│   ├── HERIDAL Dataset/        # Must be named exactly "HERIDAL Dataset"
│   │   └── train/              # Contains images and _annotations.csv
│   ├── SARD Dataset/           # Must be named exactly "SARD Dataset"
│   │   └── search-and-rescue/  # Contains train/test/valid folders
│   └── Unity Dataset/          # Synthetic data generated from Unity, contains images/labels folders
```

### Dataset Download Instructions:
* **HERIDAL Dataset**: Download the "train" set. Ensure `_annotations.csv` is present in the root of the folder.
* **SARD Dataset**: Ensure it contains the standard YOLO subfolders (`images/`, `labels/`) inside `train`, `test`, and `valid`.
* **Unity Dataset**: This is synthetic dataset. Completely optional. Place generated images and labels in the corresponding subfolders.

---

## 3. Model Preparation

Download the base **YOLO26** weights from the official Ultralytics repository or your project storage.

* **Models**: `yolo26n.pt`, `yolo26s.pt`, `yolo26m.pt`, `yolo26l.pt`, `yolo26x.pt`.
* **Placement**: Save these files directly in `C:\Projet_2A_Simond\MachineVision\drone_project\brain\`.
* **Recommendation**: Use **YOLO26m** for the best balance of speed and accuracy on the **RTX 3050** laptop GPU.

---

## 4. Usage: Training (trainer_CLI_with_Unity_Recall_Goal.py)

This script handles the fine-tuning of the model using HERIDAL, SARD, and Unity datasets.

* **How to run**:
    ```bash
    python trainer_CLI_with_Unity_Recall_Goal.py
    ```
* **Features**:
    * **Interactive Wizard**: Allows you to choose between starting a new training or resuming from a checkpoint.
    * **Smart Crop (SAHI)**: For high-resolution images (HERIDAL), it automatically slices them into **1024x1024** tiles before resizing to **640x640**.
    * **Optimization**: Configured with **Batch Size: 4** to prevent Memory Errors on the RTX 3050 (4GB VRAM).
    * **Composition**: Balances the dataset by generating negative (background) crops to reduce False Positives.

---

## 5. Usage: Benchmarking (benchmark_manager_CLI.py)

Use this to evaluate your trained model's performance on a specific "Sandbox" subset.

* **How to run**:
    ```bash
    python benchmark_manager_CLI.py
    ```
* **Metrics**:
    * Calculates **Precision**, **Recall**, and **F1 Score**.
    * Reports **TP** (True Positives), **FP** (False Positives), and **FN** (False Negatives).
* **Inference Logic**: Uses SAHI slicing (1024px slice size, 0.2 overlap) to ensure consistency with the production environment.

---

## 6. Usage: Unity Inference Server (server_filebased.py)

This script acts as the "brain" for the Unity simulation.

* **How to run**:
    ```bash
    python server_filebased.py
    ```
* **How it works**:
    1.  **Watches**: Monitors `ServerExchange/input` for new `.jpg` frames sent from Unity.
    2.  **Inference**: Runs the trained model (e.g., `unity_run_ft/weights/last.pt`) using SAHI slicing.
    3.  **Output**: Generates a `.json` file in `ServerExchange/output` with coordinates of detected people.
    4.  **Debug**: Saves a visualization in `ServerExchange/debug` to verify performance visually.

---

## 7. Technical Notes for Developers

* **Domain Shift**: When testing in Unity, you may notice lower performance initially due to the Low-Poly style. Fine-tuning on the **Unity Dataset** is required to bridge this gap.
* **GPU Management**: If you encounter `Out of Memory` errors, ensure no other heavy applications are using the GPU and keep the `BATCH_SIZE` at 4.
* **Overlap**: The default overlap for slicing is **20%**. Increase this if you notice objects are being cut in half at the edges of the tiles.