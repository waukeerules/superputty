using System;
using System.IO;
using log4net;

namespace SuperPutty.Utils
{
    /// <summary>
    /// Writes a file whole or not at all: the new contents go to a temporary file beside it, which then takes the
    /// file's place. Writing straight over the file, as SuperPuTTY used to, left a cut-off Sessions.xml when the
    /// write was interrupted, which is likelier with the settings folder on a network drive. Used by
    /// SessionData.SaveSessionsToFile (Sessions.xml, PrivateSessions.xml) and FolderSettings (Folders.xml).
    /// </summary>
    public static class SafeFile
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SafeFile));

        /// <summary>
        /// Write a file through a temporary file in the same folder, then put it in the file's place. If the
        /// replace can't be done (some network shares don't support it), the temporary file is copied over the file.
        /// The temporary file is removed either way.
        /// </summary>
        /// <param name="path">The file to write</param>
        /// <param name="write">Writes the contents (UTF-8, as StreamWriter's default)</param>
        /// <exception cref="IOException">The temporary file couldn't be written; the file is left as it was</exception>
        public static void Write(string path, Action<TextWriter> write)
        {
            string temp = path + ".saving";
            try
            {
                using (StreamWriter writer = new StreamWriter(temp))
                {
                    write(writer);
                }
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(temp, path, null);
                    }
                    catch (IOException ex)
                    {
                        Log.Warn("Could not replace " + path + " in one step; copying over it instead", ex);
                        File.Copy(temp, path, true);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        Log.Warn("Could not replace " + path + " in one step; copying over it instead", ex);
                        File.Copy(temp, path, true);
                    }
                }
                else
                {
                    File.Move(temp, path);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(temp))
                        File.Delete(temp);
                }
                catch (Exception ex)
                {
                    Log.Warn("Could not remove " + temp, ex);
                }
            }
        }
    }
}
