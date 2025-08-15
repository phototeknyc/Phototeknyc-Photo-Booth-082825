using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Photobooth.Services
{
    /// <summary>
    /// Share service interface for cloud and local implementations
    /// </summary>
    public interface IShareService
    {
        Task<ShareResult> CreateShareableGalleryAsync(string sessionId, List<string> photoPaths);
        Task<bool> SendSMSAsync(string phoneNumber, string galleryUrl);
        BitmapImage GenerateQRCode(string url);
    }
}