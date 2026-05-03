import os
import cv2
import csv
import yaml
import shutil
import random
import torch
import logging
import gc
import hashlib
import json
import time
import signal
import sys
import glob
import numpy as np
from tqdm import tqdm
from ultralytics import YOLO
from typing import Dict, List, Tuple, Set, Optional, Any

__version__ = "18.0.0"

# --- DEFAULT CONFIGURATION ---
CONFIG = {
    # Paths
    'BASE_DIR': os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    'HERIDAL_ROOT': 'HERIDAL Dataset/train',
    'HERIDAL_CSV': 'HERIDAL Dataset/train/_annotations.csv',
    'SARD_ROOT': 'SARD Dataset/search-and-rescue',
    'UNITY_ROOT': 'Unity Dataset', # NEW: Unity Dataset path
    'PROJECT_NAME': 'drone_project', # YOLO Project Name & Folder
    
    # Model Settings
    'MODEL_TYPE': 'yolo26m.pt', # Default
    'IMG_SIZE': 640,
    'EPOCHS': 50,
    'BATCH_SIZE': 4,
    
    # Dataset Selection
    'USE_HERIDAL': True,
    'USE_SARD': True,
    'USE_UNITY': True, # NEW: Unity toggle

    # Smart Crop Logic
    'CROP_SIZE': 1024,
    'SECURE_PADDING': 60,
    'MIN_BOX_SIZE': 10,
    'MIN_VISIBILITY_PIXELS': 5,
    'AUGMENT_COPIES': 3,
    'BACKGROUND_RATIO': 0.1,
    'MAX_BG_CROPS': 5,
    'BG_MAX_ATTEMPTS_MULTIPLIER': 10,
    
    # Split & Seed
    'TRAIN_RATIO': 0.85,
    'SEED': 42,
    
    # System & Optimization
    'WORKERS': 0,
    'GC_FREQUENCY': 50,
    'SAVE_FREQUENCY': 50,
    'DEBUG_MODE': False,
    'MAX_DEBUG_SAMPLES': 20,
    
    # RESUME & CACHE
    'RESUME_PREP': True,
    'RESUME_TRAINING': False, # Controlled by Wizard
    'RESUME_TYPE': 'none',    # 'continue' or 'finetune'
    'RUN_NAME': f'production_run_v18_{int(time.time())}',
    
    # Safety
    'DRY_RUN': False
}

# --- GLOBAL PATHS ---
INPUT_DIR = os.path.join(CONFIG['BASE_DIR'], 'input_images')

# --- LOGGING SETUP ---
# Fix Windows console encoding
if sys.platform == 'win32':
    import io
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8')

log_file = os.path.join(CONFIG['BASE_DIR'], 'brain', 'training_v18.log')
file_handler = logging.FileHandler(log_file, encoding='utf-8')
stream_handler = logging.StreamHandler(sys.stdout)

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s',
    handlers=[file_handler, stream_handler]
)
logger = logging.getLogger(__name__)

# --- HELPER FUNCTIONS ---
def get_output_dir() -> str:
    """Returns the current output directory based on active CONFIG."""
    return os.path.join(CONFIG['BASE_DIR'], 'brain', f"dataset_{CONFIG['RUN_NAME']}")

def get_user_choice(prompt: str, min_val: int, max_val: int) -> int:
    """Gets validated numeric input from user."""
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
        except Exception:
            print("Invalid input.")

