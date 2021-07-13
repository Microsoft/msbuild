// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Build.BackEnd.SdkResolution;
using Microsoft.Build.FileSystem;
using Microsoft.Build.Internal;
using Microsoft.Build.Shared;
using Microsoft.Build.Shared.FileSystem;

namespace Microsoft.Build.Evaluation.Context
{
    /// <summary>
    ///     An object used by the caller to extend the lifespan of evaluation caches (by passing the object on to other
    ///     evaluations).
    ///     The caller should throw away the context when the environment changes (IO, environment variables, SDK resolution
    ///     inputs, etc).
    ///     This class and its closure needs to be thread safe since API users can do evaluations in parallel.
    /// </summary>
    public class EvaluationContext
    {
        public enum SharingPolicy
        {
            /// <summary>
            /// Instructs the <see cref="EvaluationContext"/> to reuse state between the different project evaluations that use it.
            /// </summary>
            Shared,

            /// <summary>
            /// Instructs the <see cref="EvaluationContext"/> not to reuse state between the different project evaluations that use it.
            /// </summary>
            Isolated
        }

        internal static Action<EvaluationContext> TestOnlyHookOnCreate { get; set; }

        private int _used;

        internal SharingPolicy Policy { get; }

        internal ISdkResolverService SdkResolverService { get; }
        internal IFileSystem FileSystem { get; }
        internal EngineFileUtilities EngineFileUtilities { get; }

        /// <summary>
        /// Key to file entry list. Example usages: cache glob expansion and intermediary directory expansions during glob expansion.
        /// </summary>
        private ConcurrentDictionary<string, IReadOnlyList<string>> FileEntryExpansionCache { get; }

        private EvaluationContext(SharingPolicy policy, IFileSystem fileSystem)
        {
            // Unsupported case: isolated context with non null file system.
            // Isolated means caches aren't reused, but the given file system might cache.
            ErrorUtilities.VerifyThrowArgument(
                policy == SharingPolicy.Shared || fileSystem == null,
                "IsolatedContextDoesNotSupportFileSystem");

            Policy = policy;

            SdkResolverService = new CachingSdkResolverService();
            FileEntryExpansionCache = new ConcurrentDictionary<string, IReadOnlyList<string>>();
            FileSystem = fileSystem ?? new CachingFileSystemWrapper(FileSystems.Default);
            EngineFileUtilities = new EngineFileUtilities(new FileMatcher(FileSystem, FileEntryExpansionCache));
        }

        /// <summary>
        ///     Factory for <see cref="EvaluationContext" />
        /// </summary>
        public static EvaluationContext Create(SharingPolicy policy)
        {
            
            // ReSharper disable once IntroduceOptionalParameters.Global
            // do not remove this method to avoid breaking binary compatibility
            return Create(policy, fileSystem: null);
        }

        /// <summary>
        ///     Factory for <see cref="EvaluationContext" />
        /// </summary>
        /// <param name="policy"> The <see cref="SharingPolicy"/> to use.</param>
        /// <param name="fileSystem">The <see cref="IFileSystem"/> to use.
        ///     This parameter is compatible only with <see cref="SharingPolicy.Shared"/>.
        ///     The method throws if a file system is used with <see cref="SharingPolicy.Isolated"/>.
        ///     The reasoning is that <see cref="SharingPolicy.Isolated"/> means not reusing any caches between evaluations,
        ///     and the passed in <paramref name="fileSystem"/> might cache state.
        /// </param>
        public static EvaluationContext Create(SharingPolicy policy, MSBuildFileSystemBase fileSystem)
        {
            var context = new EvaluationContext(
                policy,
                fileSystem);

            TestOnlyHookOnCreate?.Invoke(context);

            return context;
        }

        private EvaluationContext CreateUsedIsolatedContext()
        {
            var context = Create(SharingPolicy.Isolated);
            context._used = 1;

            return context;
        }

        internal EvaluationContext ContextForNewProject()
        {
            // Projects using isolated contexts need to get a new context instance 
            switch (Policy)
            {
                case SharingPolicy.Shared:
                    return this;
                case SharingPolicy.Isolated:
                    // reuse the first isolated context if it has not seen an evaluation yet.
                    var previousValueWasUsed = Interlocked.CompareExchange(ref _used, 1, 0);
                    return previousValueWasUsed == 0
                        ? this
                        : CreateUsedIsolatedContext();
                default:
                    ErrorUtilities.ThrowInternalErrorUnreachable();
                    return null;
            }
        }
    }
}
