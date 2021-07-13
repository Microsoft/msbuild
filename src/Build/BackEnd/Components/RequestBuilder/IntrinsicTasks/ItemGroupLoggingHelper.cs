// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Remoting;
using Microsoft.Build.BackEnd.Logging;
using Microsoft.Build.Collections;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;
using Microsoft.Build.Utilities;

namespace Microsoft.Build.BackEnd
{
    /// <summary>
    /// Helper class to convert ItemGroup parameters to a string value for logging.
    /// </summary>
    internal static class ItemGroupLoggingHelper
    {
        /// <summary>
        /// The default character limit for logging parameters. 10k is somewhat arbitrary, see https://github.com/microsoft/msbuild/issues/4907.
        /// </summary>
        internal static int parameterCharacterLimit = 40_000;

        /// <summary>
        /// The default parameter limit for logging. 200 is somewhat arbitrary, see https://github.com/microsoft/msbuild/pull/5210.
        /// </summary>
        internal static int parameterLimit = 200;

        internal static string ItemGroupIncludeLogMessagePrefix = ResourceUtilities.GetResourceString("ItemGroupIncludeLogMessagePrefix");
        internal static string ItemGroupRemoveLogMessage = ResourceUtilities.GetResourceString("ItemGroupRemoveLogMessage");
        internal static string OutputItemParameterMessagePrefix = ResourceUtilities.GetResourceString("OutputItemParameterMessagePrefix");
        internal static string TaskParameterPrefix = ResourceUtilities.GetResourceString("TaskParameterPrefix");
        internal static string SkipTargetUpToDateInputs = ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword("SkipTargetUpToDateInputs", string.Empty);
        internal static string SkipTargetUpToDateOutputs = ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword("SkipTargetUpToDateOutputs", string.Empty);

        /// <summary>
        /// <see cref="TaskParameterEventArgs"/> by itself doesn't have the implementation
        /// to materialize the Message as that's a declaration assembly. We inject the logic
        /// here.
        /// </summary>
        static ItemGroupLoggingHelper()
        {
            BuildEventArgs.ResourceStringFormatter = ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword;
            TaskParameterEventArgs.MessageGetter = GetTaskParameterText;
            TaskParameterEventArgs.DictionaryFactory = ArrayDictionary<string, string>.Create;
        }

        /// <summary>
        /// Gets a text serialized value of a parameter for logging.
        /// </summary>
        internal static string GetParameterText(string prefix, string parameterName, IList parameterValue, bool logItemMetadata = true)
        {
            if (parameterValue == null || parameterValue.Count == 0)
            {
                return parameterName;
            }

            using (var sb = new ReuseableStringBuilder())
            {
                sb.Append(prefix);

                bool firstEntryIsTaskItemWithSomeCustomMetadata = false;
                var firstItem = parameterValue[0] as ITaskItem;
                if (firstItem != null)
                {
                    if (firstItem.CloneCustomMetadata().Count > 0)
                    {
                        firstEntryIsTaskItemWithSomeCustomMetadata = true;
                    }
                }

                // If it's just one entry in the list, and it's not a task item with metadata, keep it on one line like a scalar
                bool specialTreatmentForSingle = (parameterValue.Count == 1 && !firstEntryIsTaskItemWithSomeCustomMetadata);

                // If the parameterName is not specified, no need to have an extra indent.
                // Without parameterName:
                //
                // Input files: 
                //     a.txt
                //     b.txt
                //
                // With parameterName:
                //
                // Input files:
                //     ParamName=
                //         a.txt
                //         b.txt
                string indent = "        ";
                if (parameterName == null)
                {
                    indent = "    ";
                }

                if (!specialTreatmentForSingle)
                {
                    sb.Append("\n");
                    if (parameterName != null)
                    {
                        sb.Append("    ");
                    }
                }

                if (parameterName != null)
                {
                    sb.Append(parameterName);
                    sb.Append('=');

                    if (!specialTreatmentForSingle)
                    {
                        sb.Append("\n");
                    }
                }

                bool truncateTaskInputs = Traits.Instance.EscapeHatches.TruncateTaskInputs;

                for (int i = 0; i < parameterValue.Count; i++)
                {
                    if (parameterValue[i] == null)
                    {
                        continue;
                    }

                    if (!specialTreatmentForSingle)
                    {
                        sb.Append(indent);
                    }

                    AppendStringFromParameterValue(sb, parameterValue[i], logItemMetadata);

                    if (!specialTreatmentForSingle && i < parameterValue.Count - 1)
                    {
                        sb.Append("\n");
                    }

                    if (truncateTaskInputs && (sb.Length >= parameterCharacterLimit || i > parameterLimit))
                    {
                        sb.Append(ResourceUtilities.GetResourceString("LogTaskInputs.Truncated"));
                        break;
                    }
                }

                return sb.ToString();
            }
        }

        /// <summary>
        /// Given an object wrapping a scalar value that will be set on a task,
        /// returns a suitable string to log its value, with a trailing newline.
        /// First line is already indented.
        /// Indent of any subsequent line should be 12 spaces.
        /// </summary>
        internal static string GetStringFromParameterValue(object parameterValue, bool logItemMetadata = true)
        {
            // fast path for the common case
            if (parameterValue is string valueText)
            {
                return valueText;
            }

            using (var sb = new ReuseableStringBuilder())
            {
                AppendStringFromParameterValue(sb, parameterValue, logItemMetadata);
                return sb.ToString();
            }
        }

        // Avoid allocating a temporary list to hold metadata for sorting every time.
        // Each thread gets its own copy.
        [ThreadStatic]
        private static List<KeyValuePair<string, string>> keyValuePairList;

