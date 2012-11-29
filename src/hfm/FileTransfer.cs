using System;
using System.IO;
using System.IO.Compression;

using log4net;
using HSXSERVERFILETRANSFERLib;
using SCRIPTINGLib;

using Command;
using HFMCmd;


namespace HFM
{

    /// <summary>
    /// A class for transferring files between the the current machine and an
    /// HFM server.
    /// </summary>
    public class FileTransfer
    {
        // Reference to class logger
        protected static readonly ILog _log = LogManager.GetLogger(
            System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        // Reference to the HsxFileTransfer object
        internal readonly IHsxServerFileTransfer _hsxFileTransfer;


        public FileTransfer(IHsxServerFileTransfer hsxFileTransfer)
        {
            _hsxFileTransfer = hsxFileTransfer;
        }


        /// <summary>
        /// Downloads a file from the server working folder
        /// </summary>
        public void RetrieveFile(Session session, string serverFile, string targetPath,
                bool decompress, IOutput output)
        {
            bool eof = false;
            bool cancel = false;
            object oBytes = null;
            byte[] bytes;
            byte[] buffer = new byte[32768];
            int bytesRead;
            long readPos;
            long totalRead = 0;
            long totalWritten = 0;

            if(serverFile.EndsWith(".gz") && !decompress && !targetPath.EndsWith(".gz")) {
                targetPath += ".gz";
            }

            // Determine file size on server, and monitor progress of download
            var fso = (FileSystemObject)session.HsvSession.CreateObject("Scripting.FileSystemObject");
            var fileSize = (int)fso.GetFile(serverFile).Size;
            fso = null;

            output.InitProgress("Downloading");
            HFM.Try("Initiating download of file {0}", serverFile,
                    () => _hsxFileTransfer.BeginTransferToClient(serverFile, false));
            try {
                // Create a memory stream to hold the downloaded gzipped bytes
                using(var ms = new MemoryStream()) {
                    using(var gz = new GZipStream(ms, CompressionMode.Decompress)) {
                        using(var fs = new FileStream(targetPath, FileMode.Create)) {
                            while(!eof && !cancel) {
                                HFM.Try("Retrieving file content",
                                        () => _hsxFileTransfer.SendBytesToClient(out oBytes, out eof));
                                bytes = (byte[])oBytes;
                                if(bytes != null) {
                                    _log.DebugFormat("Downloaded {0} bytes", bytes.Length);
                                    totalRead += bytes.Length;
                                    if(decompress) {
                                        readPos = ms.Position;
                                        ms.Seek(0, SeekOrigin.End);
                                        ms.Write(bytes, 0, bytes.Length);
                                        ms.Position = readPos;
                                        while((bytesRead = gz.Read(buffer, 0, buffer.Length)) != 0) {
                                            fs.Write(buffer, 0, bytesRead);
                                            totalWritten += bytesRead;
                                        }
                                    }
                                    else {
                                        fs.Write(bytes, 0, bytes.Length);
                                        totalWritten += bytes.Length;
                                    }
                                    // Update progress and determine whether to cancel
                                    cancel = output.SetProgress((int)(totalRead * 100 / fileSize)) || cancel;
                                }
                            }
                            fs.Flush();
                            fs.Close();
                            gz.Close();
                            ms.Close();
                        }
                    }
                }
            }
            finally {
                // Ensure we close file on server
                HFM.Try("Completing transfer",
                        () => _hsxFileTransfer.EndTransfer());
            }
            output.EndProgress();

            if(!cancel) {
                HFM.Try("Deleting file {0}", serverFile,
                        () => _hsxFileTransfer.DeleteFileOnServer(serverFile));
                _log.Info("File transfer completed successfully");
                _log.FineFormat("{0} bytes downloaded, {1} bytes written",
                        totalRead, totalWritten);
            }
        }

    }

}