# --- INTERACTIVE WIZARD ---
def run_setup_wizard():
    print("\n" + "="*60)
    print(f" [DRONE TRAINING PIPELINE V{__version__}] SETUP WIZARD")
    print("="*60 + "\n")

    # 1. Training Mode Selection
    print("Select Training Mode:")
    print(" [1] Start NEW Training (from base YOLO model)")
    print(" [2] RESUME or FINE-TUNE Existing Run")
    
    choice = get_user_choice("Choice (1/2): ", 1, 2)
    
    if choice == 2:
        # --- RESUME LOGIC ---
        runs_dir = os.path.join(CONFIG['BASE_DIR'], 'brain', CONFIG['PROJECT_NAME'])
        if not os.path.exists(runs_dir):
            print(f"\n[Error] No training runs found in {runs_dir}")
            sys.exit(1)
            
        runs = [d for d in os.listdir(runs_dir) if os.path.isdir(os.path.join(runs_dir, d))]
        # Sort by mtime (newest first)
        runs.sort(key=lambda x: os.path.getmtime(os.path.join(runs_dir, x)), reverse=True)
        
        if not runs:
            print(f"\n[Error] No valid run directories found.")
            sys.exit(1)
            
        print("\nAvailable Runs (Newest First):")
        for idx, run in enumerate(runs):
            mtime = os.path.getmtime(os.path.join(runs_dir, run))
            t_str = time.strftime('%Y-%m-%d %H:%M', time.localtime(mtime))
            print(f" [{idx+1}] {run} ({t_str})")
            
        run_idx = get_user_choice(f"\nSelect Run (1-{len(runs)}): ", 1, len(runs)) - 1
        selected_run = runs[run_idx]
        
        # Select Checkpoint
        weights_dir = os.path.join(runs_dir, selected_run, 'weights')
        if not os.path.exists(weights_dir):
            print(f"[Error] No weights folder found in {selected_run}")
            sys.exit(1)
            
        weights = [f for f in os.listdir(weights_dir) if f.endswith('.pt')]
        weights.sort(key=lambda x: os.path.getmtime(os.path.join(weights_dir, x)), reverse=True)
        
        print(f"\nAvailable Checkpoints in '{selected_run}':")
        for idx, w in enumerate(weights):
            print(f" [{idx+1}] {w}")
            
        w_idx = get_user_choice(f"\nSelect Checkpoint (1-{len(weights)}): ", 1, len(weights)) - 1
        selected_weight_path = os.path.join(weights_dir, weights[w_idx])
        
        print("\nResume Mode:")
        print(" [1] Continue interrupted training (resume=True)")
        print(" [2] Fine-tune from this checkpoint (start new run)")
        
        resume_mode = get_user_choice("Choice (1/2): ", 1, 2)
        
        CONFIG['MODEL_TYPE'] = selected_weight_path
        CONFIG['RESUME_TRAINING'] = True
        
        if resume_mode == 1:
            CONFIG['RESUME_TYPE'] = 'continue'
            CONFIG['RUN_NAME'] = selected_run # Use existing name
            print(f"-> Resuming run: {selected_run}")
        else:
            CONFIG['RESUME_TYPE'] = 'finetune'
            default_name = f"{selected_run}_ft"
            new_name = input(f"Name for fine-tuned run (default: {default_name}): ").strip()
            CONFIG['RUN_NAME'] = new_name if new_name else default_name
            print(f"-> Fine-tuning as: {CONFIG['RUN_NAME']}")
        
    else:
        # --- NEW TRAINING LOGIC ---
        models = ['yolo26n.pt', 'yolo26s.pt', 'yolo26m.pt', 'yolo26l.pt', 'yolo26x.pt']
        print("\nSelect Base Model:")
        for idx, m in enumerate(models):
            print(f" [{idx+1}] {m}")
            
        m_idx = get_user_choice(f"Choice (1-{len(models)}): ", 1, len(models)) - 1
        CONFIG['MODEL_TYPE'] = models[m_idx]
        CONFIG['RESUME_TRAINING'] = False
        
        custom_name = input(f"\nName for this run (default: {CONFIG['RUN_NAME']}): ").strip()
        if custom_name:
            CONFIG['RUN_NAME'] = custom_name

    # 2. Dataset Selection
    print("\nSelect Datasets:")
    print(" [1] HERIDAL Only")
    print(" [2] SARD Only")
    print(" [3] UNITY Only")
    print(" [4] ALL Datasets (Recommended)")
    
    ds_choice = get_user_choice("Choice (1-4): ", 1, 4)
    
    # Reset all to False initially
    CONFIG['USE_HERIDAL'] = CONFIG['USE_SARD'] = CONFIG['USE_UNITY'] = False
    
    if ds_choice == 1: CONFIG['USE_HERIDAL'] = True
    elif ds_choice == 2: CONFIG['USE_SARD'] = True
    elif ds_choice == 3: CONFIG['USE_UNITY'] = True
    else: 
        CONFIG['USE_HERIDAL'] = True
        CONFIG['USE_SARD'] = True
        CONFIG['USE_UNITY'] = True

    # 3. Config Review
    print("\n" + "-"*40)
    print("CONFIGURATION REVIEW")
    print("-"*40)
    keys_to_show = ['MODEL_TYPE', 'EPOCHS', 'BATCH_SIZE', 'IMG_SIZE', 'CROP_SIZE']
    for k in keys_to_show:
        print(f" {k:<15}: {CONFIG[k]}")
    print(f" RUN NAME       : {CONFIG['RUN_NAME']}")
    print(f" DATASETS       : HER={CONFIG['USE_HERIDAL']}, SARD={CONFIG['USE_SARD']}, UNITY={CONFIG['USE_UNITY']}")
    print("-"*40)
    
    if input("\nModify parameters? (y/n): ").lower() == 'y':
        while True:
            key = input("Enter param name (or ENTER to finish): ").strip().upper()
            if not key: break
            if key not in CONFIG:
                print("Invalid key.")
                continue
            
            curr_val = CONFIG[key]
            new_val = input(f"New value for {key} (current: {curr_val}): ")
            
            try:
                if isinstance(curr_val, bool):
                    CONFIG[key] = new_val.lower() in ('true', '1', 'yes', 'y')
                elif isinstance(curr_val, int):
                    CONFIG[key] = int(new_val)
                elif isinstance(curr_val, float):
                    CONFIG[key] = float(new_val)
                else:
                    CONFIG[key] = new_val
                print(f"-> Updated {key} to {CONFIG[key]}")
            except ValueError:
                print("Invalid value type.")
    
    print("\n[OK] Configuration finalized. Starting pipeline...")
    time.sleep(1)

