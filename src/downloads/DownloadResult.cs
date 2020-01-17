namespace filewatcher
{
  public struct DownloadResult
  {
    public int? Progress;
    public int ExitCode;
    public DownloadState DownloadState;
    public string LocalPath;
    public int FileSize;
  }
}