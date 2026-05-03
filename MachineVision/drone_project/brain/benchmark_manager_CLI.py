import os
import shutil
import time
import sys
import random
import csv
import pandas as pd
import numpy as np
import cv2
import torch
import logging
from tqdm import tqdm
from ultralytics import YOLO

# --- SAHI IMPORTS ---
try:
    from sahi import AutoDetectionModel
    from sahi.predict import get_sliced_prediction
except ImportError:
    print("\n[CRITICAL ERROR] SAHI library not found.")
    print("Please install it running: pip install sahi")
    sys.exit(1)

__version__ = "19.1.0"

# --- UNITY FINE-TUNED MODEL ---
UNITY_WEIGHTS_DIR = r"D:\Windows Folders\Desktop\drone_project\brain\drone_project\unity_run_ft\weights"

# --- UNITY DATASET ---
UNITY_DATASET_DIR = r"D:\Windows Folders\Desktop\drone_project\input_images\Unity Dataset"

# --- CONFIGURATION ---
CONFIG = {
    'BASE_DIR': os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    'INPUT_IMAGES_DIR': '', # Set dynamically
    'TEMP_DIR': os.path.join(os.getcwd(), 'temp_benchmark_subset'),
    'PROJECT_NAME': 'drone_project',
    
    # SAHI Settings
    'SLICE_SIZE': 1024,
    'OVERLAP_RATIO': 0.2,
    'CONFIDENCE_THRESHOLD': 0.25,
    
    # Models to Benchmark (Populated by Wizard)
    'MODELS': [] 
}

# Set Global Input Dir
CONFIG['INPUT_IMAGES_DIR'] = os.path.join(CONFIG['BASE_DIR'], 'input_images')

# --- LOGGING SETUP ---
if sys.platform == 'win32':
    import io
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8')

logging.basicConfig(level=logging.INFO, format='%(message)s')
logger = logging.getLogger(__name__)

# --- UTILS ---
def get_user_choice(prompt: str, min_val: int, max_val: int) -> int:
    while True:
        try:
            choice = input(prompt).strip()
            if not choice.isdigit():
                print("Please enter a number.")
                continue
            choice = int(choice)
            if min_val <= choice <= max_val:
                return choice
            print(f"Please enter a number between {min_val} and {max_val}")
        except Exception: pass

def imread_safe(path: str):
    """Windows-safe image reading."""
    try:
        stream = open(path, "rb")
        bytes = bytearray(stream.read())
        numpyarray = np.asarray(bytes, dtype=np.uint8)
        return cv2.imdecode(numpyarray, cv2.IMREAD_UNCHANGED)
    except Exception: return None

def select_custom_model_path():
    """Interactive browser for drone_project runs."""
    runs_dir = os.path.join(CONFIG['BASE_DIR'], 'brain', CONFIG['PROJECT_NAME'])
    if not os.path.exists(runs_dir):
        print(f"[Error] No runs found in {runs_dir}")
        return None

    runs = [d for d in os.listdir(runs_dir) if os.path.isdir(os.path.join(runs_dir, d))]
    runs.sort(key=lambda x: os.path.getmtime(os.path.join(runs_dir, x)), reverse=True)

    if not runs:
        print("[Error] No runs found.")
        return None

    print("\n--- Select Training Run ---")
    for idx, run in enumerate(runs):
        mtime = os.path.getmtime(os.path.join(runs_dir, run))
        t_str = time.strftime('%Y-%m-%d %H:%M', time.localtime(mtime))
        print(f" [{idx+1}] {run} ({t_str})")
    
    r_idx = get_user_choice(f"Select Run (1-{len(runs)}): ", 1, len(runs)) - 1
    selected_run = runs[r_idx]
    
    weights_dir = os.path.join(runs_dir, selected_run, 'weights')
    if not os.path.exists(weights_dir):
        print("[Error] No weights folder in this run.")
        return None
        
    weights = [f for f in os.listdir(weights_dir) if f.endswith('.pt')]
    weights.sort(key=lambda x: os.path.getmtime(os.path.join(weights_dir, x)), reverse=True)
    
    print(f"\n--- Select Checkpoint in '{selected_run}' ---")
    for idx, w in enumerate(weights):
        print(f" [{idx+1}] {w}")
        
    w_idx = get_user_choice(f"Select Checkpoint (1-{len(weights)}): ", 1, len(weights)) - 1
    
    full_path = os.path.join(weights_dir, weights[w_idx])
    # Create a short alias for the report
    alias = f"{selected_run[:15]}.../{weights[w_idx]}"
    return full_path, alias