# --- UTILITIES ---
class GracefulKiller:
    kill_now = False
    def __init__(self):
        signal.signal(signal.SIGINT, self.exit_gracefully)
        signal.signal(signal.SIGTERM, self.exit_gracefully)
    def exit_gracefully(self, signum, frame):
        logger.warning(f"\nReceived signal {signum}. Saving progress and exiting...")
        self.kill_now = True

def log_gpu_stats():
    if torch.cuda.is_available():
        t = torch.cuda.get_device_properties(0).total_memory
        r = torch.cuda.memory_reserved(0)
        a = torch.cuda.memory_allocated(0)
        logger.info(f"[GPU] Total: {t/1e9:.2f}GB | Reserved: {r/1e9:.2f}GB | Allocated: {a/1e9:.2f}GB")

def set_seed(seed: int):
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    if torch.cuda.is_available():
        torch.cuda.manual_seed_all(seed)

def get_config_hash() -> str:
    relevant_keys = [
        'CROP_SIZE', 'SECURE_PADDING', 'MIN_BOX_SIZE', 'MIN_VISIBILITY_PIXELS',
        'AUGMENT_COPIES', 'BACKGROUND_RATIO', 'TRAIN_RATIO', 'SEED', 
        'USE_HERIDAL', 'USE_SARD', 'USE_UNITY' # Added USE_UNITY here
    ]
    subset = {k: CONFIG[k] for k in relevant_keys}
    return hashlib.md5(json.dumps(subset, sort_keys=True).encode('utf-8')).hexdigest()

def check_cache_validity() -> bool:
    output_dir = get_output_dir()
    hash_file = os.path.join(output_dir, 'config_hash.txt')
    current_hash = get_config_hash()
    
    if os.path.exists(hash_file):
        with open(hash_file, 'r') as f:
            saved_hash = f.read().strip()
        if saved_hash == current_hash:
            logger.info("Cache Valid: Configuration match.")
            return True
        else:
            logger.warning("Cache Invalid: Configuration changed. Rebuilding dataset.")
            return False
    return False

def validate_config():
    errors = []
    if CONFIG['USE_HERIDAL'] and not os.path.exists(os.path.join(INPUT_DIR, CONFIG['HERIDAL_ROOT'])):
        errors.append(f"HERIDAL_ROOT not found: {CONFIG['HERIDAL_ROOT']}")
    if CONFIG['CROP_SIZE'] < 256:
        errors.append(f"CROP_SIZE too small: {CONFIG['CROP_SIZE']}")
    if not 0 < CONFIG['TRAIN_RATIO'] < 1:
        errors.append("TRAIN_RATIO must be between 0 and 1")
    
    if errors:
        raise ValueError("Configuration errors:\n  - " + "\n  - ".join(errors))
    logger.info("[OK] Configuration validated")

def atomic_write_image(path: str, img: np.ndarray):
    if CONFIG['DRY_RUN']: return
    if img is None or img.size == 0:
        logger.error(f"Attempted to write empty image to {path}")
        return

    tmp_path = path + '.tmp'
    try:
        success, buf = cv2.imencode('.jpg', img)
        if not success:
            raise IOError("cv2.imencode returned False")
        
        with open(tmp_path, 'wb') as f:
            buf.tofile(f)
            
        # os.replace is atomic and safe
        os.replace(tmp_path, path)
    except Exception as e:
        logger.error(f"Failed to write image {path}: {e}")
        if os.path.exists(tmp_path):
            try: os.remove(tmp_path)
            except: pass
        raise

def visualize_crop_debug(img: np.ndarray, crop_box: List[int], all_boxes: List[List[int]], save_path: str):
    if not CONFIG['DEBUG_MODE']: return
    vis = img.copy()
    cx1, cy1, cx2, cy2 = crop_box
    cv2.rectangle(vis, (cx1, cy1), (cx2, cy2), (0, 255, 0), 4)
    cv2.putText(vis, "CROP", (cx1+10, cy1+30), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2)
    
    for idx, box in enumerate(all_boxes):
        bx1, by1, bx2, by2 = box
        overlaps = check_overlap(crop_box, [box])
        color = (0, 0, 255) if overlaps else (128, 128, 128)
        thickness = 2 if overlaps else 1
        cv2.rectangle(vis, (bx1, by1), (bx2, by2), color, thickness)
        if overlaps:
            cv2.putText(vis, f"#{idx}", (bx1, by1-5), cv2.FONT_HERSHEY_SIMPLEX, 0.5, color, 1)
            
    atomic_write_image(save_path, vis)

