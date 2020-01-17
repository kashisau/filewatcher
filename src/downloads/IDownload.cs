using System.Threading;
using System.Threading.Tasks;

namespace filewatcher
{
    interface IDownload
    {
        int? GetProgress();
        DownloadState GetState();
        DownloadResult GetDownloadResult();
        Task<DownloadResult> DownloadAsync(CancellationToken dlCancellationToken);
    }
}
