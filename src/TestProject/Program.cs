using System;
using System.Threading.Tasks;
using Taskato.Models;
using Taskato.Services;

class Program {
    static async Task Main() {
        var db = new DatabaseService();
        await db.InitializeAsync();
        
        var t1 = new TaskItem { Title = "Test Yesterday Completed", CreatedAt = DateTime.Today.AddDays(-1), CompletedAt = DateTime.Now, Priority = 1 };
        var t2 = new TaskItem { Title = "Test Today", CreatedAt = DateTime.Today, Priority = 1 };
        await db.AddTaskAsync(t1);
        await db.AddTaskAsync(t2);
        
        Console.WriteLine("Before update: " + (await db.GetTodayTasksAsync()).Count);
        
        t1.Priority = 3;
        await db.UpdateTaskAsync(t1);
        
        try {
            Console.WriteLine("After update: " + (await db.GetTodayTasksAsync()).Count);
        } catch (Exception ex) {
            Console.WriteLine("Exception: " + ex);
        }
    }
}
