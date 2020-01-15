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
    
    public Connection(string server, int port, string clientID, string downloadPath, string rsyncServer, ILogger logger)
    {
      this.server = server;
      this.port = port;
      this.logger = logger;
      this.clientID = clientID;
      this.downloadPath = downloadPath;
      this.rsyncServer = rsyncServer;
    }

    public async Task Connect(CancellationToken stoppingToken)
    {
        await Task.Run(async () => {
          try 
              {
                  client = new TcpClient(server, port);
                  stream = client.GetStream();

                  await Identify();
                  await GetFileList();
                  
                  stream.Close();         
                  client.Close();         
              } 
              catch (Exception e) 
              {
                  logger.LogError($"Error from stream: {e}");
              }
        });
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
            await Task.Run(() => {
              // Bytes Array to receive Server Response.
              BinaryFormatter formatter = new BinaryFormatter();
              var serverFiles = (ServerFiles) formatter.Deserialize(stream);
              var clientList = serverFiles.Files;
              logger.LogInformation($"Recieved from server: {clientList.Count} files.");
              
              string path = serverFiles.ServerPath;
              // string remoteFilePathString = clientList.Last();
              clientList.ForEach((remoteFilePathString) => {
                  logger.LogInformation($"Recieved from server: path: {path}. Remote file: {remoteFilePathString}.");
                  FilePath filePath = new FilePath(remoteFilePathString, path);
                  string localPath = downloadPath + filePath.Relative;
                  FilePath.CreateDirectoryPath(localPath, logger);
                  var rsyncCommand = $"rsync --progress -c --append -e ssh {rsyncServer}:\"'{filePath.Absolute}'\" \"{downloadPath}{filePath.Relative}\"";
                  // logger.LogInformation(rsyncCommand);
                  
                  var escapedArgs = rsyncCommand.Replace("\"", "\\\"");
                
                  var process = new Process()
                  {
                      StartInfo = new ProcessStartInfo
                      {
                          FileName = "/bin/bash",
                          Arguments = $"-c \"{escapedArgs}\"",
                          RedirectStandardOutput = true,
                          UseShellExecute = false,
                          CreateNoWindow = true,
                      }
                  };
                  process.Start();
                  string result = process.StandardOutput.ReadToEnd();
                  process.WaitForExit();
                  logger.LogInformation(result);
                  logger.LogInformation($"Exit code: {process.ExitCode}");
              });
            });
        } catch (Exception e) {
          logger.LogError($"Error during GetFileList: {e}");
        }
    }
  }
}