def select_unity_model():
    """Lets the user pick best.pt or last.pt from the Unity fine-tuned run."""
    if not os.path.exists(UNITY_WEIGHTS_DIR):
        print(f"[Error] Unity weights folder not found:\n  {UNITY_WEIGHTS_DIR}")
        return None

    weights = [f for f in os.listdir(UNITY_WEIGHTS_DIR) if f.endswith('.pt')]
    weights.sort(key=lambda x: os.path.getmtime(os.path.join(UNITY_WEIGHTS_DIR, x)), reverse=True)

    if not weights:
        print("[Error] No .pt files found in Unity weights folder.")
        return None

    print("\n--- Unity Fine-tuned Model --- Select Checkpoint ---")
    for idx, w in enumerate(weights):
        size_mb = os.path.getsize(os.path.join(UNITY_WEIGHTS_DIR, w)) / 1_048_576
        mtime   = os.path.getmtime(os.path.join(UNITY_WEIGHTS_DIR, w))
        t_str   = time.strftime('%Y-%m-%d %H:%M', time.localtime(mtime))
        print(f" [{idx+1}] {w}  ({size_mb:.1f} MB, {t_str})")

    w_idx = get_user_choice(f"Select (1-{len(weights)}): ", 1, len(weights)) - 1
    chosen = weights[w_idx]
    full_path = os.path.join(UNITY_WEIGHTS_DIR, chosen)
    alias = f"Unity-FT/{chosen}"
    return full_path, alias

