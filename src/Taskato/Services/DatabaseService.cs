using System.Linq;
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

            // 数据库迁移：为旧数据库添加 LastModifiedAt 列
            var colCheck = await _db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM pragma_table_info('TaskItems') WHERE name='LastModifiedAt'");
            if (colCheck == 0)
            {
                await _db.ExecuteAsync("ALTER TABLE TaskItems ADD COLUMN LastModifiedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'");
            }
        }

        /// <summary>
        /// 全局数据变更事件 — 当添加、更新或删除任务时触发
        /// 用于通知各个界面（如主页和历史列表）同步刷新数据
        /// </summary>
        public event Action? OnDataChanged;

        // ==================== 任务的 CRUD 操作 ====================

        /// <summary>
        /// 添加一条新任务到数据库
        /// </summary>
        /// <param name="task">任务对象（Id 会自动生成）</param>
        /// <returns>受影响的行数</returns>
        public async Task<int> AddTaskAsync(TaskItem task)
        {
            var result = await _db.InsertAsync(task);
            if (result > 0) OnDataChanged?.Invoke(); // 触发全局刷新通知
            return result;
        }

        /// <summary>
        /// 更新任务内容（支持标题、完成状态、优先级、排序索引的全量更新）
        /// </summary>
        public async Task<int> UpdateTaskAsync(TaskItem task)
        {
            task.LastModifiedAt = DateTime.Now;

            // 使用标准的 ORM 更新方法，避免手写 SQL 可能带来的参数绑定（如 DateTime?, bool）隐患
            var result = await _db.UpdateAsync(task);
            
            if (result > 0) OnDataChanged?.Invoke(); // 触发全局刷新通知
            return result;
        }

        /// <summary>
        /// 删除指定任务
        /// </summary>
        /// <param name="task">要删除的任务对象</param>
        /// <returns>受影响的行数</returns>
        public async Task<int> DeleteTaskAsync(TaskItem task)
        {
            var result = await _db.DeleteAsync(task);
            if (result > 0) OnDataChanged?.Invoke(); // 触发全局刷新通知
            return result;
        }

        /// <summary>
        /// 获取今日创建的所有任务
        /// 排序规则：1. 优先级降序；2. 手动排序索引升序；3. 创建时间降序
        /// </summary>
        /// <returns>今日任务列表</returns>
        public async Task<List<TaskItem>> GetTodayTasksAsync()
        {
            var todayStart = DateTime.Today;              // 今天 00:00:00

            // 获取条件：“今天创建的任务 (无论是否完成) + 过去未完成的任务”
            // 排序规则：1. 优先级降序；2. 手动排序索引升序；3. 创建时间降序
            return await _db.Table<TaskItem>()
                .Where(t => t.CreatedAt >= todayStart || t.IsCompleted == false)
                .OrderByDescending(t => t.Priority)     // 级别高在前
                .ThenBy(t => t.OrderIndex)               // 拖拽序号越小越靠前
                .ThenByDescending(t => t.CreatedAt)      // 最后按时间倒序
                .ToListAsync();
        }

        /// <summary>
        /// 按条件查询历史任务（用于历史查询窗体）
        /// 支持状态筛选、多字段排序、等级优先
        /// </summary>
        public async Task<List<TaskItem>> SearchTasksAsync(
            DateTime startDate, DateTime endDate, string keyword = "",
            int statusFilter = 0, string sortField = "CreatedAt",
            bool sortDesc = true, bool priorityFirst = false)
        {
            var endDateTime = endDate.Date.AddDays(1);

            var query = _db.Table<TaskItem>()
                .Where(t => t.CreatedAt >= startDate.Date && t.CreatedAt < endDateTime);

            var results = await query.ToListAsync();

            // 关键词筛选
            if (!string.IsNullOrWhiteSpace(keyword))
                results = results.Where(t => t.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();

            // 状态筛选
            if (statusFilter == 1)
                results = results.Where(t => !t.IsCompleted).ToList();
            else if (statusFilter == 2)
                results = results.Where(t => t.IsCompleted).ToList();

            // 排序
            results = ApplySort(results, sortField, sortDesc, priorityFirst);
            return results;
        }

        /// <summary>
        /// 查询指定日期内完成的所有任务（不限创建日期）
        /// </summary>
        public async Task<List<TaskItem>> GetTasksCompletedOnAsync(DateTime date)
        {
            var dayStart = date.Date;
            var dayEnd = dayStart.AddDays(1);
            return await _db.Table<TaskItem>()
                .Where(t => t.CompletedAt >= dayStart && t.CompletedAt < dayEnd)
                .ToListAsync();
        }

        /// <summary>
        /// 查询指定日期及之前创建的未完成任务
        /// </summary>
        public async Task<List<TaskItem>> GetUncompletedCreatedUpToAsync(DateTime date)
        {
            var dayEnd = date.Date.AddDays(1);
            return await _db.Table<TaskItem>()
                .Where(t => t.CreatedAt < dayEnd && !t.IsCompleted)
                .ToListAsync();
        }

        private static List<TaskItem> ApplySort(List<TaskItem> tasks, string sortField, bool sortDesc, bool priorityFirst)
        {
            IOrderedEnumerable<TaskItem> ordered;
            if (priorityFirst)
            {
                ordered = sortDesc
                    ? tasks.OrderByDescending(t => t.Priority)
                    : tasks.OrderBy(t => t.Priority);
                ordered = ThenByField(ordered, sortField, sortDesc);
            }
            else
            {
                ordered = OrderByField(tasks, sortField, sortDesc);
            }
            return ordered.ToList();
        }

        private static IOrderedEnumerable<TaskItem> OrderByField(IEnumerable<TaskItem> source, string field, bool desc)
        {
            return field switch
            {
                "CompletedAt" => desc
                    ? source.OrderByDescending(t => t.CompletedAt ?? DateTime.MinValue)
                    : source.OrderBy(t => t.CompletedAt ?? DateTime.MinValue),
                "LastModifiedAt" => desc
                    ? source.OrderByDescending(t => t.LastModifiedAt)
                    : source.OrderBy(t => t.LastModifiedAt),
                _ => desc
                    ? source.OrderByDescending(t => t.CreatedAt)
                    : source.OrderBy(t => t.CreatedAt),
            };
        }

        private static IOrderedEnumerable<TaskItem> ThenByField(IOrderedEnumerable<TaskItem> source, string field, bool desc)
        {
            return field switch
            {
                "CompletedAt" => desc
                    ? source.ThenByDescending(t => t.CompletedAt ?? DateTime.MinValue)
                    : source.ThenBy(t => t.CompletedAt ?? DateTime.MinValue),
                "LastModifiedAt" => desc
                    ? source.ThenByDescending(t => t.LastModifiedAt)
                    : source.ThenBy(t => t.LastModifiedAt),
                _ => desc
                    ? source.ThenByDescending(t => t.CreatedAt)
                    : source.ThenBy(t => t.CreatedAt),
            };
        }

        /// <summary>
        /// 获取所有任务（带多级排序）
        /// </summary>
        public async Task<List<TaskItem>> GetAllTasksAsync()
        {
            return await _db.Table<TaskItem>()
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.OrderIndex)
                .ThenByDescending(t => t.CreatedAt)
                .ToListAsync();
        }
    }
}
