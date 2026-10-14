using Taskato.Models;

namespace Taskato.Services
{
    public partial class DatabaseService
    {
        public async Task<HashSet<int>> GetTaskIdsWithImagesAsync()
        {
            var images = await _db.QueryAsync<TaskImage>("SELECT DISTINCT TaskId FROM TaskImages");
            return images.Select(image => image.TaskId).ToHashSet();
        }

        // 列表只读取缩略图，避免切换任务时加载全部原图。
        public Task<List<TaskImage>> GetTaskImagesAsync(int taskId) =>
            _db.QueryAsync<TaskImage>(
                "SELECT Id, TaskId, FileName, ContentType, Thumbnail FROM TaskImages WHERE TaskId = ? ORDER BY Id", taskId);

        public Task<TaskImage> GetTaskImageAsync(int taskId, int imageId) =>
            _db.Table<TaskImage>().Where(image => image.TaskId == taskId && image.Id == imageId).FirstOrDefaultAsync();

        public async Task AddTaskImageAsync(TaskImage image)
        {
            await _db.RunInTransactionAsync(connection =>
            {
                if (connection.Find<TaskItem>(image.TaskId) is null)
                    throw new InvalidOperationException("任务已删除，无法添加图片。");
                connection.Insert(image);
            });
        }

        public Task<int> DeleteTaskImageAsync(int taskId, int imageId) =>
            _db.ExecuteAsync("DELETE FROM TaskImages WHERE TaskId = ? AND Id = ?", taskId, imageId);
    }
}
