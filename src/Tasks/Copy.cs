// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Dataflow;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Build.Shared;
using Microsoft.Build.Shared.FileSystem;
using Microsoft.Build.Eventing;

namespace Microsoft.Build.Tasks
{
    /// <summary>
    /// A task that copies files.
    /// </summary>
    public class Copy : TaskExtension, ICancelableTask
    {
        internal const string AlwaysRetryEnvVar = "MSBUILDALWAYSRETRY";
        internal const string AlwaysOverwriteReadOnlyFilesEnvVar = "MSBUILDALWAYSOVERWRITEREADONLYFILES";

        // Default parallelism determined empirically - times below are in seconds spent in the Copy task building this repo
        // with "build -skiptests -rebuild -configuration Release /ds" (with hack to build.ps1 to disable creating selfhost
        // build for non-selfhost first build; implies first running build in repo to pull packages and create selfhost)
        // and comparing the task timings from the default and selfhost binlogs with different settings for this parallelism
        // number (via env var override). >=3 samples averaged for each number.
        //
        //                            Parallelism: | 1     2     3     4     5     6     8     MaxInt
        // ----------------------------------------+-------------------------------------------------
        // 2-core (4 hyperthreaded) M.2 SSD laptop | 22.3  17.5  13.4  12.6  13.1  9.52  11.3  10.9
        // 12-core (24 HT) SATA2 SSD 2012 desktop  | 15.1  10.2  9.57  7.29  7.64  7.41  7.67  7.79
        // 12-core (24 HT) 1TB spinny disk         | 22.7  15.03 11.1  9.23  11.7  11.1  9.27  11.1
        //
        // However note that since we are relying on synchronous File.Copy() - which will hold threadpool
        // threads at the advantage of performing file copies more quickly in the kernel - we must avoid
        // taking up the whole threadpool esp. when hosted in Visual Studio. IOW we use a specific number
        // instead of int.MaxValue.
        private static readonly int DefaultCopyParallelism = NativeMethodsShared.GetLogicalCoreCount() > 4 ? 6 : 4;

        /// <summary>
        /// Constructor.
        /// </summary>
        public Copy()
        {
            RetryDelayMilliseconds = RetryDelayMillisecondsDefault;

            if (DidNotCopyBecauseOfFileMatch == null)
            {
                CreatesDirectory = Log.GetResourceMessage("Copy.CreatesDirectory");
                DidNotCopyBecauseOfFileMatch = Log.GetResourceMessage("Copy.DidNotCopyBecauseOfFileMatch");
                FileComment = Log.GetResourceMessage("Copy.FileComment");
                HardLinkComment = Log.GetResourceMessage("Copy.HardLinkComment");
                RetryingAsFileCopy = Log.GetResourceMessage("Copy.RetryingAsFileCopy");
                RemovingReadOnlyAttribute = Log.GetResourceMessage("Copy.RemovingReadOnlyAttribute");
                SymbolicLinkComment = Log.GetResourceMessage("Copy.SymbolicLinkComment");
            }
        }

        private static string CreatesDirectory;
        private static string DidNotCopyBecauseOfFileMatch;
        private static string FileComment;
        private static string HardLinkComment;
        private static string RetryingAsFileCopy;
        private static string RemovingReadOnlyAttribute;
        private static string SymbolicLinkComment;

        #region Properties

        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        // Bool is just a placeholder, we're mainly interested in a threadsafe key set.
        private readonly ConcurrentDictionary<string, bool> _directoriesKnownToExist = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Force the copy to retry even when it hits ERROR_ACCESS_DENIED -- normally we wouldn't retry in this case since 
        /// normally there's no point, but occasionally things get into a bad state temporarily, and retrying does actually 
        /// succeed.  So keeping around a secret environment variable to allow forcing that behavior if necessary.  
        /// </summary>
        private static bool s_alwaysRetryCopy = Environment.GetEnvironmentVariable(AlwaysRetryEnvVar) != null;

        /// <summary>
        /// Global flag to force on UseSymboliclinksIfPossible since Microsoft.Common.targets doesn't expose the functionality.
        /// </summary>
        private static readonly bool s_forceSymlinks = Environment.GetEnvironmentVariable("MSBuildUseSymboliclinksIfPossible") != null;

        private static readonly int s_parallelism = GetParallelismFromEnvironment();

