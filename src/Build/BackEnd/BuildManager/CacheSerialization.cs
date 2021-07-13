// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Execution
{
    internal static class CacheSerialization
    {
        public static string SerializeCaches(IConfigCache configCache, IResultsCache resultsCache, string outputCacheFile)
        {
            ErrorUtilities.VerifyThrowInternalNull(outputCacheFile, nameof(outputCacheFile));

            try
            {
                if (string.IsNullOrWhiteSpace(outputCacheFile))
                {
                    return ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword("EmptyOutputCacheFile");
                }

                var fullPath = FileUtilities.NormalizePath(outputCacheFile);

                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

                using (var fileStream = File.OpenWrite(fullPath))
                {
                    var translator = BinaryTranslator.GetWriteTranslator(fileStream);

                    ConfigCache configCacheToSerialize = null;
                    ResultsCache resultsCacheToSerialize = null;

                    switch (configCache)
                    {
                        case ConfigCache asConfigCache:
                            configCacheToSerialize = asConfigCache;
                            break;
                        case ConfigCacheWithOverride configCacheWithOverride:
                            configCacheToSerialize = configCacheWithOverride.CurrentCache;
                            break;
                        default:
                            ErrorUtilities.ThrowInternalErrorUnreachable();
                            break;
                    }

                    switch (resultsCache)
                    {
                        case ResultsCache asResultsCache:
                            resultsCacheToSerialize = asResultsCache;
                            break;
                        case ResultsCacheWithOverride resultsCacheWithOverride:
                            resultsCacheToSerialize = resultsCacheWithOverride.CurrentCache;
                            break;
                        default:
                            ErrorUtilities.ThrowInternalErrorUnreachable();
                            break;
                    }

                    translator.Translate(ref configCacheToSerialize);
                    translator.Translate(ref resultsCacheToSerialize);
                }
            }
            catch (Exception e)
            {
                return ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword("ErrorWritingCacheFile", outputCacheFile, e.Message);
            }

            return null;
        }

        public static (IConfigCache ConfigCache, IResultsCache ResultsCache, Exception exception) DeserializeCaches(string inputCacheFile)
        {
            try
            {
                ConfigCache configCache = null;
                ResultsCache resultsCache = null;

                using (var fileStream = File.OpenRead(inputCacheFile))
                {
                    using var translator = BinaryTranslator.GetReadTranslator(fileStream, null);

                    translator.Translate(ref configCache);
                    translator.Translate(ref resultsCache);
                }

                ErrorUtilities.VerifyThrowInternalNull(configCache, nameof(configCache));
                ErrorUtilities.VerifyThrowInternalNull(resultsCache, nameof(resultsCache));

                return (configCache, resultsCache, null);
            }
            catch (Exception e)
            {
                return (null, null, e);
            }
        }
    }
}