# --- WIZARD ---
def run_setup_wizard():
    print("\n" + "="*60)
    print(f" [BENCHMARK & INFERENCE TOOL V{__version__}] SETUP WIZARD")
    print("="*60 + "\n")

    # 1. Dataset Selection
    print("Select Dataset for Testing:")
    print(" [1] HERIDAL (Real Humans)")
    print(" [2] SARD (Synthetic/Mixed)")
    print(" [3] Unity Synthetic (Generated in Unity)")
    
    ds_choice = get_user_choice("Choice (1-3): ", 1, 3)
    
    dataset_config = {}
    if ds_choice == 1:
        dataset_config = {
            'name': 'HERIDAL',
            'images_dir': os.path.join(CONFIG['INPUT_IMAGES_DIR'], 'HERIDAL Dataset', 'train'),
            'type': 'csv',
            'labels': os.path.join(CONFIG['INPUT_IMAGES_DIR'], 'HERIDAL Dataset', 'train', '_annotations.csv')
        }
    elif ds_choice == 2:
        dataset_config = {
            'name': 'SARD',
            'images_dir': os.path.join(CONFIG['INPUT_IMAGES_DIR'], 'SARD Dataset', 'search-and-rescue', 'valid', 'images'),
            'type': 'yolo',
            'labels': os.path.join(CONFIG['INPUT_IMAGES_DIR'], 'SARD Dataset', 'search-and-rescue', 'valid', 'labels')
        }
    else:
        # Unity dataset: images/ and labels/ sit directly inside UNITY_DATASET_DIR
        unity_images = os.path.join(UNITY_DATASET_DIR, 'images')
        unity_labels = os.path.join(UNITY_DATASET_DIR, 'labels')
        if not os.path.exists(unity_images):
            print(f"[Error] Unity images folder not found:\n  {unity_images}")
            sys.exit(1)
        if not os.path.exists(unity_labels):
            print(f"[Error] Unity labels folder not found:\n  {unity_labels}")
            sys.exit(1)
        # Count split: show user how many with/without person
        total_imgs = len([f for f in os.listdir(unity_images) if f.lower().endswith('.jpg')])
        bg_count   = sum(1 for f in os.listdir(unity_labels)
                         if f.endswith('.txt') and os.path.getsize(os.path.join(unity_labels, f)) == 0)
        print(f"[Unity Dataset] {total_imgs} images  |  {total_imgs - bg_count} with person  |  {bg_count} background")
        dataset_config = {
            'name': 'Unity',
            'images_dir': unity_images,
            'type': 'yolo',
            'labels': unity_labels
        }

    # 2. Model Selection Loop
    print("\n--- Model Selection ---")
    CONFIG['MODELS'] = []
    
    while True:
        print(f"\nCurrent List: {[m['name'] for m in CONFIG['MODELS']]}")
        print("Add Model:")
        print(" [1] Add Custom Model (Browse 'drone_project' folder)")
        print(" [2] Add Standard Model (yolo26n/s/m...)")
        print(" [3] Add Unity Fine-tuned Model  (unity_run_ft)")
        print(" [4] Done (Start Benchmark)")
        
        m_choice = get_user_choice("Choice (1-4): ", 1, 4)
        
        if m_choice == 1:
            result = select_custom_model_path()
            if result:
                path, alias = result
                CONFIG['MODELS'].append({'path': path, 'name': alias})
        elif m_choice == 2:
            std_models = ['yolo26n.pt', 'yolo26s.pt', 'yolo26m.pt', 'yolo26l.pt']
            print("\nSelect Standard Model:")
            for i, m in enumerate(std_models): print(f" [{i+1}] {m}")
            idx = get_user_choice("Choice: ", 1, len(std_models)) - 1
            CONFIG['MODELS'].append({'path': std_models[idx], 'name': std_models[idx].upper()})
        elif m_choice == 3:
            result = select_unity_model()
            if result:
                path, alias = result
                CONFIG['MODELS'].append({'path': path, 'name': alias})
                print(f"[OK] Added: {alias}")
        else:
            if not CONFIG['MODELS']:
                print("[Error] You must select at least one model.")
                continue
            break

    # 3. Sample Size
    print("\nHow many images to test?")
    print(" (Enter '0' for ALL images, or a number like '10', '50')")
    sample_input = input("Number: ").strip()
    sample_size = int(sample_input) if sample_input.isdigit() else 10
    if sample_size == 0: sample_size = None # All

    return dataset_config, sample_size

# --- CORE LOGIC ---
def prepare_sandbox(dataset_cfg, sample_size):
    if os.path.exists(CONFIG['TEMP_DIR']): shutil.rmtree(CONFIG['TEMP_DIR'])
    os.makedirs(os.path.join(CONFIG['TEMP_DIR'], 'images'), exist_ok=True)
    
    all_imgs = [f for f in os.listdir(dataset_cfg['images_dir']) if f.lower().endswith('.jpg')]
    
    if sample_size and sample_size < len(all_imgs):
        selected = random.sample(all_imgs, sample_size)
    else:
        selected = all_imgs
        
    print(f"\n[INFO] Preparing sandbox with {len(selected)} images...")
    
    # Parse labels if CSV
    csv_data = {}
    if dataset_cfg['type'] == 'csv':
        csv_data = parse_heridal_csv(dataset_cfg['labels'])

    ground_truths = {} # fname -> [[x1,y1,x2,y2], ...]

    for fname in tqdm(selected):
        src_path = os.path.join(dataset_cfg['images_dir'], fname)
        dst_path = os.path.join(CONFIG['TEMP_DIR'], 'images', fname)
        shutil.copy2(src_path, dst_path)
        
        # Load GT
        img = imread_safe(src_path)
        if img is None: continue
        h, w = img.shape[:2]
        boxes = []
        
        if dataset_cfg['type'] == 'csv':
            boxes = csv_data.get(fname, []) # Already pixels if parsed correctly
        else:
            # SARD YOLO format
            lbl_name = os.path.splitext(fname)[0] + '.txt'
            lbl_path = os.path.join(dataset_cfg['labels'], lbl_name)
            if os.path.exists(lbl_path):
                with open(lbl_path, 'r') as f:
                    for line in f:
                        parts = list(map(float, line.strip().split()))
                        if int(parts[0]) == 0: # Person
                            nx, ny, nw, nh = parts[1:]
                            x1 = (nx - nw/2) * w
                            y1 = (ny - nh/2) * h
                            x2 = (nx + nw/2) * w
                            y2 = (ny + nh/2) * h
                            boxes.append([x1, y1, x2, y2])
        
        ground_truths[fname] = boxes
        
    return selected, ground_truths

