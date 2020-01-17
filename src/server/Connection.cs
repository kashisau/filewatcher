using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Threading.Tasks;
using filewatched;
using Microsoft.Extensions.Logging;

namespace filewatcher {
  class Connection
  {
    private ILogger logger;
    private string downloadPath;
    Int32 port;
    string server;
    string rsyncServer;
    string clientID;
    TcpClient client;
    NetworkStream stream;

    public bool? IsConnected;
    
    public Connection(string server, int port, string clientID, string downloadPath, string rsyncServer, ILogger logger)
    {
      this.server = server;
      this.port = port;
      this.logger = logger;
      this.clientID = clientID;
      this.downloadPath = downloadPath;
      this.rsyncServer = rsyncServer;
    }

    // Maintains a connection to the server, retrying on failure.
    public async Task Connect(CancellationToken stoppingToken)
    {
        while ( ! stoppingToken.IsCancellationRequested)
        {
            await TryConnection();
            await Task.Delay(1000);
        }
    }

    public async Task TryConnection()
    {
        try 
        {
            client = new TcpClient(server, port);
            stream = client.GetStream();

            IsConnected = true;
            logger.LogInformation($"Connected to server {server} on port {port}.");

            await Identify();
            await GetFileList();
            
            stream.Close();         
            client.Close();         
        } 
        catch (SocketException se) 
        {
            if (IsConnected == false) return;
            IsConnected = false;
            if (se.Message.IndexOf("Connection refused") == 0) {
                logger.LogError($"Error connecting to server {server} on port {port}.");
                return;
            }
            logger.LogError($"Error connecting to server {server} on port {port}. Error: {se.Message}");
        }
      }

    async Task Identify()
    {
        await Task.Run(() => {
            try {
                // Translate the Message into ASCII.
                Byte[] data = System.Text.Encoding.ASCII.GetBytes(clientID);

                // Send the message to the connected TcpServer. 
                stream.Write(data, 0, data.Length);
                logger.LogInformation($"Sent {clientID} to server.");
            } catch (Exception e)
            {
              logger.LogError($"Error during Identify: {e}");
            }
        });
    }
    async Task GetFileList()
    {
        try
        {
            await Task.Run(async () => {
              // Bytes Array to receive Server Response.
              BinaryFormatter formatter = new BinaryFormatter();
              var serverFiles = (ServerFiles) formatter.Deserialize(stream);
              var clientList = serverFiles.Files;
              logger.LogInformation($"Recieved from server: {clientList.Count} files.");
              
              string path = serverFiles.ServerPath;
              string remoteFilePathString = clientList.Last();
              remoteFilePathString = clientList[1];
              // clientList.ForEach(async (remoteFilePathString) => {
                  FilePath filePath = new FilePath(remoteFilePathString, path);
                  string localPath = downloadPath + filePath.Relative;
                  FilePath.CreateDirectoryPath(localPath, logger);
                  var download = new RsyncDownload(remoteFilePathString, localPath, rsyncServer, logger);
                  DownloadResult downloadResult = await download.DownloadAsync(new CancellationToken());
                  logger.LogInformation($"Download result: {downloadResult.DownloadState}");
              // });
            });
        } catch (Exception e) {
          logger.LogError($"Error during GetFileList: {e}");
        }
    }
  }
}