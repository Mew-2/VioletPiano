namespace VioletPiano.Services
{
    using Microsoft.AspNetCore.Mvc;

    // Services/FileDownloadService.cs
    using Microsoft.AspNetCore.StaticFiles;

    public class FileDownloadService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly IContentTypeProvider _contentTypeProvider;

        public FileDownloadService(IWebHostEnvironment environment)
        {
            _environment = environment;
            _contentTypeProvider = new FileExtensionContentTypeProvider();
        }

        public FileContentResult GetFileDownloadResult(string filePath, string downloadFileName = null)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("文件不存在");
            }

            var fileBytes = File.ReadAllBytes(filePath);
            var fileName = downloadFileName ?? Path.GetFileName(filePath);

            // 获取Content-Type
            if (!_contentTypeProvider.TryGetContentType(fileName, out var contentType))
            {
                contentType = "application/octet-stream";
            }

            return new FileContentResult(fileBytes, contentType)
            {
                FileDownloadName = fileName
            };
        }

        public string GetRelativeUrl(string absolutePath)
        {
            var relativePath = absolutePath.Replace(_environment.WebRootPath, "").Replace("\\", "/").TrimStart('/');
            return $"/{relativePath}";
        }
    }
}