        /// <summary>
        /// Default milliseconds to wait between necessary retries
        /// </summary>
        private const int RetryDelayMillisecondsDefault = 1000;

        [Required]
        public ITaskItem[] SourceFiles { get; set; }

        public ITaskItem DestinationFolder { get; set; }

        /// <summary>
        /// How many times to attempt to copy, if all previous
        /// attempts failed. Defaults to zero.
        /// Warning: using retries may mask a synchronization problem in your
        /// build process.
        /// </summary>
        public int Retries { get; set; } = 10;

        /// <summary>
        /// Delay between any necessary retries.
        /// Defaults to <see cref="RetryDelayMillisecondsDefault">RetryDelayMillisecondsDefault</see>
        /// </summary>
        public int RetryDelayMilliseconds { get; set; }

        /// <summary>
        /// Create Hard Links for the copied files rather than copy the files if possible to do so
        /// </summary>
        public bool UseHardlinksIfPossible { get; set; }

        /// <summary>
        /// Create Symbolic Links for the copied files rather than copy the files if possible to do so
        /// </summary>
        public bool UseSymboliclinksIfPossible { get; set; } = s_forceSymlinks;

        /// <summary>
        /// Fail if unable to create a symbolic or hard link instead of falling back to copy
        /// </summary>
        public bool ErrorIfLinkFails { get; set; }

        public bool SkipUnchangedFiles { get; set; }

        [Output]
        public ITaskItem[] DestinationFiles { get; set; }

        /// <summary>
        /// The subset of files that were successfully copied.
        /// </summary>
        [Output]
        public ITaskItem[] CopiedFiles { get; private set; }

        /// <summary>
        /// Whether to overwrite files in the destination
        /// that have the read-only attribute set.
        /// </summary>
        public bool OverwriteReadOnlyFiles { get; set; }

        #endregion

        /// <summary>
        /// Stop and return (in an undefined state) as soon as possible.
        /// </summary>
        public void Cancel()
        {
            _cancellationTokenSource.Cancel();
        }

        #region ITask Members

