# Faster ONNX Models for Background Removal

## Current Models
- **u2net.onnx** (168 MB) - High quality but slow
- **u2netp.onnx** (4.4 MB) - Lighter but still not the fastest

## Recommended Faster Models

### 1. MODNet (RECOMMENDED for Photobooths)
**Download**: https://github.com/ZHKKKe/MODNet/releases
- **File**: `modnet.onnx` (~25-30 MB)
- **Speed**: 3-5x faster than U²-Net
- **Quality**: Excellent for human subjects
- **How to use**: Download and place in this folder

### 2. PP-HumanSeg-Lite (Ultra-Fast)
**Download**: https://github.com/PaddlePaddle/PaddleSeg/tree/release/2.8/contrib/PP-HumanSeg
- **File**: `pp_humanseg_lite.onnx` (~300 KB - 1 MB)
- **Speed**: 10-20x faster
- **Quality**: Good for basic segmentation
- **How to use**: Export from PaddlePaddle or find pre-converted ONNX

### 3. RMBG-1.4 (Modern 2024 Model)
**Download**: https://huggingface.co/briaai/RMBG-1.4
- **File**: `rmbg-1.4.onnx` (~15-40 MB)
- **Speed**: 2-4x faster than U²-Net
- **Quality**: Very good, AI-optimized
- **How to use**: Convert from PyTorch or find ONNX version

### 4. MediaPipe SelfieSegmentation
**Download**: https://google.github.io/mediapipe/solutions/selfie_segmentation
- **File**: `selfie_segmentation.onnx` (~5-10 MB)
- **Speed**: Real-time (30+ FPS)
- **Quality**: Good for portraits/selfies
- **How to use**: Export from TFLite model

## Installation Instructions

1. **Download the model** you want from the links above
2. **Place the .onnx file** in this folder (`Models/BackgroundRemoval/`)
3. **The app will automatically detect** and use the fastest available model based on your quality settings:
   - **Low Quality** → Uses PP-HumanSeg or SelfieSegmentation if available
   - **Medium Quality** → Uses MODNet or RMBG if available
   - **High Quality** → Uses U²-Net or best available

## Model Selection Logic

The app now includes a `BackgroundRemovalModelManager` that:
1. Automatically detects available models
2. Selects the best model based on quality settings
3. Falls back to U²-Net if preferred models aren't available

## Performance Comparison

| Model | Size | Speed vs U²-Net | Quality | Best For |
|-------|------|----------------|---------|----------|
| U²-Net | 168 MB | 1x (baseline) | Excellent | High quality needs |
| U²-Net-P | 4.4 MB | 2.5x | Good | Balanced |
| MODNet | 25-30 MB | 4x | Very Good | Photobooths |
| PP-HumanSeg | <1 MB | 15x | Basic | Speed critical |
| RMBG-1.4 | 15-40 MB | 3x | Very Good | Modern AI |
| SelfieSegmentation | 5-10 MB | 8x | Good | Portraits |

## Quick Start with MODNet (Recommended)

1. Download MODNet ONNX: https://drive.google.com/file/d/1Nf1ZxeJZJL8No2pRSvJ6xTn5YpId8aiZ/view
2. Rename to `modnet.onnx`
3. Place in this folder
4. Set quality to "Medium" in settings
5. Restart the app

The app will automatically use MODNet for 4x faster processing!

## Notes

- Models are loaded based on availability and quality settings
- GPU acceleration (DirectML) works with all models
- Lower resolution models process faster but with reduced edge quality
- For photobooths with human subjects, MODNet offers the best speed/quality balance