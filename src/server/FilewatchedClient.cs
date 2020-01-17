using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace filewatcher
{
    class FilewatchedClient
    {
        readonly ILogger logger;
        readonly string server;
        readonly Int32 port;
        readonly string rsyncHost;
        NetworkStream clientStream;

        string fwdServerPath;
        public FilewatchedClient(string fileWatchedServer, Int32 filewatchedPort, string rsyncHost, ILogger logger)
        {
            this.server = fileWatchedServer;
            this.port = filewatchedPort;
            this.rsyncHost = rsyncHost;
            this.logger = logger;
        }
        public void SetDestination(string localPath)
        {
            var localDirectory = FilePath.CreateDirectoryPath(localPath);
            if (localDirectory != null) return; // Successfully created

            logger.LogCritical($"The destination path for local files '{localPath}' could not be created.");
        }

        // This process needs to continue until the cancellationToken is 
        // activated.
        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            while ( ! cancellationToken.IsCancellationRequested)
            {
                // Get list of files
                clientStream = await Connect();
                var fwdServerFiles = GetFileListAsync(clientStream);

                // Download or verify existing files

                // Start watching for new files
            }
            return;
        }

        async Task<NetworkStream> Connect()
        {
            try
            {
                TcpClient fwdTcpClient = new TcpClient(server, port);
                NetworkStream stream = fwdTcpClient.GetStream();
                if (RegisterFilewatcherClient(stream))
                {
                    return stream;
                }
            }
            catch (SocketException se)
            {
                var seErrorMessage = se.Message;
                if (string.IsNullOrEmpty(seErrorMessage)) return null;
                if (FileWatchedMessageRegexConstants.ConnectionRefused.IsMatch(seErrorMessage))
                {
                    Match seErrorMatch = FileWatchedMessageRegexConstants.ConnectionRefused.Match(seErrorMessage);
                    string resolvedAddress = seErrorMatch.Groups["resolvedAddress"].Value;
                    logger.LogWarning($"The filewatched server on {server}:{port} ({resolvedAddress}) cannot be reached.");
                    await Task.Delay(1000);
                    return await Connect();
                }
            }
            return null;
        }

        bool RegisterFilewatcherClient(NetworkStream stream)
        {
            try {
                // Translate the Message into ASCII.
                Byte[] data = System.Text.Encoding.ASCII.GetBytes("filewatcher");
                stream.Write(data, 0, data.Length);
                return true;
            } catch (Exception e)
            {
                logger.LogError($"Could not write to network stream: {e.Message}");
                return false;
            }
        }

        List<FilePath> GetFileListAsync(NetworkStream stream)
        {
            try
            {
                BinaryFormatter formatter = new BinaryFormatter();
                var serverFiles = (ServerFiles) formatter.Deserialize(stream);
                fwdServerPath = serverFiles.ServerPath;
                
                var filePaths = new List<FilePath>();
                serverFiles.Files.ForEach(
                    serverFilePath => filePaths.Add(new FilePath(serverFilePath, fwdServerPath))
                );
                return filePaths;
            } catch (SocketException se)
            {
                // General connection error
                logger.LogError($"Error fetching files from server: {se.Message}");
                return null;
            }
            catch (SerializationException se)
            {
                var seErrorMessage = se.Message;
                if (FileWatchedMessageRegexConstants.BinaryFormatError.IsMatch(seErrorMessage))
                {
                    // Wrong service
                    throw new ArgumentException("Incorrect server");
                }
                logger.LogError($"Error parsing server response: {se.Message}");
                return null;
            }

        }

        Task DownloadFilesAsync(string remoteFilePath)
        {
            return null;
        }
        
        Task DownloadFilesAsync(List<string> remoteFilePaths)
        {
            return null;
        }

        Task DownloadExistingFiles()
        {
            return null;
        }
    }

    static class FileWatchedMessageRegexConstants
    {
        public readonly static Regex ConnectionRefused = new Regex(
            "^Connection refused (?<resolvedAddress>[\\d\\.:]+)$",
            RegexOptions.Compiled
        );
        public readonly static Regex BinaryFormatError = new Regex(
            "^Connection refused (?<resolvedAddress>[\\d\\.:]+)$",
            RegexOptions.Compiled
        );

    }
}