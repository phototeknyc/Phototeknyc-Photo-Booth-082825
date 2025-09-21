Background Removal Models
=========================

This directory contains ONNX models for ML-powered background removal.

Required Models:
----------------
1. u2net.onnx (176 MB)
   - High-quality U2-Net model for photo capture
   - Best accuracy but slower processing
   - Used for final captured photos

2. u2netp.onnx (4.7 MB)
   - Lightweight U2-Net model for real-time processing
   - Optimized for live view (15-30 FPS)
   - Lower quality but fast enough for preview

To download models:
-------------------
Run the PowerShell script:
  .\download_models.ps1

Or manually download from:
- U2-Net: https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2net.onnx
- U2-Net Lite: https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx

Model Licenses:
--------------
- U2-Net: Apache 2.0 License
- Models from rembg project by danielgatis

Performance Notes:
-----------------
- GPU acceleration via DirectML provides 3-5x speedup
- CPU processing works but may be slower for live view
- Recommended: NVIDIA GPU with 4GB+ VRAM for best performance