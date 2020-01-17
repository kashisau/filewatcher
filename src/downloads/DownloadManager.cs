using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace filewatcher
{
    class DownloadManager
    {
        readonly ILogger logger;
        readonly string rsyncServer;
        readonly string destinationPath;
        readonly int maxSimultaneous;
        SemaphoreSlim semaphore;

        public DownloadManager(string destinationPath, string rsyncServer, ILogger logger, int maxSimultaneous = 2)
        {
            this.destinationPath = destinationPath;
            this.rsyncServer = rsyncServer;
            this.logger = logger;
            this.maxSimultaneous = maxSimultaneous;
            
            semaphore = new SemaphoreSlim(0, maxSimultaneous);
        }

        public List<string> InitialSync(List<FilePath> fwdFileList)
        {
            fwdFileList.ForEach(
              fwdRemoteFile => {
                  string localPath = destinationPath + fwdRemoteFile.Relative;
                  // FilePath.CreateDirectoryPath(localPath, logger);
                  var download = new RsyncDownload(fwdRemoteFile.Absolute, localPath, rsyncServer, logger);
                  // DownloadResult downloadResult = await download.DownloadAsync(new CancellationToken());
                  // logger.LogInformation($"Download result: {downloadResult.DownloadState}");
              }
            );
        }
        Task WatchForNewFiles(NetworkStream fwdStream)
        {
            return null;
        }
    }
}