        /// <summary>
        /// Method compares two files and returns true if their size and timestamp are identical.
        /// </summary>
        /// <param name="sourceFile">The source file</param>
        /// <param name="destinationFile">The destination file</param>
        private static bool IsMatchingSizeAndTimeStamp
        (
            FileState sourceFile,
            FileState destinationFile
        )
        {
            // If the destination doesn't exist, then it is not a matching file.
            if (!destinationFile.FileExists)
            {
                return false;
            }

            if (sourceFile.LastWriteTimeUtcFast != destinationFile.LastWriteTimeUtcFast)
            {
                return false;
            }

            if (sourceFile.Length != destinationFile.Length)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// INTERNAL FOR UNIT-TESTING ONLY
        /// 
        /// We've got several environment variables that we read into statics since we don't expect them to ever
        /// reasonably change, but we need some way of refreshing their values so that we can modify them for
        /// unit testing purposes.
        /// </summary>
        internal static void RefreshInternalEnvironmentValues()
        {
            s_alwaysRetryCopy = Environment.GetEnvironmentVariable(AlwaysRetryEnvVar) != null;
        }

        /// <summary>
        /// If MSBUILDALWAYSRETRY is set, also log useful diagnostic information -- as 
        /// a warning, so it's easily visible. 
        /// </summary>
        private void LogDiagnostic(string message, params object[] messageArgs)
        {
            if (s_alwaysRetryCopy)
            {
                Log.LogWarning(message, messageArgs);
            }
        }

        /// <summary>
        /// Copy one file from source to destination. Create the target directory if necessary and 
        /// leave the file read-write.
        /// </summary>
        /// <returns>Return true to indicate success, return false to indicate failure and NO retry, return NULL to indicate retry.</returns>
        private bool? CopyFileWithLogging
        (
            FileState sourceFileState,      // The source file
            FileState destinationFileState  // The destination file
        )
        {
            bool destinationFileExists = false;

            if (destinationFileState.DirectoryExists)
            {
                Log.LogErrorWithCodeFromResources("Copy.DestinationIsDirectory", sourceFileState.Name, destinationFileState.Name);
                return false;
            }

            if (sourceFileState.DirectoryExists)
            {
                // If the source file passed in is actually a directory instead of a file, log a nice
                // error telling the user so.  Otherwise, .NET Framework's File.Copy method will throw
                // an UnauthorizedAccessException saying "access is denied", which is not very useful
                // to the user.
                Log.LogErrorWithCodeFromResources("Copy.SourceIsDirectory", sourceFileState.Name);
                return false;
            }

            if (!sourceFileState.FileExists)
            {
                Log.LogErrorWithCodeFromResources("Copy.SourceFileNotFound", sourceFileState.Name);
                return false;
            }

            string destinationFolder = Path.GetDirectoryName(destinationFileState.Name);

            if (!string.IsNullOrEmpty(destinationFolder) && !_directoriesKnownToExist.ContainsKey(destinationFolder))
            {
                if (!FileSystems.Default.DirectoryExists(destinationFolder))
                {
                    Log.LogMessage(MessageImportance.Normal, CreatesDirectory, destinationFolder);
                    Directory.CreateDirectory(destinationFolder);
                }

                // It's very common for a lot of files to be copied to the same folder. 
                // Eg., "c:\foo\a"->"c:\bar\a", "c:\foo\b"->"c:\bar\b" and so forth.
                // We don't want to check whether this folder exists for every single file we copy. So store which we've checked.
                _directoriesKnownToExist.TryAdd(destinationFolder, true);
            }

            if (OverwriteReadOnlyFiles)
            {
                MakeFileWriteable(destinationFileState, true);
                destinationFileExists = destinationFileState.FileExists;
            }

            bool linkCreated = false;
            string errorMessage = string.Empty;

            // If we want to create hard or symbolic links, then try that first
            if (UseHardlinksIfPossible)
            {
                TryCopyViaLink(HardLinkComment, MessageImportance.Normal, sourceFileState, destinationFileState, ref destinationFileExists, out linkCreated, ref errorMessage, (source, destination, errMessage) => NativeMethods.MakeHardLink(destination, source, ref errorMessage));
            }
            else if (UseSymboliclinksIfPossible)
            {
                TryCopyViaLink(SymbolicLinkComment, MessageImportance.Normal, sourceFileState, destinationFileState, ref destinationFileExists, out linkCreated, ref errorMessage, (source, destination, errMessage) => NativeMethods.MakeSymbolicLink(destination, source, ref errorMessage));
            }

            if (ErrorIfLinkFails && !linkCreated)
            {
                Log.LogErrorWithCodeFromResources("Copy.LinkFailed", sourceFileState.Name, destinationFileState.Name);
                return false;
            }

            // If the link was not created (either because the user didn't want one, or because it couldn't be created)
            // then let's copy the file
            if (!linkCreated)
            {
                // Do not log a fake command line as well, as it's superfluous, and also potentially expensive
                string sourceFilePath = FileUtilities.GetFullPathNoThrow(sourceFileState.Name);
                string destinationFilePath = FileUtilities.GetFullPathNoThrow(destinationFileState.Name);
                Log.LogMessage(MessageImportance.Normal, FileComment, sourceFilePath, destinationFilePath);

                File.Copy(sourceFileState.Name, destinationFileState.Name, true);
            }

            destinationFileState.Reset();

            // If the destinationFile file exists, then make sure it's read-write.
            // The File.Copy command copies attributes, but our copy needs to
            // leave the file writeable.
            if (sourceFileState.IsReadOnly)
            {
                MakeFileWriteable(destinationFileState, false);
            }

            return true;
        }

        private void TryCopyViaLink(string linkComment, MessageImportance messageImportance, FileState sourceFileState, FileState destinationFileState, ref bool destinationFileExists, out bool linkCreated, ref string errorMessage, Func<string, string, string, bool> createLink)
        {
            // Do not log a fake command line as well, as it's superfluous, and also potentially expensive
            Log.LogMessage(MessageImportance.Normal, linkComment, sourceFileState.Name, destinationFileState.Name);

            if (!OverwriteReadOnlyFiles)
            {
                destinationFileExists = destinationFileState.FileExists;
            }

            // CreateHardLink and CreateSymbolicLink cannot overwrite an existing file or link
            // so we need to delete the existing entry before we create the hard or symbolic link.
            if (destinationFileExists)
            {
                FileUtilities.DeleteNoThrow(destinationFileState.Name);
            }

            linkCreated = createLink(sourceFileState.Name, destinationFileState.Name, errorMessage);

            if (!linkCreated)
            {
                // This is only a message since we don't want warnings when copying to network shares etc.
                Log.LogMessage(messageImportance, RetryingAsFileCopy, sourceFileState.Name, destinationFileState.Name, errorMessage);
            }
        }

        /// <summary>
        /// Ensure the read-only attribute on the specified file is off, so
        /// the file is writeable.
        /// </summary>
        private void MakeFileWriteable(FileState file, bool logActivity)
        {
            if (file.FileExists)
            {
                if (file.IsReadOnly)
                {
                    if (logActivity)
                    {
                        Log.LogMessage(MessageImportance.Low, RemovingReadOnlyAttribute, file.Name);
                    }

                    File.SetAttributes(file.Name, FileAttributes.Normal);
                    file.Reset();
                }
            }
        }

        /// <summary>
        /// Copy the files.
        /// </summary>
        /// <param name="copyFile">Delegate used to copy the files.</param>
        /// <param name="parallelism">
        /// Thread parallelism allowed during copies. 1 uses the original algorithm, >1 uses newer algorithm.
        /// </param>
        internal bool Execute
        (
            CopyFileWithState copyFile,
            int parallelism
        )
        {
            // If there are no source files then just return success.
            if (SourceFiles == null || SourceFiles.Length == 0)
            {
                DestinationFiles = Array.Empty<ITaskItem>();
                CopiedFiles = Array.Empty<ITaskItem>();
                return true;
            }

            if (!(ValidateInputs() && InitializeDestinationFiles()))
            {
                return false;
            }

            // Environment variable stomps on user-requested value if it's set. 
            if (Environment.GetEnvironmentVariable(AlwaysOverwriteReadOnlyFilesEnvVar) != null)
            {
                OverwriteReadOnlyFiles = true;
            }

            // Track successfully copied subset.
            List<ITaskItem> destinationFilesSuccessfullyCopied;

            // Use single-threaded code path when requested or when there is only copy to make
            // (no need to create all the parallel infrastructure for that case).
            bool success = parallelism == 1 || DestinationFiles.Length == 1
                ? CopySingleThreaded(copyFile, out destinationFilesSuccessfullyCopied)
                : CopyParallel(copyFile, parallelism, out destinationFilesSuccessfullyCopied);

            // copiedFiles contains only the copies that were successful.
            CopiedFiles = destinationFilesSuccessfullyCopied.ToArray();

            return success && !_cancellationTokenSource.IsCancellationRequested;
        }

        /// <summary>
        /// Original copy code that performs single-threaded copies.
        /// Used for single-file copies and when parallelism is 1.
        /// </summary>
        private bool CopySingleThreaded(
            CopyFileWithState copyFile,
            out List<ITaskItem> destinationFilesSuccessfullyCopied)
        {
            bool success = true;
            destinationFilesSuccessfullyCopied = new List<ITaskItem>(DestinationFiles.Length);

            // Set of files we actually copied and the location from which they were originally copied.  The purpose
            // of this collection is to let us skip copying duplicate files.  We will only copy the file if it 
            // either has never been copied to this destination before (key doesn't exist) or if we have copied it but
            // from a different location (value is different.)
            // { dest -> source }
            var filesActuallyCopied = new Dictionary<string, string>(
                DestinationFiles.Length, // Set length to common case of 1:1 source->dest.
                StringComparer.OrdinalIgnoreCase);

            // Now that we have a list of destinationFolder files, copy from source to destinationFolder.
            for (int i = 0; i < SourceFiles.Length && !_cancellationTokenSource.IsCancellationRequested; ++i)
            {
                bool copyComplete = false;
                string destPath = DestinationFiles[i].ItemSpec;
                MSBuildEventSource.Log.CopyUpToDateStart(destPath);
                if (filesActuallyCopied.TryGetValue(destPath, out string originalSource))
                {
                    if (String.Equals(originalSource, SourceFiles[i].ItemSpec, StringComparison.OrdinalIgnoreCase))
                    {
                        // Already copied from this location, don't copy again.
                        copyComplete = true;
                    }
                }

                if (!copyComplete)
                {
                    if (DoCopyIfNecessary(new FileState(SourceFiles[i].ItemSpec), new FileState(DestinationFiles[i].ItemSpec), copyFile))
                    {
                        filesActuallyCopied[destPath] = SourceFiles[i].ItemSpec;
                        copyComplete = true;
                    }
                    else
                    {
                        success = false;
                    }
                }
                else
                {
                    MSBuildEventSource.Log.CopyUpToDateStop(destPath);
                }

                if (copyComplete)
                {
                    SourceFiles[i].CopyMetadataTo(DestinationFiles[i]);
                    destinationFilesSuccessfullyCopied.Add(DestinationFiles[i]);
                }
            }

            return success;
        }

        /// <summary>
        /// Parallelize I/O with the same semantics as the single-threaded copy method above.
        /// ResolveAssemblyReferences tends to generate longer and longer lists of files to send
        /// to CopyTask as we get further and further down the dependency graph.
        /// The OS can handle a lot of parallel I/O so let's minimize wall clock time to get
        /// it all done.
        /// </summary>
        private bool CopyParallel(
            CopyFileWithState copyFile,
            int parallelism,
            out List<ITaskItem> destinationFilesSuccessfullyCopied)
        {
            bool success = true;

            // We must supply the same semantics as the single-threaded version above:
            //
            // - For copy operations in the list that have the same destination, we must
            //   provide for in-order copy attempts that allow re-copying different files
            //   and avoiding copies for later files that match SkipUnchangedFiles semantics.
            //   We must also add a destination file copy item for each attempt.
            // - The order of entries in destinationFilesSuccessfullyCopied must match
            //   the order of entries passed in, along with copied metadata.
            // - Metadata must not be copied to destination item if the copy operation failed.
            //
            // We split the work into different Tasks:
            //
            // - Entries with unique destination file paths each get their own parallel operation.
            // - Each subset of copies into the same destination get their own Task to run
            //   the single-threaded logic in order.
            //
            // At the end we reassemble the result list in the same order as was passed in.

            // Map: Destination path -> indexes in SourceFiles/DestinationItems array indices (ordered low->high).
            var partitionsByDestination = new Dictionary<string, List<int>>(
                DestinationFiles.Length, // Set length to common case of 1:1 source->dest.
                StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < SourceFiles.Length && !_cancellationTokenSource.IsCancellationRequested; ++i)
            {
                ITaskItem destItem = DestinationFiles[i];
                string destPath = destItem.ItemSpec;
                if (!partitionsByDestination.TryGetValue(destPath, out List<int> sourceIndices))
                {
                    // Use 1 for list length - common case is for no destination overlap.
                    sourceIndices = new List<int>(1);
                    partitionsByDestination[destPath] = sourceIndices;
                }
                sourceIndices.Add(i);
            }

            // Lockless flags updated from each thread - each needs to be a processor word for atomicity.
            var successFlags = new IntPtr[DestinationFiles.Length];
            var actionBlockOptions = new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = parallelism,
                CancellationToken = _cancellationTokenSource.Token
            };
            var partitionCopyActionBlock = new ActionBlock<List<int>>(
                async (List<int> partition) =>
                {
                    // Break from synchronous thread context of caller to get onto thread pool thread.
                    await System.Threading.Tasks.Task.Yield();

                    for (int partitionIndex = 0; partitionIndex < partition.Count && !_cancellationTokenSource.IsCancellationRequested; partitionIndex++)
                    {
                        int fileIndex = partition[partitionIndex];
                        ITaskItem sourceItem = SourceFiles[fileIndex];
                        ITaskItem destItem = DestinationFiles[fileIndex];
                        string sourcePath = sourceItem.ItemSpec;

                        // Check if we just copied from this location to the destination, don't copy again.
                        MSBuildEventSource.Log.CopyUpToDateStart(destItem.ItemSpec);
                        bool copyComplete = partitionIndex > 0 &&
                                            String.Equals(
                                                sourcePath,
                                                SourceFiles[partition[partitionIndex - 1]].ItemSpec,
                                                StringComparison.OrdinalIgnoreCase);

                        if (!copyComplete)
                        {
                            if (DoCopyIfNecessary(
                                new FileState(sourceItem.ItemSpec),
                                new FileState(destItem.ItemSpec),
                                copyFile))
                            {
                                copyComplete = true;
                            }
                            else
                            {
                                // Thread race to set outer variable but they race to set the same (false) value.
                                success = false;
                            }
                        }
                        else
                        {
                            MSBuildEventSource.Log.CopyUpToDateStop(destItem.ItemSpec);
                        }

                        if (copyComplete)
                        {
                            sourceItem.CopyMetadataTo(destItem);
                            successFlags[fileIndex] = (IntPtr)1;
                        }
                    }
                },
                actionBlockOptions);

            foreach (List<int> partition in partitionsByDestination.Values)
            {
                bool partitionAccepted = partitionCopyActionBlock.Post(partition);
                if (!partitionAccepted)
                {
                    // Retail assert...
                    ErrorUtilities.VerifyThrow(false,
                        "Failed posting a file copy to an ActionBlock. Should not happen with block at max int capacity.");
                }
            }

            partitionCopyActionBlock.Complete();
            partitionCopyActionBlock.Completion.GetAwaiter().GetResult();

            // Assemble an in-order list of destination items that succeeded.
            destinationFilesSuccessfullyCopied = new List<ITaskItem>(DestinationFiles.Length);
            for (int i = 0; i < successFlags.Length; i++)
            {
                if (successFlags[i] != (IntPtr)0)
                {
                    destinationFilesSuccessfullyCopied.Add(DestinationFiles[i]);
                }
            }

            return success;
        }

