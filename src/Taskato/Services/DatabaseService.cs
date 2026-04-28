using SQLite;
using Taskato.Models;

namespace Taskato.Services
{
    /// <summary>
    /// 数据库服务 — 负责 SQLite 数据库的初始化、CRUD 操作
    /// 数据库文件存放在 exe 同级的 Data 子目录下，方便迁移（用户要求）
    /// </summary>
    public class DatabaseService
    {
        /// <summary>
        /// SQLite 异步连接实例（线程安全）
        /// </summary>
        private readonly SQLiteAsyncConnection _db;

        /// <summary>
        /// 数据库文件的完整路径（供外部读取，例如备份/迁移时使用）
        /// </summary>
        public string DatabasePath { get; }

        /// <summary>
        /// 构造函数 — 初始化数据库连接
        /// 数据库文件路径 = exe所在目录/Data/taskato.db
        /// </summary>
        public DatabaseService()
        {
            // 获取 exe 所在目录，在其下创建 Data 文件夹存放数据库
            // 这样用户换电脑时只需拷贝整个程序目录即可完成迁移
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var dataDir = System.IO.Path.Combine(baseDir, "Data");

            // 确保 Data 目录存在
            if (!System.IO.Directory.Exists(dataDir))
                System.IO.Directory.CreateDirectory(dataDir);

            DatabasePath = System.IO.Path.Combine(dataDir, "taskato.db");
            _db = new SQLiteAsyncConnection(DatabasePath);
        }

        /// <summary>
        /// 初始化数据库 — 创建表结构（如果表已存在则不会重复创建）
        /// 应在应用启动时调用一次
        /// </summary>
        public async Task InitializeAsync()
        {
            await _db.CreateTableAsync<TaskItem>();
        }

        // ==================== 任务的 CRUD 操作 ====================

        /// <summary>
        /// 添加一条新任务到数据库
        /// </summary>
        /// <param name="task">任务对象（Id 会自动生成）</param>
        /// <returns>受影响的行数</returns>
        public async Task<int> AddTaskAsync(TaskItem task)
        {
            return await _db.InsertAsync(task);
        }

        /// <summary>
        /// 更新任务状态（防止全量 Update 造成的 CreatedAt 时区解析变异导致消失的 Bug）
        /// </summary>
        public async Task<int> UpdateTaskAsync(TaskItem task)
        {
            return await _db.ExecuteAsync(
                "UPDATE TaskItems SET IsCompleted = ?, CompletedAt = ? WHERE Id = ?",
                task.IsCompleted, task.CompletedAt, task.Id);
        }

        /// <summary>
        /// 删除指定任务
        /// </summary>
        /// <param name="task">要删除的任务对象</param>
        /// <returns>受影响的行数</returns>
        public async Task<int> DeleteTaskAsync(TaskItem task)
        {
            return await _db.DeleteAsync(task);
        }

        /// <summary>
        /// 获取今日创建的所有任务（按创建时间降序排列，最新的在前面）
        /// "今日" = 今天 00:00:00 至明天 00:00:00
        /// </summary>
        /// <returns>今日任务列表</returns>
        public async Task<List<TaskItem>> GetTodayTasksAsync()
        {
            var todayStart = DateTime.Today;              // 今天 00:00:00
            var todayEnd = todayStart.AddDays(1);         // 明天 00:00:00

            return await _db.Table<TaskItem>()
                .Where(t => t.CreatedAt >= todayStart && t.CreatedAt < todayEnd)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
        }

        /// <summary>
        /// 按条件查询历史任务（用于历史查询窗体）
        /// 支持按日期范围和关键词筛选
        /// </summary>
        /// <param name="startDate">开始日期（含）</param>
        /// <param name="endDate">结束日期（含，会自动扩展到当天结尾）</param>
        /// <param name="keyword">搜索关键词（留空则不过滤关键词）</param>
        /// <returns>符合条件的任务列表</returns>
        public async Task<List<TaskItem>> SearchTasksAsync(DateTime startDate, DateTime endDate, string keyword = "")
        {
            // 结束日期扩展到当天 23:59:59，确保包含结束日期当天的所有任务
            var endDateTime = endDate.Date.AddDays(1);

            var query = _db.Table<TaskItem>()
                .Where(t => t.CreatedAt >= startDate.Date && t.CreatedAt < endDateTime);

            // 获取结果后在内存中做关键词过滤
            // （SQLite-net 的 LINQ 不支持 Contains 翻译为 SQL LIKE，所以在应用层过滤）
            var results = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();

            // 如果用户输入了关键词，则进一步过滤标题
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                results = results
                    .Where(t => t.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            return results;
        }

        /// <summary>
        /// 获取所有任务（备份/导出用）
        /// </summary>
        /// <returns>全部任务列表</returns>
        public async Task<List<TaskItem>> GetAllTasksAsync()
        {
            return await _db.Table<TaskItem>()
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
        }
    }
}
