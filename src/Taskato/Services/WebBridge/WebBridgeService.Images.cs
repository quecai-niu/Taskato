using System.Text.Json;
using Taskato.Models;

namespace Taskato.Services.WebBridge
{
    public sealed partial class WebBridgeService
    {
        private async Task<object> ListTaskImagesAsync(JsonElement parameters)
        {
            var task = await FindTaskAsync(parameters);
            var images = await _database.GetTaskImagesAsync(task.Id);
            return images.Select(image => new
            {
                image.Id,
                image.FileName,
                thumbnailUrl = "data:image/png;base64," + Convert.ToBase64String(image.Thumbnail)
            }).ToList();
        }

        private async Task<object> AddTaskImageAsync(JsonElement parameters)
        {
            var task = await FindTaskAsync(parameters);
            var data = ReadImageBytes(parameters, "data", 10 * 1024 * 1024);
            var thumbnail = ReadImageBytes(parameters, "thumbnail", 256 * 1024);
            var contentType = RequiredString(parameters, "contentType");
            if (!HasImageSignature(data, contentType) || !HasImageSignature(thumbnail, "image/png"))
                throw new BridgeRequestException("invalid_image", "图片格式无效，支持 PNG、JPEG、GIF、BMP 和 WebP。");
            var fileName = RequiredString(parameters, "fileName");
            if (fileName.Length > 255)
                throw new BridgeRequestException("invalid_image", "图片文件名不能超过 255 个字符。");

            var image = new TaskImage
            {
                TaskId = task.Id,
                FileName = fileName,
                ContentType = contentType,
                Data = data,
                Thumbnail = thumbnail
            };
            await _database.AddTaskImageAsync(image);
            PostEvent("tasks.images.changed", new { taskId = task.Id });
            return new { image.Id };
        }

        private async Task<object> GetTaskImageAsync(JsonElement parameters)
        {
            var task = await FindTaskAsync(parameters);
            var image = await _database.GetTaskImageAsync(task.Id, RequiredInt(parameters, "imageId"));
            if (image is null)
                throw new BridgeRequestException("image_not_found", "图片已删除。");
            return new { url = $"data:{image.ContentType};base64,{Convert.ToBase64String(image.Data)}" };
        }

        private async Task<object> DeleteTaskImageAsync(JsonElement parameters)
        {
            var task = await FindTaskAsync(parameters);
            var affected = await _database.DeleteTaskImageAsync(task.Id, RequiredInt(parameters, "imageId"));
            PostEvent("tasks.images.changed", new { taskId = task.Id });
            return new { affected };
        }

        private static byte[] ReadImageBytes(JsonElement parameters, string name, int maximumBytes)
        {
            var value = RequiredString(parameters, name);
            if (value.Length > ((maximumBytes + 2) / 3) * 4)
                throw new BridgeRequestException("image_too_large", "图片超过大小限制，原图最多 10 MB。");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(value); }
            catch (FormatException)
            {
                throw new BridgeRequestException("invalid_image", "图片数据无法解析。");
            }
            if (bytes.Length == 0 || bytes.Length > maximumBytes)
                throw new BridgeRequestException("image_too_large", "图片为空或超过大小限制，原图最多 10 MB。");
            return bytes;
        }

        private static bool HasImageSignature(byte[] data, string contentType)
        {
            return contentType switch
            {
                "image/png" => data.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
                "image/jpeg" => data.AsSpan().StartsWith(new byte[] { 255, 216, 255 }),
                "image/gif" => data.AsSpan().StartsWith("GIF87a"u8) || data.AsSpan().StartsWith("GIF89a"u8),
                "image/bmp" => data.AsSpan().StartsWith("BM"u8),
                "image/webp" => data.Length >= 12 && data.AsSpan().StartsWith("RIFF"u8) && data.AsSpan(8).StartsWith("WEBP"u8),
                _ => false
            };
        }
    }
}
