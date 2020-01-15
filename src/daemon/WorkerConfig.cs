using System;

namespace filewatcher
{
    public class WorkerConfig 
    {
        public string DaemonName { get; set; }
        public string DownloadsPath { get; set; }
        public string RsyncServer { get; set; }
        public string Server { get; set; }
        public Int32 Port { get; set; }
        public int MaxDownloads { get; set; }

    }
}