def save_metrics(stats: Dict, output_path: str):
    if CONFIG['DRY_RUN']: return
    metrics = {
        'version': __version__,
        'timestamp': time.strftime('%Y-%m-%d %H:%M:%S'),
        'config': {k: v for k, v in CONFIG.items() if not k.endswith('_DIR')},
        'stats': {k: v for k, v in stats.items() if k != 'box_sizes'},
        'box_statistics': {
            'count': len(stats['box_sizes']),
            'widths': {
                'min': int(min(w for w, h in stats['box_sizes'])) if stats['box_sizes'] else 0,
                'max': int(max(w for w, h in stats['box_sizes'])) if stats['box_sizes'] else 0,
                'mean': float(np.mean([w for w, h in stats['box_sizes']])) if stats['box_sizes'] else 0
            },
            'heights': {
                'min': int(min(h for w, h in stats['box_sizes'])) if stats['box_sizes'] else 0,
                'max': int(max(h for w, h in stats['box_sizes'])) if stats['box_sizes'] else 0,
                'mean': float(np.mean([h for w, h in stats['box_sizes']])) if stats['box_sizes'] else 0
            }
        }
    }
    try:
        with open(output_path, 'w') as f:
            json.dump(metrics, f, indent=2)
        logger.info(f"Metrics saved to {output_path}")
    except Exception as e:
        logger.error(f"Failed to save metrics: {e}")

class ProgressTracker:
    def __init__(self, checkpoint_path: str, save_frequency: int = 50):
        self.checkpoint_path = checkpoint_path
        self.save_frequency = save_frequency
        self.processed = self.load()
        self.unsaved_count = 0
        self.start_time = time.time()
        
    def load(self) -> Set[str]:
        if os.path.exists(self.checkpoint_path):
            try:
                with open(self.checkpoint_path, 'r') as f:
                    return set(json.load(f))
            except: return set()
        return set()

    def mark_processed(self, item: str):
        self.processed.add(item)
        self.unsaved_count += 1
        if self.unsaved_count >= self.save_frequency:
            self.save()

    def is_processed(self, item: str) -> bool:
        return item in self.processed

    def save(self):
        if CONFIG['DRY_RUN']: return
        try:
            tmp_path = self.checkpoint_path + '.tmp'
            with open(tmp_path, 'w') as f:
                json.dump(list(self.processed), f)
            # os.replace is atomic
            os.replace(tmp_path, self.checkpoint_path)
            self.unsaved_count = 0
        except Exception as e:
            logger.error(f"Failed to save progress: {e}")

    def __str__(self):
        elapsed = time.time() - self.start_time
        rate = len(self.processed) / elapsed if elapsed > 0 else 0
        return f"{len(self.processed)} files @ {rate:.1f} files/sec"

