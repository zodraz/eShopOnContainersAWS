﻿namespace Ordering.BackgroundTasks
{
    public class BackgroundTaskSettings
    {
        public string ConnectionString { get; set; }

        public int GracePeriodTime { get; set; }

        public int CheckUpdateTime { get; set; }
    }
}
