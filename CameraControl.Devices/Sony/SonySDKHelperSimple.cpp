// SonySDKHelperSimple.cpp
// Simplified helper that loads Sony SDK at runtime
#include <windows.h>
#include <cstdint>
#include <cstring>

typedef uint32_t CrInt32u;
typedef uint8_t CrInt8u;
typedef int32_t CrError;

// Function pointers for Sony SDK
typedef void* (*CreateImageDataBlockFunc)();
typedef void (*DestroyImageDataBlockFunc)(void*);
typedef void (*SetSizeFunc)(void*, CrInt32u);
typedef void (*SetDataFunc)(void*, CrInt8u*);
typedef CrInt32u (*GetImageSizeFunc)(void*);
typedef CrInt8u* (*GetImageDataFunc)(void*);
typedef CrError (*GetLiveViewImageFunc)(void*, void*);

static HMODULE hSonyDll = NULL;
static CreateImageDataBlockFunc pCreateImageDataBlock = NULL;
static DestroyImageDataBlockFunc pDestroyImageDataBlock = NULL;
static SetSizeFunc pSetSize = NULL;
static SetDataFunc pSetData = NULL;
static GetImageSizeFunc pGetImageSize = NULL;
static GetImageDataFunc pGetImageData = NULL;
static GetLiveViewImageFunc pGetLiveViewImage = NULL;

// Simple wrapper class for CrImageDataBlock
class ImageDataBlockWrapper {
public:
    CrInt32u frameNo;
    CrInt32u size;
    CrInt8u* pData;
    CrInt32u imageSize;
    CrInt32u timeCode;
    
    ImageDataBlockWrapper() : frameNo(0), size(0), pData(nullptr), imageSize(0), timeCode(0) {}
};

extern "C" {
    __declspec(dllexport) void* __cdecl CreateImageDataBlock() {
        // Return a simple wrapper that simulates the CrImageDataBlock structure
        return new ImageDataBlockWrapper();
    }
    
    __declspec(dllexport) void __cdecl DestroyImageDataBlock(void* imageData) {
        if (imageData) {
            delete static_cast<ImageDataBlockWrapper*>(imageData);
        }
    }
    
    __declspec(dllexport) void __cdecl SetImageDataBlockSize(void* imageData, CrInt32u size) {
        if (imageData) {
            static_cast<ImageDataBlockWrapper*>(imageData)->size = size;
        }
    }
    
    __declspec(dllexport) void __cdecl SetImageDataBlockData(void* imageData, CrInt8u* data) {
        if (imageData) {
            static_cast<ImageDataBlockWrapper*>(imageData)->pData = data;
        }
    }
    
    __declspec(dllexport) CrInt32u __cdecl GetImageDataBlockImageSize(void* imageData) {
        if (imageData) {
            return static_cast<ImageDataBlockWrapper*>(imageData)->imageSize;
        }
        return 0;
    }
    
    __declspec(dllexport) CrInt8u* __cdecl GetImageDataBlockImageData(void* imageData) {
        if (imageData) {
            return static_cast<ImageDataBlockWrapper*>(imageData)->pData;
        }
        return nullptr;
    }
    
    __declspec(dllexport) CrError __cdecl GetLiveViewImageHelper(void* deviceHandle, void* imageData) {
        // Load Sony DLL if not already loaded
        if (!hSonyDll) {
            hSonyDll = LoadLibraryA("Cr_Core.dll");
            if (!hSonyDll) {
                return 0x8001; // CrError_Generic
            }
            
            // Get the GetLiveViewImage function
            pGetLiveViewImage = (GetLiveViewImageFunc)GetProcAddress(hSonyDll, "GetLiveViewImage");
            if (!pGetLiveViewImage) {
                return 0x8001; // CrError_Generic
            }
        }
        
        if (deviceHandle && imageData && pGetLiveViewImage) {
            // Call the Sony SDK function with our wrapper
            return pGetLiveViewImage(deviceHandle, imageData);
        }
        return 0x8001; // CrError_Generic
    }
    
    __declspec(dllexport) void __cdecl CopyImageData(void* imageData, CrInt8u* destBuffer, CrInt32u bufferSize) {
        if (imageData && destBuffer) {
            ImageDataBlockWrapper* wrapper = static_cast<ImageDataBlockWrapper*>(imageData);
            if (wrapper->pData && wrapper->imageSize > 0 && wrapper->imageSize <= bufferSize) {
                memcpy(destBuffer, wrapper->pData, wrapper->imageSize);
            }
        }
    }
}