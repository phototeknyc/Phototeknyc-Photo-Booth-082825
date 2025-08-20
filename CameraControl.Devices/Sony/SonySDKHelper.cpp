// SonySDKHelper.cpp
// Helper functions to bridge between C# and Sony's C++ SDK
#include <windows.h>
#include <cstdint>
#include <cstring>

// Define the Sony SDK types to match the SDK headers
typedef uint32_t CrInt32u;
typedef uint8_t CrInt8u;
typedef int32_t CrError;

// Forward declare the Sony SDK namespace and class
namespace SCRSDK {
    class CrImageDataBlock {
    public:
        CrImageDataBlock();
        ~CrImageDataBlock();
        
        CrInt32u GetFrameNo() const;
        void SetSize(CrInt32u size);
        CrInt32u GetSize() const;
        void SetData(CrInt8u* data);
        CrInt32u GetImageSize() const;
        CrInt8u* GetImageData() const;
        CrInt32u GetTimeCode() const;
        
    private:
        CrInt32u frameNo;
        CrInt32u size;
        CrInt8u* pData;
        CrInt32u imageSize;
        CrInt32u timeCode;
    };
}

// Import the actual GetLiveViewImage function from Sony SDK
extern "C" __declspec(dllimport) CrError GetLiveViewImage(void* deviceHandle, SCRSDK::CrImageDataBlock* imageData);

// Export helper functions for C#
extern "C" {
    __declspec(dllexport) void* __cdecl CreateImageDataBlock() {
        return new SCRSDK::CrImageDataBlock();
    }
    
    __declspec(dllexport) void __cdecl DestroyImageDataBlock(void* imageData) {
        if (imageData) {
            delete static_cast<SCRSDK::CrImageDataBlock*>(imageData);
        }
    }
    
    __declspec(dllexport) void __cdecl SetImageDataBlockSize(void* imageData, CrInt32u size) {
        if (imageData) {
            static_cast<SCRSDK::CrImageDataBlock*>(imageData)->SetSize(size);
        }
    }
    
    __declspec(dllexport) void __cdecl SetImageDataBlockData(void* imageData, CrInt8u* data) {
        if (imageData) {
            static_cast<SCRSDK::CrImageDataBlock*>(imageData)->SetData(data);
        }
    }
    
    __declspec(dllexport) CrInt32u __cdecl GetImageDataBlockImageSize(void* imageData) {
        if (imageData) {
            return static_cast<SCRSDK::CrImageDataBlock*>(imageData)->GetImageSize();
        }
        return 0;
    }
    
    __declspec(dllexport) CrInt8u* __cdecl GetImageDataBlockImageData(void* imageData) {
        if (imageData) {
            return static_cast<SCRSDK::CrImageDataBlock*>(imageData)->GetImageData();
        }
        return nullptr;
    }
    
    __declspec(dllexport) CrError __cdecl GetLiveViewImageHelper(void* deviceHandle, void* imageData) {
        if (deviceHandle && imageData) {
            return GetLiveViewImage(deviceHandle, static_cast<SCRSDK::CrImageDataBlock*>(imageData));
        }
        return 0x8001; // CrError_Generic
    }
    
    __declspec(dllexport) void __cdecl CopyImageData(void* imageData, CrInt8u* destBuffer, CrInt32u bufferSize) {
        if (imageData && destBuffer) {
            SCRSDK::CrImageDataBlock* block = static_cast<SCRSDK::CrImageDataBlock*>(imageData);
            CrInt32u imageSize = block->GetImageSize();
            CrInt8u* imagePtr = block->GetImageData();
            
            if (imagePtr && imageSize > 0 && imageSize <= bufferSize) {
                memcpy(destBuffer, imagePtr, imageSize);
            }
        }
    }
}