# --- CORE LOGIC ---
def main():
    # 1. Wizard
    run_setup_wizard()
    
    logger.info(f"=== STARTING ULTIMATE DRONE PIPELINE V{__version__} ===")
    validate_config()
    log_gpu_stats()
    set_seed(CONFIG['SEED'])
    
    killer = GracefulKiller()
    output_dir = get_output_dir()
    
    # 2. Cache Control
    if CONFIG['RESUME_PREP']:
        if not check_cache_validity():
            if os.path.exists(output_dir):
                shutil.rmtree(output_dir)
            CONFIG['RESUME_PREP'] = False 
    else:
        if os.path.exists(output_dir):
            logger.warning(f"Wiping directory {output_dir} (RESUME_PREP=False)")
            shutil.rmtree(output_dir)

    setup_directories()
    
    if not CONFIG['DRY_RUN']:
        with open(os.path.join(output_dir, 'config_hash.txt'), 'w') as f:
            f.write(get_config_hash())

    # Stats Dictionary
    stats = {
        'crops_generated': 0, 'background_crops': 0, 'skipped_small': 0,
        'missing_files': 0, 'sard_images': 0, 'sard_cached': 0,
        'sard_empty_labels': 0, 'unity_images': 0, 'unity_cached': 0, # Added Unity keys
        'unity_empty_labels': 0, 'box_sizes': []
    }
    
    tracker = ProgressTracker(
        os.path.join(output_dir, 'progress.json'), 
        save_frequency=CONFIG['SAVE_FREQUENCY']
    )

    # --- PHASE 1: HERIDAL ---
    if CONFIG['USE_HERIDAL']:
        logger.info("[Step 1] Processing HERIDAL...")
        heridal_data = parse_heridal_csv()
        
        if heridal_data:
            all_files = sorted(list(heridal_data.keys()))
            random.shuffle(all_files)
            
            split_idx = int(len(all_files) * CONFIG['TRAIN_RATIO'])
            process_heridal_split(all_files[:split_idx], heridal_data, 'train', stats, tracker, killer)
            if not killer.kill_now:
                process_heridal_split(all_files[split_idx:], heridal_data, 'val', stats, tracker, killer)
            
        tracker.save()
    else:
        logger.info("[Step 1] HERIDAL processing skipped (disabled).")

    # --- PHASE 2: SARD ---
    if CONFIG['USE_SARD']:
        if not killer.kill_now:
            logger.info("[Step 2] Processing SARD...")
            process_sard(stats)
    else:
        logger.info("[Step 2] SARD processing skipped (disabled).")
    
    # --- PHASE 3: UNITY ---
    if CONFIG['USE_UNITY']:
        if not killer.kill_now:
            logger.info("[Step 3] Processing UNITY Dataset...")
            process_unity(stats)
    else:
        logger.info("[Step 3] UNITY processing skipped (disabled).")

    # Save metrics JSON
    save_metrics(stats, os.path.join(output_dir, 'metrics.json'))

    if killer.kill_now:
        logger.warning("Pipeline interrupted by user. Exiting gracefully.")
        sys.exit(0)

    # --- PHASE 4: VALIDATION & METRICS ---
    try:
        validate_dataset_state()
        analyze_dataset_quality(stats)
    except RuntimeError as e:
        logger.error(f"CRITICAL DATASET FAILURE: {e}")
        return 

    if CONFIG['DRY_RUN']:
        logger.info("DRY RUN COMPLETE. No training initiated.")
        return

    # --- PHASE 5: TRAINING ---
    logger.info("\n" + "=== "*10 + " PIPELINE READY FOR TRAINING " + "=== "*10 + "\n")
    logger.info("[Step 3] Preparing YOLO Training...")
    yaml_path = create_yaml()
    
    weights_path = CONFIG['MODEL_TYPE']
    resume_flag = False
    
    # Advanced Resume Logic
    if CONFIG['RESUME_TRAINING']:
        if CONFIG['RESUME_TYPE'] == 'continue':
            resume_flag = True
            logger.info(f"RESUMING interrupted training run: {weights_path}")
        else:
            resume_flag = False # Fine-tuning starts fresh metrics but inherits weights
            logger.info(f"FINE-TUNING from checkpoint: {weights_path}")

    try:
        log_gpu_stats()
        model = YOLO(weights_path)
        model.train(
            data=yaml_path, epochs=CONFIG['EPOCHS'], imgsz=CONFIG['IMG_SIZE'],
            batch=CONFIG['BATCH_SIZE'], device=0, workers=CONFIG['WORKERS'],
            project=CONFIG['PROJECT_NAME'], name=CONFIG['RUN_NAME'],
            exist_ok=True, resume=resume_flag, seed=CONFIG['SEED'],
            
            # --- GEOMETRIC AUGMENTATIONS ---
            degrees=15.0,        # Slight rotations
            fliplr=0.5,          # Standard horizontal flip
            flipud=0.3,          # Up-down flip (excellent for top-down nadir drone view)
            scale=0.5,           # Zoom variations (vital for varying drone flight altitudes)
            
            # --- COLOR & LIGHTING AUGMENTATIONS ---
            hsv_v=0.4,           # Brightness variations (for shadows/sunlight in the forest)
            
            # --- ADVANCED AUGMENTATIONS & TRAINING LOGIC ---
            mosaic=1.0,          # Combine 4 images to teach objects in different contexts
            close_mosaic=10,     # Turn off mosaic in final 10 epochs for fine-tuning
            erasing=0.05,        # Minimized erasing to avoid wiping out tiny bounding boxes
            label_smoothing=0.05,# Prevent overconfidence, boosting recall on hard/ambiguous cases
            
            # --- LOSS WEIGHTS & VALIDATION (RECALL OPTIMIZATION) ---
            iou=0.40,            # Lower NMS threshold to allow overlapping predictions
            box=10.0,            # Heavy penalty for bounding box inaccuracy/misses
            cls=1.0,             # Heavy penalty for missed class predictions
            
            save_period=5
        )
        logger.info("SUCCESS: Training Complete.")
    except Exception as e:
        logger.error(f"Training Critical Failure: {e}", exc_info=True)

def setup_directories():
    output_dir = get_output_dir()
    subdirs = ['images/train', 'images/val', 'labels/train', 'labels/val']
    if CONFIG['DEBUG_MODE']: subdirs.append('debug_crops')
    for d in subdirs:
        os.makedirs(os.path.join(output_dir, d), exist_ok=True)

def parse_heridal_csv() -> Dict[str, List[List[int]]]:
    csv_path = os.path.join(INPUT_DIR, CONFIG['HERIDAL_CSV'])
    if not os.path.exists(csv_path): return {}
    data = {}
    try:
        with open(csv_path, 'r') as f:
            reader = csv.reader(f)
            next(reader)
            for row in reader:
                if not row or len(row) < 8: continue
                fname, w, h, cls, xmin, ymin, xmax, ymax = row
                if cls != 'human': continue
                if fname not in data: data[fname] = []
                data[fname].append([int(xmin), int(ymin), int(xmax), int(ymax)])
    except Exception as e: logger.error(f"CSV Error: {e}")
    return data

def check_overlap(crop_box: List[int], boxes: List[List[int]]) -> bool:
    cx1, cy1, cx2, cy2 = crop_box
    for b in boxes:
        ix1 = max(cx1, b[0])
        iy1 = max(cy1, b[1])
        ix2 = min(cx2, b[2])
        iy2 = min(cy2, b[3])
        if ix2 > ix1 and iy2 > iy1:
            return True     
    return False

