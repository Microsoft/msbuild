﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections;
using System.Collections.Generic;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.Build.Tasks.ResolveAssemblyReferences.Contract
{
    internal sealed class ReadOnlyTaskItem
    {
        public string ItemSpec { get; set; }

        public Dictionary<string, string> MetadataNameToValue { get; set; }

        public ReadOnlyTaskItem(string itemSpec)
        {
            ItemSpec = itemSpec;
            MetadataNameToValue = new Dictionary<string, string>();
        }

        public ReadOnlyTaskItem(string itemSpec, IDictionary metadata)
        {
            ItemSpec = itemSpec;

            if (metadata is Dictionary<string, string> metadataNameToValue)
            {
                MetadataNameToValue = metadataNameToValue;
            }
            else if (metadata is IDictionary<string, string> metadataByInterface)
            {
                MetadataNameToValue = new Dictionary<string, string>(metadataByInterface);
            }
            else
            {
                if (metadata.Count > 0)
                {
                    MetadataNameToValue = new Dictionary<string, string>();

                    foreach (DictionaryEntry singleMetadata in metadata)
                    {
                        string key = (string)singleMetadata.Key;
                        if (key != null)
                        {
                            MetadataNameToValue[key] = (string)singleMetadata.Value ?? string.Empty;
                        }
                    }
                }
            }
        }

        internal static ReadOnlyTaskItem[] ToReadOnlyItems(ITaskItem[] items)
        {
            if (items == null)
                return null;

            ReadOnlyTaskItem[] readOnlyTaskItems = new ReadOnlyTaskItem[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == null)
                    continue;

                ReadOnlyTaskItem readOnlyTaskItem = new ReadOnlyTaskItem(items[i].ItemSpec, items[i].CloneCustomMetadata());
                readOnlyTaskItems[i] = readOnlyTaskItem;
            }

            return readOnlyTaskItems;
        }

        internal static ITaskItem[] ToTaskItems(ReadOnlyTaskItem[] readOnlyTaskItems)
        {
            if (readOnlyTaskItems == null)
                return null;

            ITaskItem[] items = new ITaskItem[readOnlyTaskItems.Length];
            for (int i = 0; i < items.Length; i++)
            {
                if (readOnlyTaskItems[i] == null)
                    continue;

                TaskItem item = new TaskItem(readOnlyTaskItems[i].ItemSpec, readOnlyTaskItems[i].MetadataNameToValue);
                items[i] = item;
            }

            return items;
        }
    }
}
