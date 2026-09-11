using System.Threading.Tasks;

namespace LuckyDogRise;

public interface IPlatformScreenshotService
{
    // True only after ScreenshotReady confirms the image is in the local Steam library.
    Task<bool> SaveScreenshotAsync(byte[] rgb, int width, int height);
}
