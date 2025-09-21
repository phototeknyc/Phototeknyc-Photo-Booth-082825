# Direct Download Links for Faster Background Removal Models

## 🚀 Quick Start - MODNet (BEST FOR PHOTOBOOTHS)

### Option 1: Pre-converted ONNX (Easiest)
**Direct Download**: https://github.com/RobustVideoMatting/RobustVideoMatting/releases/download/v1.0.0/modnet_photographic_portrait_matting.onnx
- Click link → Download → Rename to `modnet.onnx` → Place in this folder

### Option 2: From Original Repository
**GitHub Release**: https://github.com/ZHKKKe/MODNet/tree/master/onnx
- Download `modnet_photographic_portrait_matting.onnx`
- Place in this folder

### Option 3: Google Drive Mirror
**Google Drive**: https://drive.google.com/drive/folders/1umYmlCulvIFNaqPjwod1SayFmSRHziyR
- Download the ONNX model
- Rename to `modnet.onnx`

---

## 💨 PP-HumanSeg-Lite (ULTRA FAST - 15x Faster)

### Direct Download
**Paddle2ONNX Converted Models**:
```bash
# Download directly using wget or curl
wget https://bj.bcebos.com/paddlehub/fastdeploy/PP_HumanSegV2_Lite_192x192_infer.onnx
# Rename to pp_humanseg_lite.onnx
```

### Alternative Sources
1. **PaddleHub Models**: https://github.com/PaddlePaddle/PaddleHub/tree/develop/modules/image/semantic_segmentation
2. **Baidu Cloud**: https://paddleseg.bj.bcebos.com/dygraph/pp_humanseg_v2/pp_humanseg_v2_lite_192x192.zip
   - Extract and convert to ONNX using paddle2onnx

---

## 🎯 RMBG-1.4 (Latest 2024 Model)

### Hugging Face (Official)
**Direct Link**: https://huggingface.co/briaai/RMBG-1.4/tree/main
1. Download `model.onnx` from Files tab
2. Rename to `rmbg-1.4.onnx`
3. Place in this folder

### Using Git LFS
```bash
git clone https://huggingface.co/briaai/RMBG-1.4
cd RMBG-1.4
# The ONNX file will be in the folder
```

---

## 🤳 MediaPipe Selfie Segmentation

### Pre-converted ONNX Models
**GitHub Repository**: https://github.com/PINTO0309/PINTO_model_zoo/tree/main/144_selfie_segmentation
1. Navigate to the repository
2. Download `selfie_segmentation_landscape.onnx` (best quality)
3. Rename to `selfie_segmentation.onnx`

### Alternative Download
**Direct from PINTO Model Zoo**:
```bash
wget https://github.com/PINTO0309/PINTO_model_zoo/raw/main/144_selfie_segmentation/selfie_segmentation_landscape.onnx
```

---

## 🔧 Alternative: Convert Your Own Models

### For MODNet (Python Required)
```python
# Install requirements
pip install onnx torch torchvision

# Download and convert
import torch
from modnet import MODNet

# Load pretrained model
model = MODNet(backbone_pretrained=True)
model.load_state_dict(torch.load('modnet_photographic_portrait_matting.ckpt'))

# Export to ONNX
dummy_input = torch.randn(1, 3, 512, 512)
torch.onnx.export(model, dummy_input, "modnet.onnx", opset_version=11)
```

### For PaddlePaddle Models
```bash
# Install paddle2onnx
pip install paddle2onnx

# Convert
paddle2onnx --model_dir pp_humanseg_v2_lite \
            --model_filename model.pdmodel \
            --params_filename model.pdiparams \
            --save_file pp_humanseg_lite.onnx
```

---

## 📦 Model Packs (Multiple Models)

### Community Collection
**GitHub - Background Removal Models**: https://github.com/danielgatis/rembg/tree/main/rembg/sessions
- Contains multiple pre-trained models
- Download the `.onnx` files directly

### ONNX Model Zoo
**Official ONNX Models**: https://github.com/onnx/models/tree/main/vision/body_analysis
- Various segmentation models
- Pre-optimized for ONNX Runtime

---

## ✅ Installation Checklist

1. **Choose a model** based on your needs:
   - **Best Quality/Speed Balance**: MODNet
   - **Fastest**: PP-HumanSeg-Lite
   - **Latest AI**: RMBG-1.4
   - **Portraits**: Selfie Segmentation

2. **Download the .onnx file** from links above

3. **Place in this folder**: `Models/BackgroundRemoval/`

4. **Verify filename**:
   - `modnet.onnx`
   - `pp_humanseg_lite.onnx`
   - `rmbg-1.4.onnx`
   - `selfie_segmentation.onnx`

5. **Restart the application**

6. **Set quality in settings**:
   - Low → Uses fastest available
   - Medium → Uses MODNet if available
   - High → Uses best quality available

---

## 🚨 Troubleshooting

### Model Not Loading?
1. Check file is actually `.onnx` format (not `.pth` or `.pb`)
2. Ensure filename matches exactly (case-sensitive)
3. Check file isn't corrupted (should be several MB)

### Still Using U²-Net?
1. Check debug logs for model detection
2. Verify model file is in correct folder
3. Ensure quality setting matches model tier

### Need Help Converting?
- Use online converters: https://convertmodel.com/
- Or use ONNX conversion tools: https://onnx.ai/

---

## 📊 Model Comparison Table

| Model | Direct Download | Size | Speed | Ready-to-Use |
|-------|----------------|------|--------|--------------|
| MODNet | ✅ Yes | 25MB | 4x | ✅ ONNX Available |
| PP-HumanSeg | ✅ Yes | <1MB | 15x | ✅ ONNX Available |
| RMBG-1.4 | ✅ Yes | 40MB | 3x | ✅ ONNX Available |
| Selfie Seg | ✅ Yes | 10MB | 8x | ✅ ONNX Available |

---

## 💡 Quick Test

After downloading a model:
1. Place it in this folder
2. Run the app with debug logging enabled
3. Look for: `[BackgroundRemoval] Available models: MODNet, U2Net`
4. If your model appears, it's working!

---

## 📝 Notes

- All models work with DirectML GPU acceleration
- Models are loaded once at startup for efficiency
- The app automatically selects the best available model
- You can have multiple models installed simultaneously