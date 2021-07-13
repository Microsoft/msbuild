// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Framework
{
    /// <summary>
    /// A reason why a target was skipped.
    /// </summary>
    public enum TargetSkipReason
    {
        /// <summary>
        /// The target was not skipped or the skip reason was unknown.
        /// </summary>
        None,

        /// <summary>
        /// The target previously built successfully.
        /// </summary>
        PreviouslyBuiltSuccessfully,

        /// <summary>
        /// The target previously built unsuccessfully.
        /// </summary>
        PreviouslyBuiltUnsuccessfully,

        /// <summary>
        /// All the target outputs were up-to-date with respect to their inputs.
        /// </summary>
        OutputsUpToDate,

        /// <summary>
        /// The condition on the target was evaluated as false.
        /// </summary>
        ConditionWasFalse
    }

    /// <summary>
    /// Arguments for the target skipped event.
    /// </summary>
    [Serializable]
    public class TargetSkippedEventArgs : BuildMessageEventArgs
    {
        /// <summary>
        /// Initializes a new instance of the TargetSkippedEventArgs class.
        /// </summary>
        public TargetSkippedEventArgs()
        {
        }

        /// <summary>
        /// Initializes a new instance of the TargetSkippedEventArgs class.
        /// </summary>
        public TargetSkippedEventArgs
        (
            string message,
            params object[] messageArgs
        )
            : base(
                  subcategory: null,
                  code: null,
                  file: null,
                  lineNumber: 0,
                  columnNumber: 0,
                  endLineNumber: 0,
                  endColumnNumber: 0,
                  message: message,
                  helpKeyword: null,
                  senderName: null,
                  importance: MessageImportance.Low,
                  eventTimestamp: DateTime.UtcNow,
                  messageArgs: messageArgs)
        {
        }

        /// <summary>
        /// The reason why the target was skipped.
        /// </summary>
        public TargetSkipReason SkipReason { get; set; }

        /// <summary>
        /// Gets or sets the name of the target being skipped.
        /// </summary>
        public string TargetName { get; set; }

        /// <summary>
        /// Gets or sets the parent target of the target being skipped.
        /// </summary>
        public string ParentTarget { get; set; }

        /// <summary>
        /// File where this target was declared.
        /// </summary>
        public string TargetFile { get; set; }

        /// <summary>
        /// Why the parent target built this target.
        /// </summary>
        public TargetBuiltReason BuildReason { get; set; }

        /// <summary>
        /// Whether the target succeeded originally.
        /// </summary>
        public bool OriginallySucceeded { get; set; }

        /// <summary>
        /// <see cref="BuildEventContext"/> describing the original build of the target, or null if not available.
        /// </summary>
        public BuildEventContext OriginalBuildEventContext { get; set; }

        /// <summary>
        /// The condition expression on the target declaration.
        /// </summary>
        public string Condition { get; set; }

        /// <summary>
        /// The value of the condition expression as it was evaluated.
        /// </summary>
        public string EvaluatedCondition { get; set; }

        internal override void WriteToStream(BinaryWriter writer)
        {
            base.WriteToStream(writer);

            writer.WriteOptionalString(TargetName);
            writer.WriteOptionalString(ParentTarget);
            writer.WriteOptionalString(TargetFile);
            writer.WriteOptionalString(Condition);
            writer.WriteOptionalString(EvaluatedCondition);
            writer.Write7BitEncodedInt((int)BuildReason);
            writer.Write7BitEncodedInt((int)SkipReason);
            writer.Write(OriginallySucceeded);
            writer.WriteOptionalBuildEventContext(OriginalBuildEventContext);
        }

        internal override void CreateFromStream(BinaryReader reader, int version)
        {
            base.CreateFromStream(reader, version);

            TargetName = reader.ReadOptionalString();
            ParentTarget = reader.ReadOptionalString();
            TargetFile = reader.ReadOptionalString();
            Condition = reader.ReadOptionalString();
            EvaluatedCondition = reader.ReadOptionalString();
            BuildReason = (TargetBuiltReason)reader.Read7BitEncodedInt();
            SkipReason = (TargetSkipReason)reader.Read7BitEncodedInt();
            OriginallySucceeded = reader.ReadBoolean();
            OriginalBuildEventContext = reader.ReadOptionalBuildEventContext();
        }

        public override string Message
        {
            get
            {
                if (RawMessage == null)
                {
                    lock (locker)
                    {
                        if (RawMessage == null)
                        {
                            RawMessage = SkipReason switch
                            {
                                TargetSkipReason.PreviouslyBuiltSuccessfully or TargetSkipReason.PreviouslyBuiltUnsuccessfully =>
                                    FormatResourceStringIgnoreCodeAndKeyword(
                                        OriginallySucceeded
                                        ? "TargetAlreadyCompleteSuccess"
                                        : "TargetAlreadyCompleteFailure",
                                        TargetName),

                                TargetSkipReason.ConditionWasFalse =>
                                    FormatResourceStringIgnoreCodeAndKeyword(
                                        "TargetSkippedFalseCondition",
                                        TargetName,
                                        Condition,
                                        EvaluatedCondition),

                                TargetSkipReason.OutputsUpToDate =>
                                    FormatResourceStringIgnoreCodeAndKeyword(
                                        "SkipTargetBecauseOutputsUpToDate",
                                        TargetName),

                                _ => SkipReason.ToString()
                            };
                        }
                    }
                }

                return RawMessage;
            }
        }
    }
}