def calculate_crop_coordinates(target_box: List[int], img_shape: Tuple[int, int], crop_size: int, padding: int) -> Tuple[int, int]:
    tx1, ty1, tx2, ty2 = target_box
    h_img, w_img = img_shape
    center_x = (tx1 + tx2) // 2
    center_y = (ty1 + ty2) // 2
    ideal_x = center_x - crop_size // 2
    ideal_y = center_y - crop_size // 2
    min_x = max(0, tx2 + padding - crop_size)
    max_x = min(w_img - crop_size, tx1 - padding)
    min_y = max(0, ty2 + padding - crop_size)
    max_y = min(h_img - crop_size, ty1 - padding)
    if max_x < min_x: start_x = max(0, min(ideal_x, w_img - crop_size))
    else: start_x = random.randint(int(min_x), int(max_x))
    if max_y < min_y: start_y = max(0, min(ideal_y, h_img - crop_size))
    else: start_y = random.randint(int(min_y), int(max_y))
    return start_x, start_y

def process_heridal_split(filenames: List[str], annotations: Dict[str, List[List[int]]], split_name: str, stats: Dict, tracker: ProgressTracker, killer: GracefulKiller):
    root_path = os.path.join(INPUT_DIR, CONFIG['HERIDAL_ROOT'])
    output_dir = get_output_dir()
    gc_counter = 0
    for fname in tqdm(filenames, desc=f"Processing HERIDAL ({split_name})"):
        if killer.kill_now:
            logger.warning("Interrupt signal detected in loop.")
            break
        if CONFIG['RESUME_PREP'] and tracker.is_processed(fname):
            continue
        src = os.path.join(root_path, fname)
        if not os.path.exists(src):
            stats['missing_files'] += 1
            continue
        all_boxes = annotations.get(fname, [])
        valid_boxes = [b for b in all_boxes if (b[2]-b[0]) >= CONFIG['MIN_BOX_SIZE'] and (b[3]-b[1]) >= CONFIG['MIN_BOX_SIZE']]
        for b in valid_boxes: stats['box_sizes'].append((b[2]-b[0], b[3]-b[1]))
        stats['skipped_small'] += (len(all_boxes) - len(valid_boxes))
        needed_bg = 0
        if valid_boxes:
            target_negatives = int(len(valid_boxes) * CONFIG['AUGMENT_COPIES'] * CONFIG['BACKGROUND_RATIO'])
            needed_bg = min(target_negatives, CONFIG['MAX_BG_CROPS'])
            if CONFIG['BACKGROUND_RATIO'] > 0 and needed_bg == 0: needed_bg = 1
        elif CONFIG['BACKGROUND_RATIO'] > 0:
             needed_bg = 1
        if not valid_boxes and needed_bg == 0:
            tracker.mark_processed(fname)
            continue
        img = cv2.imread(src)
        if img is None: continue
        h_img, w_img = img.shape[:2]
        
        for box_idx, target_box in enumerate(valid_boxes):
            for i in range(CONFIG['AUGMENT_COPIES']):
                save_name = f"{os.path.splitext(fname)[0]}_human_{box_idx}_v{i}"
                dest_img = os.path.join(output_dir, 'images', split_name, save_name + '.jpg')
                dest_lbl = os.path.join(output_dir, 'labels', split_name, save_name + '.txt')
                if os.path.exists(dest_img) and os.path.exists(dest_lbl): continue
                start_x, start_y = calculate_crop_coordinates(target_box, (h_img, w_img), CONFIG['CROP_SIZE'], CONFIG['SECURE_PADDING'])
                end_x, end_y = start_x + CONFIG['CROP_SIZE'], start_y + CONFIG['CROP_SIZE']
                crop_img = img[start_y:end_y, start_x:end_x]
                if crop_img.shape[:2] != (CONFIG['CROP_SIZE'], CONFIG['CROP_SIZE']): continue
                valid_labels = []
                for b in all_boxes:
                    bx1, by1, bx2, by2 = b
                    ix1, iy1 = max(start_x, bx1), max(start_y, by1)
                    ix2, iy2 = min(end_x, bx2), min(end_y, by2)
                    if ix2 > ix1 and iy2 > iy1:
                        bw, bh = ix2 - ix1, iy2 - iy1
                        if bw < CONFIG['MIN_VISIBILITY_PIXELS'] or bh < CONFIG['MIN_VISIBILITY_PIXELS']: continue
                        nx, ny = (ix1 - start_x + bw/2)/CONFIG['CROP_SIZE'], (iy1 - start_y + bh/2)/CONFIG['CROP_SIZE']
                        valid_labels.append(f"0 {nx:.6f} {ny:.6f} {bw/CONFIG['CROP_SIZE']:.6f} {bh/CONFIG['CROP_SIZE']:.6f}")
                if valid_labels:
                    try:
                        atomic_write_image(dest_img, crop_img)
                        with open(dest_lbl, 'w') as f: f.write('\n'.join(valid_labels))
                        stats['crops_generated'] += 1
                        if CONFIG['DEBUG_MODE']:
                            debug_path = os.path.join(output_dir, 'debug_crops', f"DEBUG_{save_name}.jpg")
                            visualize_crop_debug(img, [start_x, start_y, end_x, end_y], all_boxes, debug_path)
                    except Exception as e: logger.error(f"Write error: {e}")
        
        generated_bg = 0
        attempts = 0
        max_attempts = needed_bg * CONFIG['BG_MAX_ATTEMPTS_MULTIPLIER']
        if w_img >= CONFIG['CROP_SIZE'] and h_img >= CONFIG['CROP_SIZE']:
            while generated_bg < needed_bg and attempts < max_attempts:
                attempts += 1
                bg_x = random.randint(0, w_img - CONFIG['CROP_SIZE'])
                bg_y = random.randint(0, h_img - CONFIG['CROP_SIZE'])
                if check_overlap([bg_x, bg_y, bg_x+CONFIG['CROP_SIZE'], bg_y+CONFIG['CROP_SIZE']], all_boxes): continue
                crop_bg = img[bg_y:bg_y+CONFIG['CROP_SIZE'], bg_x:bg_x+CONFIG['CROP_SIZE']]
                save_name = f"{os.path.splitext(fname)[0]}_bg_{generated_bg}"
                dest_img = os.path.join(output_dir, 'images', split_name, save_name + '.jpg')
                dest_lbl = os.path.join(output_dir, 'labels', split_name, save_name + '.txt')
                try:
                    atomic_write_image(dest_img, crop_bg)
                    open(dest_lbl, 'w').close()
                    stats['background_crops'] += 1
                    generated_bg += 1
                except Exception as e: logger.error(f"Bg write error: {e}")
        del img
        gc_counter += 1
        if gc_counter % CONFIG['GC_FREQUENCY'] == 0: gc.collect()
        tracker.mark_processed(fname)

