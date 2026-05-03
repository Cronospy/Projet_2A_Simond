import os
import time
import json
import sys
import signal
import cv2
import numpy as np
from datetime import datetime

# --- ULTRALYTICS IMPORT ---
try:
    from ultralytics import YOLO
except ImportError:
    print("[CRITICAL] ultralytics library not found. Install it: pip install ultralytics")
    sys.exit(1)

# ==============================================================================
# CONFIGURATION CONSTANTS
# ==============================================================================

UNITY_EXCHANGE_DIR = r"D:\Unity Projects\My project\Assets\ServerExchange"
MODEL_WEIGHTS_PATH = r"D:\Windows Folders\Desktop\drone_project\brain\drone_project\unity_run_ft\weights\last.pt"

CONFIDENCE_THRESHOLD = 0.35
INFERENCE_SIZE = 640 

# ==============================================================================
# CLASS: GRACEFUL STOP HANDLER
# ==============================================================================
class GracefulKiller:
    kill_now = False
    def __init__(self):
        signal.signal(signal.SIGINT, self.exit_gracefully)
        signal.signal(signal.SIGTERM, self.exit_gracefully)
    def exit_gracefully(self, signum, frame):
        print(f"\n[STOP SIGNAL] Received signal ({signum}). Finishing task...")
        self.kill_now = True

# ==============================================================================
# HELPER: DRAW DEBUG IMAGE
# ==============================================================================
def draw_and_save_debug_image(img_path, detections, output_folder):
    """Draw boxes on images and save to debug folder"""
    try:
        img = cv2.imread(img_path)
        if img is None: return

        for det in detections:
            box = det["box"]
            x, y, w, h = box["x"], box["y"], box["w"], box["h"]
            label = f"{det['name']} {det['conf']:.2f}"
            
            cv2.rectangle(img, (x, y), (x + w, y + h), (0, 255, 0), 2)
            
            (text_w, text_h), _ = cv2.getTextSize(label, cv2.FONT_HERSHEY_SIMPLEX, 0.5, 1)
            cv2.rectangle(img, (x, y - 20), (x + text_w, y), (0, 255, 0), -1)
            cv2.putText(img, label, (x, y - 5), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 0, 0), 1)

        filename = os.path.basename(img_path).replace(".jpg", "_debug.jpg")
        save_path = os.path.join(output_folder, filename)
        cv2.imwrite(save_path, img)
    except Exception as e:
        print(f"[WARN] Failed to draw debug image: {e}")

# ==============================================================================
# MAIN SERVER LOGIC
# ==============================================================================

INPUT_DIR = os.path.join(UNITY_EXCHANGE_DIR, "input")
OUTPUT_DIR = os.path.join(UNITY_EXCHANGE_DIR, "output")
DEBUG_DIR = os.path.join(UNITY_EXCHANGE_DIR, "debug")

def main():
    killer = GracefulKiller()
    print("\n" + "="*60)
    print("      FAST DRONE AI SERVER (PURE YOLOv8)")
    print("="*60)

    # 1. Directory Checks
    for d in [INPUT_DIR, OUTPUT_DIR, DEBUG_DIR]:
        if not os.path.exists(d):
            os.makedirs(d, exist_ok=True)
            print(f"[SETUP] Created: {d}")
        
    print(f"[INFO] Watching: {INPUT_DIR}")

    # 2. Model Loading
    if not os.path.exists(MODEL_WEIGHTS_PATH):
        print(f"[ERROR] Model not found: {MODEL_WEIGHTS_PATH}")
        sys.exit(1)

    print(f"[INIT] Loading YOLO model: {os.path.basename(MODEL_WEIGHTS_PATH)}...")
    model = YOLO(MODEL_WEIGHTS_PATH)
    print("[STATUS] Ready. Waiting for images...")

    # 3. Main Loop
    while not killer.kill_now:
        try:
            files = [f for f in os.listdir(INPUT_DIR) if f.lower().endswith(('.jpg', '.png'))]
            if not files:
                time.sleep(0.05) # Швидша перевірка
                continue

            for fname in files:
                if killer.kill_now: break
                file_path = os.path.join(INPUT_DIR, fname)
                
                if not is_file_ready(file_path): continue

                print(f"[{datetime.now().strftime('%H:%M:%S')}] Processing: {fname}")
                
                # --- FAST INFERENCE WITH ULTRALYTICS ---
                results = model(file_path, imgsz=INFERENCE_SIZE, conf=CONFIDENCE_THRESHOLD, verbose=False)
                
                detections = []
                for r in results:
                    boxes = r.boxes
                    for box in boxes:
                        x1, y1, x2, y2 = box.xyxy[0].tolist()
                        conf = float(box.conf[0])
                        cls_id = int(box.cls[0])
                        class_name = model.names[cls_id]

                        detections.append({
                            "name": class_name,  # Одразу пишемо "name", як треба для Unity
                            "conf": conf,
                            "box": {
                                "x": int(x1), "y": int(y1),
                                "w": int(x2 - x1), "h": int(y2 - y1)
                            }
                        })
                
                # --- SAVE RESULTS ---
                # 1. JSON
                json_path = os.path.join(OUTPUT_DIR, os.path.splitext(fname)[0] + ".json")
                with open(json_path, 'w') as f:
                    json.dump({"status": "success", "objects": detections}, f, indent=4)

                # 2. DEBUG IMAGE
                if detections: 
                    draw_and_save_debug_image(file_path, detections, DEBUG_DIR)
                    print(f"    -> FOUND {len(detections)} objects!")

                # Clean up
                os.remove(file_path)

        except Exception as e:
            print(f"[ERROR] {e}")
            time.sleep(1)

    print("\n[SHUTDOWN] Goodbye.")
    sys.exit(0)

def is_file_ready(path, retries=10, delay=0.02):
    for i in range(retries):
        try:
            if not os.path.exists(path) or os.path.getsize(path) == 0:
                time.sleep(delay); continue
            with open(path, 'rb') as f: f.read(1)
            return True
        except: time.sleep(delay)
    return False

if __name__ == "__main__":
    main()