        private static void AppendStringFromParameterValue(ReuseableStringBuilder sb, object parameterValue, bool logItemMetadata = true)
        {
            if (parameterValue is string text)
            {
                sb.Append(text);
            }
            else if (parameterValue is ITaskItem item)
            {
                sb.Append(item.ItemSpec);

                if (!logItemMetadata)
                {
                    return;
                }

                var customMetadata = item.CloneCustomMetadata();
                int count = customMetadata.Count;

                if (count > 0)
                {
                    sb.Append('\n');

                    // need to initialize the thread static on each new thread
                    if (keyValuePairList == null)
                    {
                        keyValuePairList = new List<KeyValuePair<string, string>>(count);
                    }

                    if (customMetadata is IDictionary<string, string> customMetadataDictionary)
                    {
                        foreach (KeyValuePair<string, string> kvp in customMetadataDictionary)
                        {
                            keyValuePairList.Add(kvp);
                        }
                    }
                    else
                    {
                        foreach (DictionaryEntry kvp in customMetadata)
                        {
                            keyValuePairList.Add(new KeyValuePair<string, string>((string)kvp.Key, (string)kvp.Value));
                        }
                    }

                    if (count > 1)
                    {
                        keyValuePairList.Sort((l, r) => StringComparer.OrdinalIgnoreCase.Compare(l.Key, r.Key));
                    }

                    for (int i = 0; i < count; i++)
                    {
                        var kvp = keyValuePairList[i];
                        sb.Append("                ");
                        sb.Append(kvp.Key);
                        sb.Append('=');
                        sb.Append(kvp.Value);

                        if (i < count - 1)
                        {
                            sb.Append('\n');
                        }
                    }

                    keyValuePairList.Clear();
                }
            }
            else if (parameterValue.GetType().IsValueType)
            {
                sb.Append((string)Convert.ChangeType(parameterValue, typeof(string), CultureInfo.CurrentCulture));
            }
            else
            {
                ErrorUtilities.ThrowInternalErrorUnreachable();
            }
        }

        internal static void LogTaskParameter(
            LoggingContext loggingContext,
            TaskParameterMessageKind messageKind,
            string itemType,
            IList items,
            bool logItemMetadata,
            IElementLocation location = null)
        {
            var args = CreateTaskParameterEventArgs(
                loggingContext.BuildEventContext,
                messageKind,
                itemType,
                items,
                logItemMetadata,
                DateTime.UtcNow,
                location?.Line ?? 0,
                location?.Column ?? 0);

            loggingContext.LogBuildEvent(args);
        }

        internal static TaskParameterEventArgs CreateTaskParameterEventArgs(
            BuildEventContext buildEventContext,
            TaskParameterMessageKind messageKind,
            string itemType,
            IList items,
            bool logItemMetadata,
            DateTime timestamp,
            int line = 0,
            int column = 0)
        {
            // Only create a snapshot of items if we use AppDomains
#if FEATURE_APPDOMAIN
            CreateItemsSnapshot(ref items);
#endif

            var args = new TaskParameterEventArgs(
                messageKind,
                itemType,
                items,
                logItemMetadata,
                timestamp);
            args.BuildEventContext = buildEventContext;
            args.LineNumber = line;
            args.ColumnNumber = column;
            return args;
        }

#if FEATURE_APPDOMAIN
        private static void CreateItemsSnapshot(ref IList items)
        {
            if (items == null)
            {
                return;
            }

            // If we're in the default AppDomain, but any of the items come from a different AppDomain
            // we need to take a snapshot of the items right now otherwise that AppDomain might get
            // unloaded by the time we want to consume the items.
            // If we're not in the default AppDomain, always take the items snapshot.
            //
            // It is unfortunate to need to be doing this check, but ResolveComReference and other tasks
            // still use AppDomains and create a TaskParameterEventArgs in the default AppDomain, but
            // pass it Items from another AppDomain.
            if (AppDomain.CurrentDomain.IsDefaultAppDomain())
            {
                bool needsSnapshot = false;
                foreach (var item in items)
                {
                    if (RemotingServices.IsTransparentProxy(item))
                    {
                        needsSnapshot = true;
                        break;
                    }
                }

                if (!needsSnapshot)
                {
                    return;
                }
            }

            int count = items.Count;
            var cloned = new object[count];

            for (int i = 0; i < count; i++)
            {
                var item = items[i];
                if (item is ITaskItem taskItem)
                {
                    cloned[i] = new TaskItemData(taskItem);
                }
                else
                {
                    cloned[i] = item;
                }
            }

            items = cloned;
        }
#endif

        internal static string GetTaskParameterText(TaskParameterEventArgs args)
            => GetTaskParameterText(args.Kind, args.ItemType, args.Items, args.LogItemMetadata);

        internal static string GetTaskParameterText(TaskParameterMessageKind messageKind, string itemType, IList items, bool logItemMetadata)
        {
            var resourceText = messageKind switch
            {
                TaskParameterMessageKind.AddItem => ItemGroupIncludeLogMessagePrefix,
                TaskParameterMessageKind.RemoveItem => ItemGroupRemoveLogMessage,
                TaskParameterMessageKind.TaskInput => TaskParameterPrefix,
                TaskParameterMessageKind.TaskOutput => OutputItemParameterMessagePrefix,
                TaskParameterMessageKind.SkippedTargetInputs => SkipTargetUpToDateInputs,
                TaskParameterMessageKind.SkippedTargetOutputs => SkipTargetUpToDateOutputs,
                _ => throw new NotImplementedException($"Unsupported {nameof(TaskParameterMessageKind)} value: {messageKind}")
            };

            var itemGroupText = GetParameterText(
                resourceText,
                itemType,
                items,
                logItemMetadata);
            return itemGroupText;
        }
    }
}
