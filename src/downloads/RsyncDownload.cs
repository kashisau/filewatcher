using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace filewatcher
{
  public class RsyncDownload : IDownload
  {
    int? progress;
    DownloadState state;
    int fileSize = 0;
    readonly string remotePath;
    readonly string localPath;
    readonly string rsyncServer;
    readonly ILogger logger;
    CancellationToken dlCancellationToken;
    DownloadResult downloadResult;
    
    /// <summary>Initialises a download (IDownload interface), without starting
    /// the download.</summary>
    /// <param name="remotePath">The path on the remote machine to copy from (absolute).</param>
    /// <param name="localPath">The path on the local machine to copy to (relative or absolute).</param>
    /// <param name="rsyncServer">The connection string for `rsync` to use to
    /// connect to the remote server.</param>
    /// <param name="logger">An output to log debug and issue logging.</param>
    public RsyncDownload(string remotePath, string localPath, string rsyncServer, ILogger logger = null)
    {
        this.remotePath = remotePath;
        this.localPath = localPath;
        this.rsyncServer = rsyncServer;
        this.logger = logger;

        state = DownloadState.Pending;
    }

    public int? GetProgress() => progress;
    public DownloadState GetState() => state;

    public async Task<DownloadResult> DownloadAsync(CancellationToken dlCancellationToken, SemaphoreSlim semaphore = null)
    {
        this.dlCancellationToken = dlCancellationToken;
        using (var rsyncProcess = new Process())
        {
            // Queue up the rsync process...
            rsyncProcess.StartInfo.FileName = "rsync";
            rsyncProcess.StartInfo.Arguments = $" --partial --progress -z -c --append -e ssh {rsyncServer}:\"'{remotePath}'\" \"{localPath}\"";
            rsyncProcess.StartInfo.UseShellExecute = false;
            rsyncProcess.StartInfo.RedirectStandardOutput = true;
            rsyncProcess.StartInfo.RedirectStandardError = true;
            rsyncProcess.StartInfo.CreateNoWindow = true;

            // rsync output handling
            var outputCloseTask = new TaskCompletionSource<bool>();
            rsyncProcess.OutputDataReceived += new DataReceivedEventHandler(
                (sender, eventArgs) =>
                {
                    if (eventArgs.Data == null)
                    {
                      outputCloseTask.SetResult(true);
                      return;
                    }
                    // Parse the output here.
                    var stdOut = eventArgs.Data.Trim();
                    if (stdOut == "") return;

                    // Initialisation
                    if (stdOut == FilePath.GetFilename(localPath))
                    {
                        state = DownloadState.Downloading;
                        return;
                    }

                    // Progress reporting
                    if (RsyncOutputRegexConstants.OutProgress.IsMatch(stdOut))
                    {
                        Match outCompletedMatch = RsyncOutputRegexConstants.OutProgress.Match(stdOut);
                        Int32 currentFileSize = Int32.Parse(
                            outCompletedMatch.Groups["currentFileSize"].Value,
                            NumberStyles.Integer
                          );
                        this.fileSize = currentFileSize;
                        Int32 currentProgress = Int32.Parse(
                            outCompletedMatch.Groups["currentProgress"].Value,
                            NumberStyles.Integer
                          );
                        this.progress = currentProgress;
                        return;
                    }

                    // Completion summary
                    if (RsyncOutputRegexConstants.OutCompletedSummary.IsMatch(stdOut))
                    {
                        Match outCompletedMatch = RsyncOutputRegexConstants.OutCompletedSummary.Match(stdOut);
                        Int32 fileSize = Int32.Parse(
                            outCompletedMatch.Groups["fileSize"].Value,
                            NumberStyles.Integer
                          );
                        this.fileSize = fileSize;
                        return;
                    }
                }
            );

            // rsync error handling
            var errorCloseTask = new TaskCompletionSource<bool>();
            rsyncProcess.ErrorDataReceived += new DataReceivedEventHandler(
                (sender, eventArgs) =>
                {
                    if (eventArgs.Data == null)
                    {
                      errorCloseTask.SetResult(true);
                      return;
                    }
                    var stdErr = eventArgs.Data;
                    
                    // logger.LogWarning($"StdErr: {stdErr}");

                    // File identified
                    if (stdErr == FilePath.GetFilename(remotePath))
                    {
                        state = DownloadState.Error;
                        return;
                    }

                    // SSH error
                    if (RsyncOutputRegexConstants.ErrSshConnection.IsMatch(stdErr))
                    {
                        Match outCompletedMatch = RsyncOutputRegexConstants.ErrSshConnection.Match(stdErr);
                        logger.LogWarning($"SSH connection broken, retrying {remotePath}");
                        return;
                    }

                    // Rsync cancelled by user
                    if (RsyncOutputRegexConstants.ErrRsyncCancelled.IsMatch(stdErr))
                    {
                        logger.LogWarning($"Rsync exited for {remotePath}");
                        return;
                    }

                    // Rsync error with filesize
                    if (RsyncOutputRegexConstants.ErrRsyncConnectionClosed.IsMatch(stdErr))
                    {
                        Match outCompletedMatch = RsyncOutputRegexConstants.ErrRsyncConnectionClosed.Match(stdErr);
                        Int32 bytesRecived = Int32.Parse(
                            outCompletedMatch.Groups["bytesRecived"].Value,
                            NumberStyles.Integer
                          );
                        this.fileSize = bytesRecived;
                        return;
                    }
                }
            );

            // Resolve to a DownloadResult
            rsyncProcess.Exited += new EventHandler(
                (sender, eventArgs) => {
                    if (rsyncProcess.ExitCode == 0)
                    {
                        state = DownloadState.Complete;
                        progress = 100;
                    }

                    // Work complete, update the semaphore
                    if (semaphore != null) semaphore.Release();
                    if (state == DownloadState.Complete) logger.LogInformation($"{state} {localPath}");
                    else logger.LogWarning($"{state} {localPath}");

                    downloadResult = GetDownloadResult();
                }
            );

            dlCancellationToken.Register(() => {
              try 
              {
                  rsyncProcess.Kill();
              } catch (InvalidOperationException ioe)
              {}
              downloadResult = GetDownloadResult();
            });

            // Wait here if there's a semaphore throttling downloads.
            if (semaphore != null) await semaphore.WaitAsync();

            logger.LogInformation($"Starting {localPath}");

            bool isStarted = rsyncProcess.Start();
            if ( ! isStarted) {
                downloadResult = new DownloadResult() {
                    Progress = 0,
                    ExitCode = rsyncProcess.ExitCode,
                    DownloadState = DownloadState.Error
                };
            }

            rsyncProcess.EnableRaisingEvents = true;
            rsyncProcess.BeginOutputReadLine();
            rsyncProcess.BeginErrorReadLine();

            var rsyncProcessRunTask = Task.Run(() => rsyncProcess.WaitForExit());

            await Task.WhenAll(
                rsyncProcessRunTask,
                outputCloseTask.Task,
                errorCloseTask.Task
            );

            return downloadResult;
        }
    }

    public DownloadResult GetDownloadResult() => new DownloadResult()
        {
            Progress = progress,
            DownloadState = state,
            LocalPath = localPath,
            FileSize = fileSize
        };
  }

  public static class RsyncOutputRegexConstants
  {
      public readonly static Regex OutProgress = new Regex(
            "^(?<currentFileSize>[\\d]+)\\s+(?<currentProgress>[\\d]+)%\\s+(?<currentSpeed>[\\d\\.]+)(?<currentSpeedUnits>[A-Z]B\\/s)\\s+(?<timeAliveTimespan>[\\d:]+)",
            RegexOptions.Compiled
          );
      public readonly static Regex OutSentReceivedSummary = new Regex(
            "^sent\\s+(?<sentDataSize>[\\d]+)\\s(?<sentDataUnit>[a-z]*bytes)\\s+received\\s+(?<receivedDataSize>[\\d]+)\\s+(?<receivedDataUnit>[a-z]*bytes)\\s+(?<averageSpeed>[\\.\\d]+)\\s+(?<averageSpeedUnit>[a-z]*bytes)\\/sec",
            RegexOptions.Compiled
          );

      public readonly static Regex OutCompletedSummary = new Regex(
            "^total\\ssize\\sis\\s(?<fileSize>[\\d]+)",
            RegexOptions.Compiled
          );
      public readonly static Regex ErrSshConnection = new Regex(
            "^packet_write_wait:\\s+Connection to (?<serverAddress>[0-9.]+)\\sport\\s(?<serverPort>[0-9]+):\\s+Broken\\spipe",
            RegexOptions.Compiled
          );
      public readonly static Regex ErrRsyncCancelled = new Regex(
            "^rsync\\serror:\\s+received\\sSIGINT,\\sSIGTERM,\\sor\\sSIGHUP\\s+\\(code\\s(?<errorCode>\\d+)\\)",
            RegexOptions.Compiled
          );
      public readonly static Regex ErrRsyncConnectionClosed = new Regex(
            "^rsync:\\s+connection\\sunexpectedly\\sclosed\\s+\\((?<bytesRecived>[0-9]+)\\s+bytes\\sreceived\\sso\\sfar\\)",
            RegexOptions.Compiled
          );
  }
}