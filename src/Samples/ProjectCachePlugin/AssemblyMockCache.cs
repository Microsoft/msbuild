// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Execution;
using Microsoft.Build.Experimental.ProjectCache;
using Microsoft.Build.Framework;
using Shouldly;

namespace MockCacheFromAssembly
{
    public class AssemblyMockCache : ProjectCachePluginBase
    {
        public AssemblyMockCache()
        {
            ErrorFrom("Constructor", pluginLoggerBase: null);
        }

        public override Task BeginBuildAsync(CacheContext context, PluginLoggerBase logger, CancellationToken cancellationToken)
        {
            logger.LogMessage($"{nameof(AssemblyMockCache)}: BeginBuildAsync", MessageImportance.High);

            ErrorFrom(nameof(BeginBuildAsync), logger);

            return Task.CompletedTask;
        }

        public override Task<CacheResult> GetCacheResultAsync(
            BuildRequestData buildRequest,
            PluginLoggerBase logger,
            CancellationToken cancellationToken)
        {
            logger.LogMessage($"{nameof(AssemblyMockCache)}: GetCacheResultAsync for {buildRequest.ProjectFullPath}", MessageImportance.High);

            buildRequest.ProjectInstance.ShouldNotBeNull("The cache plugin expects evaluated projects.");

            ErrorFrom(nameof(GetCacheResultAsync), logger);

            return Task.FromResult(CacheResult.IndicateNonCacheHit(CacheResultType.CacheNotApplicable));
        }

        public override Task EndBuildAsync(PluginLoggerBase logger, CancellationToken cancellationToken)
        {
            logger.LogMessage($"{nameof(AssemblyMockCache)}: EndBuildAsync", MessageImportance.High);

            ErrorFrom(nameof(EndBuildAsync), logger);

            return Task.CompletedTask;
        }

        private static void ErrorFrom(string errorLocation, PluginLoggerBase pluginLoggerBase)
        {
            var errorKind = Environment.GetEnvironmentVariable(errorLocation);

            switch (errorKind)
            {
                case "Exception":
                    pluginLoggerBase?.LogMessage($"{errorLocation} is going to throw an exception", MessageImportance.High);
                    throw new Exception($"Cache plugin exception from {errorLocation}");
                case "LoggedError":
                    pluginLoggerBase?.LogError($"Cache plugin logged error from {errorLocation}");
                    break;
            }
        }
    }
}
