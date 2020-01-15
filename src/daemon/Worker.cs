using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace filewatcher
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly WorkerConfig _options;

        public Worker(ILogger<Worker> logger, WorkerConfig options)
        {
            _logger = logger;
            _options = options;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            string downloadsPath = _options.DownloadsPath;
            string server = _options.Server;
            string rsyncServer = _options.RsyncServer;
            string path = FilePath.RemoveTrailingSlash(_options.DownloadsPath);
            Int32 port = _options.Port;

            _logger.LogInformation($"Connecting to {server}:{port}...", DateTimeOffset.Now);
            var connection = new Connection(server, port, "filewatcher", path, rsyncServer, _logger);
            await connection.Connect(stoppingToken);
        }
    }
}
