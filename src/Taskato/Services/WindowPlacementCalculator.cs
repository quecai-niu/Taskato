namespace Taskato.Services
{
    /// <summary>
    /// 与 WPF 窗口对象解耦的多屏恢复计算器，便于覆盖屏幕拔插、分辨率变化和离屏坐标。
    /// </summary>
    public static class WindowPlacementCalculator
    {
        public readonly record struct WorkArea(
            double Left,
            double Top,
            double Width,
            double Height,
            bool IsPrimary = false)
        {
            public double Right => Left + Width;
            public double Bottom => Top + Height;
        }

        public readonly record struct Placement(
            double Left,
            double Top,
            double Width,
            double Height);

        public static Placement Calculate(
            double requestedWidth,
            double requestedHeight,
            double? requestedLeft,
            double? requestedTop,
            IReadOnlyList<WorkArea> workAreas)
        {
            if (workAreas.Count == 0)
                throw new ArgumentException("至少需要一个屏幕工作区。", nameof(workAreas));

            requestedWidth = double.IsFinite(requestedWidth) && requestedWidth > 0
                ? requestedWidth
                : 1280;
            requestedHeight = double.IsFinite(requestedHeight) && requestedHeight > 0
                ? requestedHeight
                : 820;
            if (requestedLeft.HasValue && !double.IsFinite(requestedLeft.Value)) requestedLeft = null;
            if (requestedTop.HasValue && !double.IsFinite(requestedTop.Value)) requestedTop = null;

            var fallback = workAreas.FirstOrDefault(area => area.IsPrimary);
            if (fallback.Width <= 0 || fallback.Height <= 0)
                fallback = workAreas[0];

            var target = requestedLeft.HasValue && requestedTop.HasValue
                ? SelectBestArea(
                    requestedLeft.Value,
                    requestedTop.Value,
                    Math.Max(1, requestedWidth),
                    Math.Max(1, requestedHeight),
                    workAreas)
                : fallback;

            var width = Math.Clamp(requestedWidth, Math.Min(960, target.Width), target.Width);
            var height = Math.Clamp(requestedHeight, Math.Min(640, target.Height), target.Height);
            var left = requestedLeft.HasValue
                ? Math.Clamp(requestedLeft.Value, target.Left, Math.Max(target.Left, target.Right - width))
                : target.Left + (target.Width - width) / 2;
            var top = requestedTop.HasValue
                ? Math.Clamp(requestedTop.Value, target.Top, Math.Max(target.Top, target.Bottom - height))
                : target.Top + (target.Height - height) / 2;

            return new Placement(left, top, width, height);
        }

        private static WorkArea SelectBestArea(
            double left,
            double top,
            double width,
            double height,
            IReadOnlyList<WorkArea> workAreas)
        {
            var right = left + width;
            var bottom = top + height;
            var intersecting = workAreas
                .Select(area => new
                {
                    Area = area,
                    Intersection = Math.Max(0, Math.Min(right, area.Right) - Math.Max(left, area.Left)) *
                                   Math.Max(0, Math.Min(bottom, area.Bottom) - Math.Max(top, area.Top))
                })
                .OrderByDescending(candidate => candidate.Intersection)
                .First();
            if (intersecting.Intersection > 0) return intersecting.Area;

            // 屏幕布局改变后选择离原窗口中心最近的工作区，而不是一律跳回主屏。
            var centerX = left + width / 2;
            var centerY = top + height / 2;
            return workAreas
                .OrderBy(area => DistanceSquared(
                    centerX,
                    centerY,
                    Math.Clamp(centerX, area.Left, area.Right),
                    Math.Clamp(centerY, area.Top, area.Bottom)))
                .First();
        }

        private static double DistanceSquared(double x1, double y1, double x2, double y2)
        {
            var x = x1 - x2;
            var y = y1 - y2;
            return x * x + y * y;
        }
    }
}
