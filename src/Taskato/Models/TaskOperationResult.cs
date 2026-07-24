namespace Taskato.Models
{
    public enum TaskDeleteMode
    {
        DeleteBranch,
        PromoteChildren
    }

    public sealed class TaskCompletionResult
    {
        public bool Succeeded { get; init; }
        public int UncompletedDescendantCount { get; init; }
        public IReadOnlyList<int> CompletedTaskIds { get; init; } = Array.Empty<int>();
    }

    public sealed class TaskMoveResult
    {
        public bool Succeeded { get; init; }
        public string ErrorMessage { get; init; } = string.Empty;
    }
}
