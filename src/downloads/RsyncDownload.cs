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

    public async Task<DownloadResult> DownloadAsync(CancellationToken dlCancellationToken)
    {
        this.dlCancellationToken = dlCancellationToken;
        using (var rsyncProcess = new Process())
        {
            // Queue up the rsync process...
            rsyncProcess.StartInfo.FileName = "rsync";
            rsyncProcess.StartInfo.Arguments = $" --progress -z -c --append -e ssh {rsyncServer}:\"'{remotePath}'\" \"{localPath}\"";
            rsyncProcess.StartInfo.UseShellExecute = false;
            rsyncProcess.StartInfo.RedirectStandardOutput = true;
            rsyncProcess.StartInfo.RedirectStandardError = true;
            rsyncProcess.StartInfo.CreateNoWindow = true;

            logger.LogInformation(rsyncProcess.StartInfo.Arguments);

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
                    if (stdOut == FilePath.GetFilename(remotePath))
                    {
                        state = DownloadState.Downloading;
                        return;
                    }

                    // Progress reporting
                    if (RsyncOutputRegExConstants.OutProgress.IsMatch(stdOut))
                    {
                        Match outCompletedMatch = RsyncOutputRegExConstants.OutProgress.Match(stdOut);
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
                    if (RsyncOutputRegExConstants.OutCompletedSummary.IsMatch(stdOut))
                    {
                        Match outCompletedMatch = RsyncOutputRegExConstants.OutCompletedSummary.Match(stdOut);
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
                    
                    logger.LogInformation($"StdErr: {stdErr}");

                    // File identified
                    if (stdErr == FilePath.GetFilename(remotePath))
                    {
                        state = DownloadState.Error;
                        return;
                    }

                    // SSH error
                    if (RsyncOutputRegExConstants.ErrSshConnection.IsMatch(stdErr))
                    {
                        Match outCompletedMatch = RsyncOutputRegExConstants.ErrSshConnection.Match(stdErr);
                        return;
                    }

                    // Rsync error with filesize
                    if (RsyncOutputRegExConstants.ErrRsyncConnectionClosed.IsMatch(stdErr))
                    {
                        Match outCompletedMatch = RsyncOutputRegExConstants.ErrRsyncConnectionClosed.Match(stdErr);
                        Int32 bytesRecived = Int32.Parse(
                            outCompletedMatch.Groups["bytesRecived"].Value,
                            NumberStyles.Integer
                          );
                        this.fileSize = bytesRecived;
                        return;
                    }
                }
            );

            bool isStarted = rsyncProcess.Start();
            if ( ! isStarted) {
                return new DownloadResult() {
                    Progress = 0,
                    ExitCode = rsyncProcess.ExitCode,
                    DownloadState = DownloadState.Error
                };
            }

            rsyncProcess.EnableRaisingEvents = true;
            rsyncProcess.BeginOutputReadLine();
            rsyncProcess.BeginErrorReadLine();

            var rsyncProcessRunTask = Task.Run(() => rsyncProcess.WaitForExit());

            var rsyncProcessTasks = Task.WhenAll(
                rsyncProcessRunTask,
                outputCloseTask.Task,
                errorCloseTask.Task
            );

            while ( ! dlCancellationToken.IsCancellationRequested)
            {
                if (rsyncProcessRunTask.IsCompleted) {
                  switch (rsyncProcessRunTask.Status)
                  {
                      case TaskStatus.RanToCompletion:
                          state = DownloadState.Complete;
                          progress = 100;
                          break;
                      case TaskStatus.Canceled:
                          state = DownloadState.Cancelled;
                          break;
                      default:
                          state = DownloadState.Error;
                          break;
                  }
                  return GetDownloadResult();
                }
                await Task.Delay(200);
            }
            
            return new DownloadResult()
                {
                    Progress = progress,
                    DownloadState = DownloadState.Cancelled,
                    LocalPath = localPath,
                    FileSize = fileSize
                };

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

  public static class RsyncOutputRegExConstants
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
      public readonly static Regex ErrRsyncConnectionClosed = new Regex(
            "^rsync:\\s+connection\\sunexpectedly\\sclosed\\s+\\((?<bytesRecived>[0-9]+)\\s+bytes\\sreceived\\sso\\sfar\\)",
            RegexOptions.Compiled
          );
  }
}