def parse_heridal_csv(path):
    data = {} # fname -> [[x1, y1, x2, y2]]
    if not os.path.exists(path): return {}
    with open(path, 'r') as f:
        reader = csv.reader(f)
        next(reader)
        for row in reader:
            if not row or len(row) < 8: continue
            fname, w_str, h_str, cls, xmin, ymin, xmax, ymax = row
            if cls != 'human': continue
            if fname not in data: data[fname] = []
            data[fname].append([float(xmin), float(ymin), float(xmax), float(ymax)])
    return data

def run_benchmark_engine(selected_images, ground_truths):
    print("\n" + "="*60)
    print("   RUNNING SAHI BENCHMARK")
    print("="*60)
    
    report_data = []
    
    for model_cfg in CONFIG['MODELS']:
        name = model_cfg['name']
        path = model_cfg['path']
        print(f"\n[TESTING] {name}...")
        
        try:
            detection_model = AutoDetectionModel.from_pretrained(
                model_type='yolov8',
                model_path=path,
                confidence_threshold=CONFIG['CONFIDENCE_THRESHOLD'],
                device="cuda:0" if torch.cuda.is_available() else "cpu",
            )
        except Exception as e:
            print(f"[Error] Failed to load {name}: {e}")
            continue
            
        total_tp, total_fp, total_fn = 0, 0, 0
        start_t = time.time()
        
        for fname in tqdm(selected_images, desc="Inference"):
            img_path = os.path.join(CONFIG['TEMP_DIR'], 'images', fname)
            
            # SAHI Inference
            result = get_sliced_prediction(
                img_path,
                detection_model,
                slice_height=CONFIG['SLICE_SIZE'],
                slice_width=CONFIG['SLICE_SIZE'],
                overlap_height_ratio=CONFIG['OVERLAP_RATIO'],
                overlap_width_ratio=CONFIG['OVERLAP_RATIO'],
                verbose=0
            )
            
            preds = []
            for obj in result.object_prediction_list:
                b = obj.bbox
                preds.append([b.minx, b.miny, b.maxx, b.maxy])
                
            gts = ground_truths.get(fname, [])
            
            tp, fp, fn = calculate_metrics(preds, gts)
            total_tp += tp
            total_fp += fp
            total_fn += fn
            
        # Calc Stats
        total_time = (time.time() - start_t)
        img_per_sec = len(selected_images) / total_time if total_time > 0 else 0
        
        precision = total_tp / (total_tp + total_fp + 1e-6)
        recall = total_tp / (total_tp + total_fn + 1e-6)
        f1 = 2 * (precision * recall) / (precision + recall + 1e-6)
        
        report_data.append({
            'Model': name,
            'F1 Score': f"{f1*100:.1f}",
            'Recall': f"{recall*100:.1f}%",
            'Precision': f"{precision*100:.1f}%",
            'Speed': f"{img_per_sec:.1f} img/s",
            'TP': total_tp,
            'FP': total_fp, 
            'FN': total_fn
        })
        
    return report_data

