# PP-LiteSeg Model Download Instructions

## Overview
PP-LiteSeg is an ultra-fast semantic segmentation model that can achieve 273 FPS for real-time background removal in live view.

## Download Links

### Option 1: PaddlePaddle Model Zoo
1. Download the ONNX model from PaddleSeg repository:
   - Model: `pp_liteseg_stdc1_cityscapes_1024x512_scale1.0_160k`
   - URL: https://github.com/PaddlePaddle/PaddleSeg/tree/release/2.8/configs/pp_liteseg

### Option 2: Pre-converted ONNX Models
You can find pre-converted ONNX versions:
- Hugging Face Model Hub: Search for "pp-liteseg onnx"
- ONNX Model Zoo: Check for PP-LiteSeg variants

## Model Requirements
- Input Size: 256x256 (configured for speed)
- Input Format: RGB image normalized with ImageNet stats
- Output: Single channel segmentation mask

## Installation Steps

1. Download the `pp_liteseg.onnx` file
2. Place it in this directory: `/Models/BackgroundRemoval/`
3. The file should be named exactly: `pp_liteseg.onnx`

## Model Specifications
- File Size: ~1-2MB (ultra-lightweight)
- Speed: Up to 273 FPS on GPU
- Optimized for: Human segmentation in live view
- Input normalization:
  - Mean: [0.485, 0.456, 0.406]
  - Std: [0.229, 0.224, 0.225]

## Testing
After placing the model file, the application will automatically detect and use PP-LiteSeg for live view background removal when available.

## Alternative Models
If PP-LiteSeg is not available, the system will fall back to MODNet for both live view and capture.