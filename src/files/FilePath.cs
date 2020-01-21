using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;

namespace filewatcher
{
  class FilePath
  {
    public string Absolute;
    public string Relative;

    public FilePath(string filePath, string remotePath)
    {
        this.Absolute = filePath;
        this.Relative = filePath;
        if (remotePath.IndexOf(remotePath) == 0)
        {
          this.Relative = filePath.Substring(remotePath.Length);
        }
    }

    public static string RemoveTrailingSlash(string path)
    {
      return path.Trim('/');
    }


    // Extracts a filename from a file path.
    public static string GetFilename(string filePath)
    {
        return Path.GetFileName(filePath);
    }

    // Extracts the path from a file path (excluding the filename)
    public static string GetDirectoryPath(string filePath)
    {
        return Path.GetDirectoryName(filePath);
    }
    // Let's cycle through each directory in a path until we hit a snag
    public static DirectoryInfo CreateDirectoryPath(string filePath, ILogger logger = null)
    {
      string directoryPath = GetDirectoryPath(filePath);
      DirectoryInfo dirPathInfo = CreateDirectory(directoryPath, logger);
      return dirPathInfo;
    }
    public static DirectoryInfo CreateDirectory(string directoryPath, ILogger logger = null)
    {
      try {
          DirectoryInfo dirInfo = Directory.CreateDirectory(
          FilePath.RemoveTrailingSlash(directoryPath));
          return dirInfo;
      }
      catch (DirectoryNotFoundException dnfE)
      {
          if (logger == null) return null;
          logger.LogError($"The path '{directoryPath}' is invalid. {dnfE}.");
      }
      catch (IOException)
      {
          if (logger == null) return null;
          logger.LogError($"The path '{directoryPath}' is an existing file.");
      }
      return null;
    }
  }
}