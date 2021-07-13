// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Framework
{
    public enum TaskParameterMessageKind
    {
        TaskInput,
        TaskOutput,
        AddItem,
        RemoveItem,
        SkippedTargetInputs,
        SkippedTargetOutputs
    }

    /// <summary>
    /// This class is used by tasks to log their parameters (input, output).
    /// The intrinsic ItemGroupIntrinsicTask to add or remove items also
    /// uses this class.
    /// </summary>
    public class TaskParameterEventArgs : BuildMessageEventArgs
    {
        /// <summary>
        /// Creates an instance of this class for the given task parameter.
        /// </summary>
        public TaskParameterEventArgs
        (
            TaskParameterMessageKind kind,
            string itemType,
            IList items,
            bool logItemMetadata,
            DateTime eventTimestamp
        )
            : base(null, null, null, MessageImportance.Low, eventTimestamp)
        {
            Kind = kind;
            ItemType = itemType;
            Items = items;
            LogItemMetadata = logItemMetadata;
        }

        public TaskParameterMessageKind Kind { get; private set; }
        public string ItemType { get; private set; }
        public IList Items { get; private set; }
        public bool LogItemMetadata { get; private set; }

        /// <summary>
        /// The <see cref="TaskParameterEventArgs"/> type is declared in Microsoft.Build.Framework.dll
        /// which is a declarations assembly. The logic to realize the Message is in Microsoft.Build.dll
        /// which is an implementations assembly. This seems like the easiest way to inject the
        /// implementation for realizing the Message.
        /// </summary>
        /// <remarks>
        /// Note that the current implementation never runs and is provided merely
        /// as a safeguard in case MessageGetter isn't set for some reason.
        /// </remarks>
        internal static Func<TaskParameterEventArgs, string> MessageGetter = args =>
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{args.Kind}: {args.ItemType}");
            foreach (var item in args.Items)
            {
                sb.AppendLine(item.ToString());
            }

            return sb.ToString();
        };

        /// <summary>
        /// Provides a way for Microsoft.Build.dll to provide a more efficient dictionary factory
        /// (using ArrayDictionary`2). Since that is an implementation detail, it is not included
        /// in Microsoft.Build.Framework.dll so we need this extensibility point here.
        /// </summary>
        internal static Func<int, IDictionary<string, string>> DictionaryFactory = capacity => new Dictionary<string, string>(capacity);

        internal override void CreateFromStream(BinaryReader reader, int version)
        {
            RawTimestamp = reader.ReadTimestamp();
            BuildEventContext = reader.ReadOptionalBuildEventContext();
            Kind = (TaskParameterMessageKind)reader.Read7BitEncodedInt();
            ItemType = reader.ReadOptionalString();
            LineNumber = reader.Read7BitEncodedInt();
            ColumnNumber = reader.Read7BitEncodedInt();
            Items = ReadItems(reader);
        }

        private IList ReadItems(BinaryReader reader)
        {
            var list = new ArrayList();

            int count = reader.Read7BitEncodedInt();
            for (int i = 0; i < count; i++)
            {
                var item = ReadItem(reader);
                list.Add(item);
            }

            return list;
        }

        private object ReadItem(BinaryReader reader)
        {
            string itemSpec = reader.ReadString();
            int metadataCount = reader.Read7BitEncodedInt();
            if (metadataCount == 0)
            {
                return new TaskItemData(itemSpec, metadata: null);
            }

            var metadata = DictionaryFactory(metadataCount);
            for (int i = 0; i < metadataCount; i++)
            {
                string key = reader.ReadString();
                string value = reader.ReadString();
                if (key != null)
                {
                    metadata.Add(key, value);
                }
            }

            var taskItem = new TaskItemData(itemSpec, metadata);
            return taskItem;
        }

        internal override void WriteToStream(BinaryWriter writer)
        {
            writer.WriteTimestamp(RawTimestamp);
            writer.WriteOptionalBuildEventContext(BuildEventContext);
            writer.Write7BitEncodedInt((int)Kind);
            writer.WriteOptionalString(ItemType);
            writer.Write7BitEncodedInt(LineNumber);
            writer.Write7BitEncodedInt(ColumnNumber);
            WriteItems(writer, Items);
        }

        private void WriteItems(BinaryWriter writer, IList items)
        {
            if (items == null)
            {
                writer.Write7BitEncodedInt(0);
                return;
            }

            int count = items.Count;
            writer.Write7BitEncodedInt(count);

            for (int i = 0; i < count; i++)
            {
                var item = items[i];
                WriteItem(writer, item);
            }
        }

        private void WriteItem(BinaryWriter writer, object item)
        {
            if (item is ITaskItem taskItem)
            {
                writer.Write(taskItem.ItemSpec);
                if (LogItemMetadata)
                {
                    WriteMetadata(writer, taskItem);
                }
                else
                {
                    writer.Write7BitEncodedInt(0);
                }
            }
            else // string or ValueType
            {
                writer.Write(item?.ToString() ?? "");
                writer.Write7BitEncodedInt(0);
            }
        }

        [ThreadStatic]
        private static List<KeyValuePair<string, string>> reusableMetadataList;

        private void WriteMetadata(BinaryWriter writer, ITaskItem taskItem)
        {
            if (reusableMetadataList == null)
            {
                reusableMetadataList = new List<KeyValuePair<string, string>>();
            }

            // WARNING: Can't use AddRange here because CopyOnWriteDictionary in Microsoft.Build.Utilities.v4.0.dll
            // is broken. Microsoft.Build.Utilities.v4.0.dll loads from the GAC by XAML markup tooling and it's
            // implementation doesn't work with AddRange because AddRange special-cases ICollection<T> and
            // CopyOnWriteDictionary doesn't implement it properly.
            foreach (var kvp in taskItem.EnumerateMetadata())
            {
                reusableMetadataList.Add(kvp);
            }

            writer.Write7BitEncodedInt(reusableMetadataList.Count);
            if (reusableMetadataList.Count == 0)
            {
                return;
            }

            foreach (var kvp in reusableMetadataList)
            {
                writer.Write(kvp.Key);
                writer.Write(kvp.Value);
            }

            reusableMetadataList.Clear();
        }

        public override string Message
        {
            get
            {
                lock (this)
                {
                    if (base.Message == null)
                    {
                        base.Message = MessageGetter(this);
                    }

                    return base.Message;
                }
            }
        }
    }
}