def calculate_metrics(preds, gts, iou_thresh=0.1):
    """Greedy IoU matching."""
    if not preds and not gts: return 0, 0, 0
    if not preds: return 0, 0, len(gts)
    if not gts: return 0, len(preds), 0
    
    # Simple IoU calculation loop
    tp = 0
    matched_gt = set()
    
    for p in preds:
        best_iou = 0
        best_gt_idx = -1
        
        p_area = (p[2]-p[0]) * (p[3]-p[1])
        
        for i, g in enumerate(gts):
            if i in matched_gt: continue
            
            # Intersection
            xx1 = max(p[0], g[0])
            yy1 = max(p[1], g[1])
            xx2 = min(p[2], g[2])
            yy2 = min(p[3], g[3])
            
            w = max(0, xx2 - xx1)
            h = max(0, yy2 - yy1)
            inter = w * h
            
            g_area = (g[2]-g[0]) * (g[3]-g[1])
            union = p_area + g_area - inter
            
            iou = inter / union if union > 0 else 0
            
            if iou > best_iou:
                best_iou = iou
                best_gt_idx = i
        
        if best_iou >= iou_thresh:
            tp += 1
            matched_gt.add(best_gt_idx)
            
    fp = len(preds) - tp
    fn = len(gts) - len(matched_gt)
    return tp, fp, fn

def run_visual_mode(selected_images):
    print("\n" + "="*60)
    print("   GENERATING VISUALIZATIONS")
    print("="*60)
    
    if not CONFIG['MODELS']:
        print("No models selected.")
        return

    # Use the first model in list for visuals
    model_cfg = CONFIG['MODELS'][0]
    print(f"Using model: {model_cfg['name']}")
    
    save_dir = os.path.join(CONFIG['BASE_DIR'], 'brain', 'benchmark_visuals')
    if os.path.exists(save_dir): shutil.rmtree(save_dir)
    os.makedirs(save_dir, exist_ok=True)
    
    detection_model = AutoDetectionModel.from_pretrained(
        model_type='yolov8',
        model_path=model_cfg['path'],
        confidence_threshold=CONFIG['CONFIDENCE_THRESHOLD'],
        device="cuda:0" if torch.cuda.is_available() else "cpu",
    )
    
    for fname in tqdm(selected_images, desc="Saving Images"):
        img_path = os.path.join(CONFIG['TEMP_DIR'], 'images', fname)
        
        result = get_sliced_prediction(
            img_path,
            detection_model,
            slice_height=CONFIG['SLICE_SIZE'],
            slice_width=CONFIG['SLICE_SIZE'],
            overlap_height_ratio=CONFIG['OVERLAP_RATIO'],
            overlap_width_ratio=CONFIG['OVERLAP_RATIO'],
            verbose=0
        )
        
        result.export_visuals(export_dir=save_dir, file_name=f"VIS_{fname}")
        
    print(f"\n[Done] Check folder: {save_dir}")

def main():
    # 1. Wizard
    dataset_cfg, sample_size = run_setup_wizard()
    
    # 2. Prep
    selected_images, ground_truths = prepare_sandbox(dataset_cfg, sample_size)
    
    # 3. Action
    print("\nAction:")
    print(" [1] Run Benchmark Metrics")
    print(" [2] Generate Visuals (First selected model only)")
    action = get_user_choice("Choice: ", 1, 2)
    
    if action == 1:
        data = run_benchmark_engine(selected_images, ground_truths)
        if data:
            df = pd.DataFrame(data)
            print("\n" + "="*80)
            print(f"{'BENCHMARK RESULTS':^80}")
            print("="*80)
            print(df.to_string(index=False))
            print("="*80)
            
            # Save CSV
            csv_path = os.path.join(CONFIG['BASE_DIR'], 'brain', 'benchmark_results.csv')
            df.to_csv(csv_path, index=False)
            print(f"Results saved to: {csv_path}")
            
    else:
        run_visual_mode(selected_images)
        
    # Cleanup
    if os.path.exists(CONFIG['TEMP_DIR']):
        shutil.rmtree(CONFIG['TEMP_DIR'])

if __name__ == "__main__":
    main()