        /// <summary>
        /// Verify that the inputs are correct.
        /// </summary>
        /// <returns>False on an error, implying that the overall copy operation should be aborted.</returns>
        private bool ValidateInputs()
        {
            if (Retries < 0)
            {
                Log.LogErrorWithCodeFromResources("Copy.InvalidRetryCount", Retries);
                return false;
            }

            if (RetryDelayMilliseconds < 0)
            {
                Log.LogErrorWithCodeFromResources("Copy.InvalidRetryDelay", RetryDelayMilliseconds);
                return false;
            }

            // There must be a destinationFolder (either files or directory).
            if (DestinationFiles == null && DestinationFolder == null)
            {
                Log.LogErrorWithCodeFromResources("Copy.NeedsDestination", "DestinationFiles", "DestinationFolder");
                return false;
            }

            // There can't be two kinds of destination.
            if (DestinationFiles != null && DestinationFolder != null)
            {
                Log.LogErrorWithCodeFromResources("Copy.ExactlyOneTypeOfDestination", "DestinationFiles", "DestinationFolder");
                return false;
            }

            // If the caller passed in DestinationFiles, then its length must match SourceFiles.
            if (DestinationFiles != null && DestinationFiles.Length != SourceFiles.Length)
            {
                Log.LogErrorWithCodeFromResources("General.TwoVectorsMustHaveSameLength", DestinationFiles.Length, SourceFiles.Length, "DestinationFiles", "SourceFiles");
                return false;
            }

            // First check if create hard or symbolic link option is selected. If both then return an error
            if (UseHardlinksIfPossible & UseSymboliclinksIfPossible)
            {
                Log.LogErrorWithCodeFromResources("Copy.ExactlyOneTypeOfLink", "UseHardlinksIfPossible", "UseSymboliclinksIfPossible");
                return false;
            }

            if (ErrorIfLinkFails && !UseHardlinksIfPossible && !UseSymboliclinksIfPossible)
            {
                Log.LogErrorWithCodeFromResources("Copy.ErrorIfLinkFailsSetWithoutLinkOption");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Set up our list of destination files.
        /// </summary>
        /// <returns>False if an error occurred, implying aborting the overall copy operation.</returns>
        private bool InitializeDestinationFiles()
        {
            if (DestinationFiles == null)
            {
                // If the caller passed in DestinationFolder, convert it to DestinationFiles
                DestinationFiles = new ITaskItem[SourceFiles.Length];

                for (int i = 0; i < SourceFiles.Length; ++i)
                {
                    // Build the correct path.
                    string destinationFile;
                    try
                    {
                        destinationFile = Path.Combine(DestinationFolder.ItemSpec, Path.GetFileName(SourceFiles[i].ItemSpec));
                    }
                    catch (ArgumentException e)
                    {
                        Log.LogErrorWithCodeFromResources("Copy.Error", SourceFiles[i].ItemSpec, DestinationFolder.ItemSpec, e.Message);
                        // Clear the outputs.
                        DestinationFiles = Array.Empty<ITaskItem>();
                        return false;
                    }

                    // Initialize the destinationFolder item.
                    // ItemSpec is unescaped, and the TaskItem constructor expects an escaped input, so we need to 
                    // make sure to re-escape it here. 
                    DestinationFiles[i] = new TaskItem(EscapingUtilities.Escape(destinationFile));

                    // Copy meta-data from source to destinationFolder.
                    SourceFiles[i].CopyMetadataTo(DestinationFiles[i]);
                }
            }

            return true;
        }

        /// <summary>
        /// Copy source to destination, unless SkipUnchangedFiles is true and they are equivalent.
        /// </summary>
        /// <returns>True if the file was copied or, on SkipUnchangedFiles, the file was equivalent.</returns>
        private bool DoCopyIfNecessary(FileState sourceFileState, FileState destinationFileState, CopyFileWithState copyFile)
        {
            bool success = true;

            try
            {
                if (SkipUnchangedFiles && IsMatchingSizeAndTimeStamp(sourceFileState, destinationFileState))
                {
                    // If we got here, then the file's time and size match AND
                    // the user set the SkipUnchangedFiles flag which means we
                    // should skip matching files.
                    Log.LogMessage(
                        MessageImportance.Low,
                        DidNotCopyBecauseOfFileMatch,
                        sourceFileState.Name,
                        destinationFileState.Name,
                        "SkipUnchangedFiles",
                        "true"
                    );
                    MSBuildEventSource.Log.CopyUpToDateStop(destinationFileState.Name);
                }
                // We only do the cheap check for identicalness here, we try the more expensive check
                // of comparing the fullpaths of source and destination to see if they are identical,
                // in the exception handler lower down.
                else if (!String.Equals(
                             sourceFileState.Name,
                             destinationFileState.Name,
                             StringComparison.OrdinalIgnoreCase))
                {
                    MSBuildEventSource.Log.CopyUpToDateStop(destinationFileState.Name);
                    success = DoCopyWithRetries(sourceFileState, destinationFileState, copyFile);
                }
                else
                {
                    MSBuildEventSource.Log.CopyUpToDateStop(destinationFileState.Name);
                }
            }
            catch (OperationCanceledException)
            {
                success = false;
            }
            catch (PathTooLongException e)
            {
                Log.LogErrorWithCodeFromResources("Copy.Error", sourceFileState.Name, destinationFileState.Name, e.Message);
                success = false;
            }
            catch (Exception e) when (ExceptionHandling.IsIoRelatedException(e))
            {
                Log.LogErrorWithCodeFromResources("Copy.Error", sourceFileState.Name, destinationFileState.Name, e.Message);
                success = false;
            }

            return success;
        }

        /// <summary>
        /// Copy one file with the appropriate number of retries if it fails.
        /// </summary>
        private bool DoCopyWithRetries(FileState sourceFileState, FileState destinationFileState, CopyFileWithState copyFile)
        {
            int retries = 0;

            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    bool? result = copyFile(sourceFileState, destinationFileState);
                    if (result.HasValue)
                    {
                        return result.Value;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e) when (ExceptionHandling.IsIoRelatedException(e))
                {
                    if (e is ArgumentException ||  // Invalid chars
                        e is NotSupportedException || // Colon in the middle of the path
                        e is PathTooLongException)
                    {
                        // No use retrying these cases
                        throw;
                    }

                    if (e is UnauthorizedAccessException || e is IOException) // Not clear why we can get one and not the other
                    {
                        int code = Marshal.GetHRForException(e);

                        LogDiagnostic("Got {0} copying {1} to {2} and HR is {3}", e.ToString(), sourceFileState.Name, destinationFileState.Name, code);
                        if (code == NativeMethods.ERROR_ACCESS_DENIED)
                        {
                            // ERROR_ACCESS_DENIED can either mean there's an ACL preventing us, or the file has the readonly bit set.
                            // In either case, that's likely not a race, and retrying won't help.
                            // Retrying is mainly for ERROR_SHARING_VIOLATION, where someone else is using the file right now.
                            // However, there is a limited set of circumstances where a copy failure will show up as access denied due 
                            // to a failure to reset the readonly bit properly, in which case retrying will succeed.  This seems to be 
                            // a pretty edge scenario, but since some of our internal builds appear to be hitting it, provide a secret
                            // environment variable to allow overriding the default behavior and forcing retries in this circumstance as well. 
                            if (!s_alwaysRetryCopy)
                            {
                                throw;
                            }
                            else
                            {
                                LogDiagnostic("Retrying on ERROR_ACCESS_DENIED because MSBUILDALWAYSRETRY = 1");
                            }
                        }
                    }

                    if (e is IOException && DestinationFolder != null && FileSystems.Default.FileExists(DestinationFolder.ItemSpec))
                    {
                        // We failed to create the DestinationFolder because it's an existing file. No sense retrying.
                        // We don't check for this case upstream because it'd be another hit to the filesystem.
                        throw;
                    }

                    if (e is IOException)
                    {
                        // if this was just because the source and destination files are the
                        // same file, that's not a failure.
                        // Note -- we check this exceptional case here, not before the copy, for perf.
                        if (PathsAreIdentical(sourceFileState.Name, destinationFileState.Name))
                        {
                            return true;
                        }
                    }

                    if (retries < Retries)
                    {
                        retries++;
                        Log.LogWarningWithCodeFromResources("Copy.Retrying", sourceFileState.Name,
                            destinationFileState.Name, retries, RetryDelayMilliseconds, e.Message,
                            GetLockedFileMessage(destinationFileState.Name));

                        // if we have to retry for some reason, wipe the state -- it may not be correct anymore. 
                        destinationFileState.Reset();

                        Thread.Sleep(RetryDelayMilliseconds);
                        continue;
                    }
                    else if (Retries > 0)
                    {
                        // Exception message is logged in caller
                        Log.LogErrorWithCodeFromResources("Copy.ExceededRetries", sourceFileState.Name,
                            destinationFileState.Name, Retries, GetLockedFileMessage(destinationFileState.Name));
                        throw;
                    }
                    else
                    {
                        throw;
                    }
                }

                if (retries < Retries)
                {
                    retries++;
                    Log.LogWarningWithCodeFromResources("Copy.Retrying", sourceFileState.Name,
                        destinationFileState.Name, retries, RetryDelayMilliseconds, String.Empty /* no details */,
                        GetLockedFileMessage(destinationFileState.Name));

                    // if we have to retry for some reason, wipe the state -- it may not be correct anymore. 
                    destinationFileState.Reset();

                    Thread.Sleep(RetryDelayMilliseconds);
                }
                else if (Retries > 0)
                {
                    Log.LogErrorWithCodeFromResources("Copy.ExceededRetries", sourceFileState.Name,
                        destinationFileState.Name, Retries, GetLockedFileMessage(destinationFileState.Name));
                    return false;
                }
                else
                {
                    return false;
                }
            }

            // Canceling
            return false;
        }

        /// <summary>
        /// Try to get a message to inform the user which processes have a lock on a given file.
        /// </summary>
        private static string GetLockedFileMessage(string file)
        {
            string message = string.Empty;
#if !RUNTIME_TYPE_NETCORE && !MONO

            try
            {
                var processes = LockCheck.GetProcessesLockingFile(file);
                message = !string.IsNullOrEmpty(processes)
                    ? ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword("Copy.FileLocked", processes)
                    : String.Empty;
            }
            catch (Exception)
            {
                // Never throw if we can't get the processes locking the file.
            }
#endif
            return message;
        }

        /// <summary>
        /// Standard entry point.
        /// </summary>
        /// <returns></returns>
        public override bool Execute()
        {
            return Execute(CopyFileWithLogging, s_parallelism);
        }

        #endregion

        /// <summary>
        /// Compares two paths to see if they refer to the same file. We can't solve the general
        /// canonicalization problem, so we just compare strings on the full paths.
        /// </summary>
        private static bool PathsAreIdentical(string source, string destination)
        {
            string fullSourcePath = Path.GetFullPath(source);
            string fullDestinationPath = Path.GetFullPath(destination);
            StringComparison filenameComparison = NativeMethodsShared.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return String.Equals(fullSourcePath, fullDestinationPath, filenameComparison);
        }

    	private static int GetParallelismFromEnvironment()
	    {
	        int parallelism = Traits.Instance.CopyTaskParallelism;
	        if (parallelism < 0)
	        {
	            parallelism = DefaultCopyParallelism;
	        }
            else if (parallelism == 0)
	        {
	            parallelism = int.MaxValue;
	        }
            return parallelism;
        }
    }
}
