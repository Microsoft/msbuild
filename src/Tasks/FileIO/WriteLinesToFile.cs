// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Eventing;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;
using System;
using System.IO;
using System.Text;

namespace Microsoft.Build.Tasks
{
    /// <summary>
    /// Appends a list of items to a file. One item per line with carriage returns in-between.
    /// </summary>
    public class WriteLinesToFile : TaskExtension
    {
        // Default encoding taken from System.IO.WriteAllText()
        private static readonly Encoding s_defaultEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        /// <summary>
        /// File to write lines to.
        /// </summary>
        [Required]
        public ITaskItem File { get; set; }

        /// <summary>
        /// Write each item as a line in the file.
        /// </summary>
        public ITaskItem[] Lines { get; set; }

        /// <summary>
        /// If true, overwrite any existing file contents.
        /// </summary>
        public bool Overwrite { get; set; }

        /// <summary>
        /// Encoding to be used.
        /// </summary>
        public string Encoding { get; set; }

        /// <summary>
        /// If true, the target file specified, if it exists, will be read first to compare against
        /// what the task would have written. If identical, the file is not written to disk and the
        /// timestamp will be preserved.
        /// </summary>
        public bool WriteOnlyWhenDifferent { get; set; }

        /// <summary>
        /// Execute the task.
        /// </summary>
        /// <returns></returns>
        public override bool Execute()
        {
            bool success = true;

            if (File != null)
            {
                // do not return if Lines is null, because we may
                // want to delete the file in that case
                StringBuilder buffer = new StringBuilder();
                if (Lines != null)
                {
                    foreach (ITaskItem line in Lines)
                    {
                        buffer.AppendLine(line.ItemSpec);
                    }
                }

                Encoding encoding = s_defaultEncoding;
                if (Encoding != null)
                {
                    try
                    {
                        encoding = System.Text.Encoding.GetEncoding(Encoding);
                    }
                    catch (ArgumentException)
                    {
                        Log.LogErrorWithCodeFromResources("General.InvalidValue", "Encoding", "WriteLinesToFile");
                        return false;
                    }
                }

                try
                {
                    var directoryPath = Path.GetDirectoryName(FileUtilities.NormalizePath(File.ItemSpec));
                    if (Overwrite)
                    {
                        Directory.CreateDirectory(directoryPath);
                        string contentsAsString = buffer.ToString();

                        // When WriteOnlyWhenDifferent is set, read the file and if they're the same return.
                        if (WriteOnlyWhenDifferent)
                        {
                            MSBuildEventSource.Log.WriteLinesToFileUpToDateStart();
                            try
                            {
                                if (FileUtilities.FileExistsNoThrow(File.ItemSpec))
                                {
                                    string existingContents = System.IO.File.ReadAllText(File.ItemSpec);
                                    if (existingContents.Length == buffer.Length)
                                    {
                                        if (existingContents.Equals(contentsAsString))
                                        {
                                            Log.LogMessageFromResources(MessageImportance.Low, "WriteLinesToFile.SkippingUnchangedFile", File.ItemSpec);
                                            MSBuildEventSource.Log.WriteLinesToFileUpToDateStop(File.ItemSpec, true);
                                            return true;
                                        }
                                    }
                                }
                            }
                            catch (IOException)
                            {
                                Log.LogMessageFromResources(MessageImportance.Low, "WriteLinesToFile.ErrorReadingFile", File.ItemSpec);
                            }
                            MSBuildEventSource.Log.WriteLinesToFileUpToDateStop(File.ItemSpec, false);
                        }

                        System.IO.File.WriteAllText(File.ItemSpec, contentsAsString, encoding);
                    }
                    else
                    {
                        Directory.CreateDirectory(directoryPath);
                        System.IO.File.AppendAllText(File.ItemSpec, buffer.ToString(), encoding);
                    }
                }
                catch (Exception e) when (ExceptionHandling.IsIoRelatedException(e))
                {
                    Log.LogErrorWithCodeFromResources("WriteLinesToFile.ErrorOrWarning", File.ItemSpec, e.Message);
                    success = false;
                }
            }

            return success;
        }
    }
}
