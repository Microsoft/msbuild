// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Utilities
{
    /// <summary>
    ///     Represents toggleable features of the MSBuild engine
    /// </summary>
    internal class Traits
    {
        private static readonly Traits _instance = new Traits();
        public static Traits Instance
        {
            get
            {
                if (BuildEnvironmentHelper.Instance.RunningTests)
                {
                    return new Traits();
                }
                return _instance;
            }
        }

        public Traits()
        {
            EscapeHatches = new EscapeHatches();

            DebugScheduler = DebugEngine || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSBUILDDEBUGSCHEDULER"));
            DebugNodeCommunication = DebugEngine || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSBUILDDEBUGCOMM"));
        }

        public EscapeHatches EscapeHatches { get; }

        internal readonly string MSBuildDisableFeaturesFromVersion = Environment.GetEnvironmentVariable("MSBUILDDISABLEFEATURESFROMVERSION");

        /// <summary>
        /// Do not expand wildcards that match a certain pattern
        /// </summary>
        public readonly bool UseLazyWildCardEvaluation = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MsBuildSkipEagerWildCardEvaluationRegexes"));
        public readonly bool LogExpandedWildcards = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSBUILDLOGEXPANDEDWILDCARDS"));

        /// <summary>
        /// Cache file existence for the entire process
        /// </summary>
        public readonly bool CacheFileExistence = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MsBuildCacheFileExistence"));

        public readonly bool UseSimpleProjectRootElementCacheConcurrency = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MsBuildUseSimpleProjectRootElementCacheConcurrency"));

        /// <summary>
        /// Cache wildcard expansions for the entire process
        /// </summary>
        public readonly bool MSBuildCacheFileEnumerations = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MsBuildCacheFileEnumerations"));

        public readonly bool EnableAllPropertyFunctions = Environment.GetEnvironmentVariable("MSBUILDENABLEALLPROPERTYFUNCTIONS") == "1";

        /// <summary>
        /// Enable restore first functionality in MSBuild.exe
        /// </summary>
        public readonly bool EnableRestoreFirst = Environment.GetEnvironmentVariable("MSBUILDENABLERESTOREFIRST") == "1";

        /// <summary>
        /// Allow the user to specify that two processes should not be communicating via an environment variable.
        /// </summary>
        public static readonly string MSBuildNodeHandshakeSalt = Environment.GetEnvironmentVariable("MSBUILDNODEHANDSHAKESALT");

        /// <summary>
        /// Setting the associated environment variable to 1 restores the pre-15.8 single
        /// threaded (slower) copy behavior. Zero implies Int32.MaxValue, less than zero
        /// (default) uses the empirical default in Copy.cs, greater than zero can allow
        /// perf tuning beyond the defaults chosen.
        /// </summary>
        public readonly int CopyTaskParallelism = ParseIntFromEnvironmentVariableOrDefault("MSBUILDCOPYTASKPARALLELISM", -1);

        /// <summary>
        /// Instruct MSBuild to write out the generated "metaproj" file to disk when building a solution file.
        /// </summary>
        public readonly bool EmitSolutionMetaproj = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSBuildEmitSolution"));

        /// <summary>
        /// Log statistics about property functions which require reflection
        /// </summary>
        public readonly bool LogPropertyFunctionsRequiringReflection = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSBuildLogPropertyFunctionsRequiringReflection"));

        /// <summary>
        /// Log property tracking information.
        /// </summary>
        public readonly int LogPropertyTracking = ParseIntFromEnvironmentVariableOrDefault("MsBuildLogPropertyTracking", 0); // Default to logging nothing via the property tracker.

        public readonly bool DebugEngine = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSBuildDebugEngine"));
        public readonly bool DebugScheduler;
        public readonly bool DebugNodeCommunication;

        private static int ParseIntFromEnvironmentVariableOrDefault(string environmentVariable, int defaultValue)
        {
            return int.TryParse(Environment.GetEnvironmentVariable(environmentVariable), out int result)
                ? result
                : defaultValue;
        }
    }

    internal class EscapeHatches
    {
        /// <summary>
        /// Do not log command line information to build loggers. Useful to unbreak people who parse the msbuild log and who are unwilling to change their code.
        /// </summary>
        public readonly bool DoNotSendDeferredMessagesToBuildManager = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MsBuildDoNotSendDeferredMessagesToBuildManager"));

        /// <summary>
        /// https://github.com/microsoft/msbuild/pull/4975 started expanding qualified metadata in Update operations. Before they'd expand to empty strings.
        /// This escape hatch turns back the old empty string behavior.
        /// </summary>
        public readonly bool DoNotExpandQualifiedMetadataInUpdateOperation = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSBuildDoNotExpandQualifiedMetadataInUpdateOperation"));

        /// <summary>
        /// Force whether Project based evaluations should evaluate elements with false conditions.
        /// </summary>
        public readonly bool? EvaluateElementsWithFalseConditionInProjectEvaluation = ParseNullableBoolFromEnvironmentVariable("MSBUILDEVALUATEELEMENTSWITHFALSECONDITIONINPROJECTEVALUATION");

        /// <summary>
        /// Always use the accurate-but-slow CreateFile approach to timestamp extraction.
        /// </summary>
        public readonly bool AlwaysUseContentTimestamp = Environment.GetEnvironmentVariable("MSBUILDALWAYSCHECKCONTENTTIMESTAMP") == "1";

        /// <summary>
        /// Truncate task inputs when logging them. This can reduce memory pressure
        /// at the expense of log usefulness.
        /// </summary>
        public readonly bool TruncateTaskInputs = Environment.GetEnvironmentVariable("MSBUILDTRUNCATETASKINPUTS") == "1";

        /// <summary>
        /// Disables truncation of Condition messages in Tasks/Targets via ExpanderOptions.Truncate.
        /// </summary>
        public readonly bool DoNotTruncateConditions = Environment.GetEnvironmentVariable("MSBuildDoNotTruncateConditions") == "1";

        /// <summary>
        /// Disables skipping full drive/filesystem globs that are behind a false condition.
        /// </summary>
        public readonly bool AlwaysEvaluateDangerousGlobs = Environment.GetEnvironmentVariable("MSBuildAlwaysEvaluateDangerousGlobs") == "1";

        /// <summary>
        /// Emit events for project imports.
        /// </summary>
        private bool? _logProjectImports;

        /// <summary>
        /// Emit events for project imports.
        /// </summary>
        public bool LogProjectImports
        {
            get
            {
                // Cache the first time
                if (_logProjectImports == null)
                {
                    _logProjectImports = !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSBUILDLOGIMPORTS"));
                }
                return _logProjectImports.Value;
            }
            set
            {
                _logProjectImports = value;
            }
        }

        private bool? _logTaskInputs;
        public bool LogTaskInputs
        {
            get
            {
                if (_logTaskInputs == null)
                {
                    _logTaskInputs = Environment.GetEnvironmentVariable("MSBUILDLOGTASKINPUTS") == "1";
                }
                return _logTaskInputs.Value;
            }
            set
            {
                _logTaskInputs = value;
            }
        }

        private bool? _logPropertiesAndItemsAfterEvaluation;
        private bool _logPropertiesAndItemsAfterEvaluationInitialized = false;
        public bool? LogPropertiesAndItemsAfterEvaluation
        {
            get
            {
                if (!_logPropertiesAndItemsAfterEvaluationInitialized)
                {
                    _logPropertiesAndItemsAfterEvaluationInitialized = true;
                    var variable = Environment.GetEnvironmentVariable("MSBUILDLOGPROPERTIESANDITEMSAFTEREVALUATION");
                    if (!string.IsNullOrEmpty(variable))
                    {
                        _logPropertiesAndItemsAfterEvaluation = variable == "1" || string.Equals(variable, "true", StringComparison.OrdinalIgnoreCase);
                    }
                }

                return _logPropertiesAndItemsAfterEvaluation;
            }

            set
            {
                _logPropertiesAndItemsAfterEvaluationInitialized = true;
                _logPropertiesAndItemsAfterEvaluation = value;
            }
        }

        /// <summary>
        /// Read information only once per file per ResolveAssemblyReference invocation.
        /// </summary>
        public readonly bool CacheAssemblyInformation = Environment.GetEnvironmentVariable("MSBUILDDONOTCACHERARASSEMBLYINFORMATION") != "1";

        public readonly ProjectInstanceTranslationMode? ProjectInstanceTranslation = ComputeProjectInstanceTranslation();

        /// <summary>
        /// Never use the slow (but more accurate) CreateFile approach to timestamp extraction.
        /// </summary>
        public readonly bool UseSymlinkTimeInsteadOfTargetTime = Environment.GetEnvironmentVariable("MSBUILDUSESYMLINKTIMESTAMP") == "1";

        /// <summary>
        /// Allow node reuse of TaskHost nodes. This results in task assemblies locked past the build lifetime, preventing them from being rebuilt if custom tasks change, but may improve performance.
        /// </summary>
        public readonly bool ReuseTaskHostNodes = Environment.GetEnvironmentVariable("MSBUILDREUSETASKHOSTNODES") == "1";

        /// <summary>
        /// Whether or not to ignore imports that are considered empty.  See ProjectRootElement.IsEmptyXmlFile() for more info.
        /// </summary>
        public readonly bool IgnoreEmptyImports = Environment.GetEnvironmentVariable("MSBUILDIGNOREEMPTYIMPORTS") == "1";

        /// <summary>
        /// Whether to respect the TreatAsLocalProperty parameter on the Project tag.
        /// </summary>
        public readonly bool IgnoreTreatAsLocalProperty = Environment.GetEnvironmentVariable("MSBUILDIGNORETREATASLOCALPROPERTY") != null;

        /// <summary>
        /// Whether to write information about why we evaluate to debug output.
        /// </summary>
        public readonly bool DebugEvaluation = Environment.GetEnvironmentVariable("MSBUILDDEBUGEVALUATION") != null;

        /// <summary>
        /// Whether to warn when we set a property for the first time, after it was previously used.
        /// </summary>
        public readonly bool WarnOnUninitializedProperty = !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSBUILDWARNONUNINITIALIZEDPROPERTY"));

        /// <summary>
        /// MSBUILDUSECASESENSITIVEITEMNAMES is an escape hatch for the fix
        /// for https://github.com/Microsoft/msbuild/issues/1751. It should
        /// be removed (permanently set to false) after establishing that
        /// it's unneeded (at least by the 16.0 timeframe).
        /// </summary>
        public readonly bool UseCaseSensitiveItemNames = Environment.GetEnvironmentVariable("MSBUILDUSECASESENSITIVEITEMNAMES") == "1";

        /// <summary>
        /// Disable the use of paths longer than Windows MAX_PATH limits (260 characters) when running on a long path enabled OS.
        /// </summary>
        public readonly bool DisableLongPaths = Environment.GetEnvironmentVariable("MSBUILDDISABLELONGPATHS") == "1";

        /// <summary>
        /// Disable the use of any caching when resolving SDKs.
        /// </summary>
        public readonly bool DisableSdkResolutionCache = Environment.GetEnvironmentVariable("MSBUILDDISABLESDKCACHE") == "1";

        /// <summary>
        /// Disable the NuGet-based SDK resolver.
        /// </summary>
        public readonly bool DisableNuGetSdkResolver = Environment.GetEnvironmentVariable("MSBUILDDISABLENUGETSDKRESOLVER") == "1";

        /// <summary>
        /// Don't delete TargetPath metadata from associated files found by RAR.
        /// </summary>
        public readonly bool TargetPathForRelatedFiles = Environment.GetEnvironmentVariable("MSBUILDTARGETPATHFORRELATEDFILES") == "1";

        /// <summary>
        /// Disable AssemblyLoadContext isolation for plugins.
        /// </summary>
        public readonly bool UseSingleLoadContext = Environment.GetEnvironmentVariable("MSBUILDSINGLELOADCONTEXT") == "1";

        /// <summary>
        /// Enables the user of autorun functionality in CMD.exe on Windows which is disabled by default in MSBuild.
        /// </summary>
        public readonly bool UseAutoRunWhenLaunchingProcessUnderCmd = Environment.GetEnvironmentVariable("MSBUILDUSERAUTORUNINCMD") == "1";

        /// <summary>
        /// Disables switching codepage to UTF-8 after detection of characters that can't be represented in the current codepage.
        /// </summary>
        public readonly bool AvoidUnicodeWhenWritingToolTaskBatch = Environment.GetEnvironmentVariable("MSBUILDAVOIDUNICODE") == "1";

        /// <summary>
        /// Workaround for https://github.com/Microsoft/vstest/issues/1503.
        /// </summary>
        public readonly bool EnsureStdOutForChildNodesIsPrimaryStdout = Environment.GetEnvironmentVariable("MSBUILDENSURESTDOUTFORTASKPROCESSES") == "1";

        /// <summary>
        /// Use the original, string-only resx parsing in .NET Core scenarios.
        /// </summary>
        /// <remarks>
        /// Escape hatch for problems arising from https://github.com/microsoft/msbuild/pull/4420.
        /// </remarks>
        public readonly bool UseMinimalResxParsingInCoreScenarios = Environment.GetEnvironmentVariable("MSBUILDUSEMINIMALRESX") == "1";

        private bool _sdkReferencePropertyExpansionInitialized;
        private SdkReferencePropertyExpansionMode? _sdkReferencePropertyExpansionValue;

        /// <summary>
        /// Overrides the default behavior of property expansion on evaluation of a <see cref="Framework.SdkReference"/>.
        /// </summary>
        /// <remarks>
        /// Escape hatch for problems arising from https://github.com/dotnet/msbuild/pull/5552.
        /// </remarks>
        public SdkReferencePropertyExpansionMode? SdkReferencePropertyExpansion
        {
            get
            {
                if (!_sdkReferencePropertyExpansionInitialized)
                {
                    _sdkReferencePropertyExpansionValue = ComputeSdkReferencePropertyExpansion();
                    _sdkReferencePropertyExpansionInitialized = true;
                }

                return _sdkReferencePropertyExpansionValue;
            }
        }

        private static bool? ParseNullableBoolFromEnvironmentVariable(string environmentVariable)
        {
            var value = Environment.GetEnvironmentVariable(environmentVariable);

            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            if (bool.TryParse(value, out bool result))
            {
                return result;
            }

            ErrorUtilities.ThrowInternalError($"Environment variable \"{environmentVariable}\" should have values \"true\", \"false\" or undefined");

            return null;
        }

        private static ProjectInstanceTranslationMode? ComputeProjectInstanceTranslation()
        {
            var mode = Environment.GetEnvironmentVariable("MSBUILD_PROJECTINSTANCE_TRANSLATION_MODE");

            if (mode == null)
            {
                return null;
            }

            if (mode.Equals("full", StringComparison.OrdinalIgnoreCase))
            {
                return ProjectInstanceTranslationMode.Full;
            }

            if (mode.Equals("partial", StringComparison.OrdinalIgnoreCase))
            {
                return ProjectInstanceTranslationMode.Partial;
            }

            ErrorUtilities.ThrowInternalError($"Invalid escape hatch for project instance translation: {mode}");

            return null;
        }

        private static SdkReferencePropertyExpansionMode? ComputeSdkReferencePropertyExpansion()
        {
            var mode = Environment.GetEnvironmentVariable("MSBUILD_SDKREFERENCE_PROPERTY_EXPANSION_MODE");

            if (mode == null)
            {
                return null;
            }

            // The following uses StartsWith instead of Equals to enable possible tricks like
            // the dpiAware "True/PM" trick (see https://devblogs.microsoft.com/oldnewthing/20160617-00/?p=93695)
            // in the future.

            const StringComparison comparison = StringComparison.OrdinalIgnoreCase;

            if (mode.StartsWith("no", comparison))
            {
                return SdkReferencePropertyExpansionMode.NoExpansion;
            }

            if (mode.StartsWith("default", comparison))
            {
                return SdkReferencePropertyExpansionMode.DefaultExpand;
            }

            if (mode.StartsWith(nameof(SdkReferencePropertyExpansionMode.ExpandUnescape), comparison))
            {
                return SdkReferencePropertyExpansionMode.ExpandUnescape;
            }

            if (mode.StartsWith(nameof(SdkReferencePropertyExpansionMode.ExpandLeaveEscaped), comparison))
            {
                return SdkReferencePropertyExpansionMode.ExpandLeaveEscaped;
            }

            ErrorUtilities.ThrowInternalError($"Invalid escape hatch for SdkReference property expansion: {mode}");

            return null;
        }

        public enum ProjectInstanceTranslationMode
        {
            Full,
            Partial
        }

        public enum SdkReferencePropertyExpansionMode
        {
            NoExpansion,
            DefaultExpand,
            ExpandUnescape,
            ExpandLeaveEscaped
        }
    }
}
