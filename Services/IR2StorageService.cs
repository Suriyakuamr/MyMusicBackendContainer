using System.IO;
using System.Threading.Tasks;

namespace MusicPlatform.API.Services
{
    public interface IR2StorageService
    {
        Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType);
        Task<(Stream Stream, string ContentType, long ContentLength)> GetFileStreamAsync(string fileKey);
        Task DeleteFileAsync(string fileKey);
    }
}