def filter_sard_labels(src_path: str, dst_path: str) -> int:
    valid_lines = []
    try:
        with open(src_path, 'r') as f:
            for line in f:
                parts = line.strip().split()
                if len(parts) != 5: continue
                try:
                    cls = int(parts[0])
                    if cls == 0:
                        coords = list(map(float, parts[1:]))
                        if all(0 <= c <= 1 for c in coords):
                            valid_lines.append(line + '\n')
                except ValueError: continue
        with open(dst_path, 'w') as f: f.writelines(valid_lines)
        return len(valid_lines)
    except Exception as e:
        logger.error(f"SARD filter error {src_path}: {e}")
        open(dst_path, 'w').close()
        return 0

def process_sard(stats: Dict):
    sard_root = os.path.join(INPUT_DIR, CONFIG['SARD_ROOT'])
    output_dir = get_output_dir()
    if not os.path.exists(sard_root): return
    files = []
    for sub in ['train', 'valid', 'test']:
        d = os.path.join(sard_root, sub, 'images')
        l = os.path.join(sard_root, sub, 'labels')
        if os.path.exists(d):
            files.extend([(d, l, f) for f in os.listdir(d) if f.endswith('.jpg')])
    random.shuffle(files)
    split_idx = int(len(files) * CONFIG['TRAIN_RATIO'])
    for idx, (img_d, lbl_d, fname) in enumerate(tqdm(files, desc="Processing SARD")):
        split = 'train' if idx < split_idx else 'val'
        dst_img = os.path.join(output_dir, 'images', split, fname)
        dst_lbl = os.path.join(output_dir, 'labels', split, os.path.splitext(fname)[0] + '.txt')
        if CONFIG['RESUME_PREP'] and os.path.exists(dst_img):
            stats['sard_cached'] += 1
            continue
        try:
            shutil.copy2(os.path.join(img_d, fname), dst_img)
            stats['sard_images'] += 1
            src_lbl = os.path.join(lbl_d, os.path.splitext(fname)[0] + '.txt')
            if os.path.exists(src_lbl):
                count = filter_sard_labels(src_lbl, dst_lbl)
                if count == 0: stats['sard_empty_labels'] += 1
            else:
                open(dst_lbl, 'w').close()
                stats['sard_empty_labels'] += 1
        except Exception as e: logger.warning(f"SARD Copy Error {fname}: {e}")

def process_unity(stats: Dict):
    """Processes the synthetic dataset generated from Unity."""
    unity_root = os.path.join(INPUT_DIR, CONFIG['UNITY_ROOT'])
    output_dir = get_output_dir()
    
    if not os.path.exists(unity_root):
        logger.error(f"[Error] Unity dataset root not found: {unity_root}")
        return

    img_dir = os.path.join(unity_root, 'images')
    lbl_dir = os.path.join(unity_root, 'labels')

    if not os.path.exists(img_dir):
        logger.warning(f"No 'images' folder found in {unity_root}")
        return

    # Get all jpg files
    files = [f for f in os.listdir(img_dir) if f.endswith('.jpg')]
    random.shuffle(files)
    
    # Split for train and val based on TRAIN_RATIO
    split_idx = int(len(files) * CONFIG['TRAIN_RATIO'])
    
    for idx, fname in enumerate(tqdm(files, desc="Processing UNITY")):
        split = 'train' if idx < split_idx else 'val'
        dst_img = os.path.join(output_dir, 'images', split, fname)
        dst_lbl = os.path.join(output_dir, 'labels', split, os.path.splitext(fname)[0] + '.txt')
        
        # Check cache
        if CONFIG['RESUME_PREP'] and os.path.exists(dst_img):
            stats['unity_cached'] += 1
            continue
            
        try:
            # Copy Image
            shutil.copy2(os.path.join(img_dir, fname), dst_img)
            stats['unity_images'] += 1
            
            # Copy Label
            src_lbl = os.path.join(lbl_dir, os.path.splitext(fname)[0] + '.txt')
            if os.path.exists(src_lbl):
                shutil.copy2(src_lbl, dst_lbl)
                # Count empty background images
                if os.path.getsize(src_lbl) == 0:
                    stats['unity_empty_labels'] += 1
            else:
                open(dst_lbl, 'w').close()
                stats['unity_empty_labels'] += 1
                
        except Exception as e: 
            logger.warning(f"UNITY Copy Error {fname}: {e}")

