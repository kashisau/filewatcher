using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace filewatcher
{
  class Download
  {
    string remotePath;
    string localPath;
    string rsyncServer;
    private ILogger _logger;
    public static readonly Regex rsyncProgressRx = new Regex("(?<percentage>[\\d]*)%");
    public int PercentDownloaded;
    BackgroundWorker rsyncWorker;
    Process rsyncProcess;
    DownloadState downloadState;
    public Download(string remotePath, string localPath, string rsyncServer, ILogger logger)
    {
        this.remotePath = remotePath;
        this.localPath = localPath;
        this.rsyncServer = rsyncServer;
        this._logger = logger;
        this.downloadState = DownloadState.Pending;
    }

    public async Task StartDownload(CancellationToken cancellationToken)
    {
      rsyncWorker = new BackgroundWorker();
      rsyncWorker.WorkerReportsProgress = true;
      rsyncWorker.RunWorkerCompleted += RsyncWorkerCompleted;
      rsyncWorker.ProgressChanged += new ProgressChangedEventHandler(
        (Object o, ProgressChangedEventArgs e) => {
            _logger.LogInformation($"Downloading {Path.GetFileName(localPath)}: {e.ProgressPercentage}%");
        }
      );
      rsyncWorker.DoWork += LaunchRsyncWork;
      rsyncWorker.RunWorkerAsync();

      while (downloadState == DownloadState.Pending || downloadState == DownloadState.Downloading) {
        if (cancellationToken.IsCancellationRequested) {
          _logger.LogInformation($"Cancellation requested during download.");
          rsyncProcess.Close();
          rsyncProcess.Dispose();
          rsyncWorker.CancelAsync();
          rsyncWorker.Dispose();
          return;
        }
        await Task.Delay(100);
      }
    }

    private void LaunchRsyncWork(object sender, DoWorkEventArgs e)
    {
      _logger.LogInformation($"Starting download of {remotePath} -> {localPath}.");
      FilePath.CreateDirectoryPath(localPath, _logger);
      var rsyncCommand = $"rsync --progress -c --append -e ssh {rsyncServer}:\"'{remotePath}'\" \"{localPath}\"";
      _logger.LogInformation(rsyncCommand);
      var escapedArgs = rsyncCommand.Replace("\"", "\\\"");

      using (rsyncProcess = new Process())
      {
          rsyncProcess.StartInfo = new ProcessStartInfo
          {
              FileName = "/bin/bash",
              Arguments = $"-c \"{escapedArgs}\"",
              RedirectStandardOutput = true,
              RedirectStandardError = true,
              UseShellExecute = false,
              CreateNoWindow = true
          };
          rsyncProcess.OutputDataReceived += new DataReceivedEventHandler(RsyncProcessOutputDataReceived);
          rsyncProcess.ErrorDataReceived += new DataReceivedEventHandler(RsyncProcessErrorDataReceived);
          rsyncProcess.Start();
          downloadState = DownloadState.Downloading;
          rsyncProcess.BeginOutputReadLine();
          rsyncProcess.BeginErrorReadLine();
          rsyncProcess.WaitForExit();
      }
    }

    private void RsyncWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
    {
        downloadState = DownloadState.Complete;
        PercentDownloaded = 100;
    }

    private void RsyncProcessOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        // regex: [\d]+[\s]+([\d]*)%
        string rsyncOutput = e.Data;
        if (rsyncOutput != null)
        {
          var progressMatches = rsyncProgressRx.Match(rsyncOutput);
          if ( ! progressMatches.Success) return;
          int percent = Int32.Parse(progressMatches.Groups["percentage"].Value);
          if (percent == PercentDownloaded) return;
          this.PercentDownloaded = percent;
          rsyncWorker.ReportProgress(percent);
        }
    }
    private void RsyncProcessErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        string rsyncOutput = e.Data;
        if (rsyncOutput == null) return;
        if (rsyncProcess.HasExited && rsyncProcess.ExitCode == 0) {
            rsyncProcess.Close();
            return;
        }
        downloadState = DownloadState.Error;
        _logger.LogError($"rsync Error: {e.Data}");
    }
  }
}