def validate_yolo_label(label_path: str) -> bool:
    try:
        if os.path.getsize(label_path) == 0: return True
        with open(label_path, 'r') as f:
            lines = [l.strip() for l in f if l.strip()]
            if not lines: return True
            for line in lines:
                parts = line.split()
                if len(parts) != 5: return False
                cls, x, y, w, h = map(float, parts)
                if int(cls) != 0: return False
                if not (0 <= x <= 1 and 0 <= y <= 1 and 0 < w <= 1 and 0 < h <= 1): return False
        return True
    except: return False

def validate_dataset_state():
    issues = []
    summary = {}
    output_dir = get_output_dir()
    for split in ['train', 'val']:
        img_dir = os.path.join(output_dir, 'images', split)
        lbl_dir = os.path.join(output_dir, 'labels', split)
        if not os.path.exists(img_dir): 
            issues.append(f"Missing {split} image dir")
            continue
        imgs = set(os.listdir(img_dir))
        lbls = set(os.listdir(lbl_dir))
        if len(imgs) == 0: issues.append(f"No images in {split}")
        summary[split] = {'images': len(imgs), 'labels': len(lbls)}
        img_bases = {os.path.splitext(f)[0] for f in imgs}
        lbl_bases = {os.path.splitext(f)[0] for f in lbls}
        missing = img_bases - lbl_bases
        if missing: issues.append(f"{split}: {len(missing)} images missing labels")
        if lbls:
            sample_lbls = random.sample(list(lbls), min(10, len(lbls)))
            for l in sample_lbls:
                if not validate_yolo_label(os.path.join(lbl_dir, l)):
                    issues.append(f"Corrupt/Invalid label found: {l}")
                    break
    if issues: raise RuntimeError(f"Dataset Validation Failed:\n" + "\n".join(issues))
    logger.info("[OK] Dataset Validation Passed:")
    for split, counts in summary.items():
        logger.info(f"  {split}: {counts['images']} images, {counts['labels']} labels")

def analyze_dataset_quality(stats: Dict):
    logger.info("\n" + "="*70)
    logger.info("DATASET QUALITY REPORT")
    logger.info("="*70)
    if stats['box_sizes']:
        ws, hs = zip(*stats['box_sizes'])
        areas = [w*h for w, h in stats['box_sizes']]
        logger.info("\n[1] BOX METRICS (HERIDAL)")
        logger.info(f"  Avg Size: {np.mean(ws):.1f}x{np.mean(hs):.1f} px")
        logger.info(f"  Median Area: {np.median(areas):.1f} px^2")
        tiny = sum(1 for w, h in stats['box_sizes'] if min(w, h) < 20)
        small = sum(1 for w, h in stats['box_sizes'] if 20 <= min(w, h) < 50)
        large = sum(1 for w, h in stats['box_sizes'] if min(w, h) > 150)
        total = len(stats['box_sizes'])
        logger.info(f"  Tiny (<20px):  {tiny:5d} ({tiny/total:.1%})")
        logger.info(f"  Small (20-50): {small:5d} ({small/total:.1%})")
        logger.info(f"  Large (>150px):{large:5d} ({large/total:.1%})")
        if tiny/total > 0.2: logger.warning("  [!] High proportion of tiny boxes!")
    total_heridal = stats['crops_generated'] + stats['background_crops']
    total_unity = stats.get('unity_images', 0)
    total_all = total_heridal + stats['sard_images'] + total_unity
    
    if total_all > 0:
        logger.info("\n[2] COMPOSITION")
        logger.info(f"  HERIDAL Crops:   {total_heridal:5d} ({total_heridal/total_all:.1%})")
        logger.info(f"    - Positive:    {stats['crops_generated']:5d}")
        logger.info(f"    - Negative:    {stats['background_crops']:5d}")
        logger.info(f"  SARD Images:     {stats['sard_images']:5d} ({stats['sard_images']/total_all:.1%})")
        logger.info(f"  UNITY Images:    {total_unity:5d} ({total_unity/total_all:.1%})")
        logger.info(f"    - Empty Lbls:  {stats.get('unity_empty_labels', 0):5d}")
    logger.info("="*70 + "\n")

def create_yaml() -> str:
    output_dir = get_output_dir()
    y_path = os.path.join(output_dir, 'data.yaml')
    with open(y_path, 'w') as f:
        yaml.dump({'path': output_dir, 'train': 'images/train', 'val': 'images/val', 'names': {0: 'person'}}, f)
    return y_path

if __name__ == "__main__":
    main()