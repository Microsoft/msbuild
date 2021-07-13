// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Runtime.Serialization;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Framework
{
    /// <summary>
    /// This enumeration provides three levels of importance for messages.
    /// </summary>
    [Serializable]
    public enum MessageImportance
    {
        /// <summary>
        /// High importance, appears in less verbose logs
        /// </summary>
        High,

        /// <summary>
        /// Normal importance
        /// </summary>
        Normal,

        /// <summary>
        /// Low importance, appears in more verbose logs
        /// </summary>
        Low
    }

    /// <summary>
    /// Arguments for message events
    /// </summary>
    // WARNING: marking a type [Serializable] without implementing
    // ISerializable imposes a serialization contract -- it is a
    // promise to never change the type's fields i.e. the type is
    // immutable; adding new fields in the next version of the type
    // without following certain special FX guidelines, can break both
    // forward and backward compatibility
    [Serializable]
    public class BuildMessageEventArgs : LazyFormattedBuildEventArgs
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        protected BuildMessageEventArgs()
            : base()
        {
            // do nothing
        }

        /// <summary>
        /// This constructor allows all event data to be initialized
        /// </summary>
        /// <param name="message">text message</param>
        /// <param name="helpKeyword">help keyword </param>
        /// <param name="senderName">name of event sender</param>
        /// <param name="importance">importance of the message</param>
        public BuildMessageEventArgs
        (
            string message,
            string helpKeyword,
            string senderName,
            MessageImportance importance
        )
            : this(message, helpKeyword, senderName, importance, DateTime.UtcNow)
        {
        }

        /// <summary>
        /// This constructor allows a timestamp to be set
        /// </summary>
        /// <param name="message">text message</param>
        /// <param name="helpKeyword">help keyword </param>
        /// <param name="senderName">name of event sender</param>
        /// <param name="importance">importance of the message</param>
        /// <param name="eventTimestamp">Timestamp when event was created</param>
        public BuildMessageEventArgs
        (
            string message,
            string helpKeyword,
            string senderName,
            MessageImportance importance,
            DateTime eventTimestamp
        )
            : this(message, helpKeyword, senderName, importance, eventTimestamp, null)
        {
            // do nothing
        }

        /// <summary>
        /// This constructor allows a timestamp to be set
        /// </summary>
        /// <param name="message">text message</param>
        /// <param name="helpKeyword">help keyword </param>
        /// <param name="senderName">name of event sender</param>
        /// <param name="importance">importance of the message</param>
        /// <param name="eventTimestamp">Timestamp when event was created</param>
        /// <param name="messageArgs">message arguments</param>
        public BuildMessageEventArgs
        (
            string message,
            string helpKeyword,
            string senderName,
            MessageImportance importance,
            DateTime eventTimestamp,
            params object[] messageArgs
        )
            : this(null, null, null, 0, 0, 0, 0, message, helpKeyword, senderName, importance, eventTimestamp, messageArgs)
        {
            // do nothing
        }

        /// <summary>
        /// This constructor allows all event data to be initialized
        /// </summary>
        /// <param name="subcategory">event subcategory</param>
        /// <param name="code">event code</param>
        /// <param name="file">file associated with the event</param>
        /// <param name="lineNumber">line number (0 if not applicable)</param>
        /// <param name="columnNumber">column number (0 if not applicable)</param>
        /// <param name="endLineNumber">end line number (0 if not applicable)</param>
        /// <param name="endColumnNumber">end column number (0 if not applicable)</param>
        /// <param name="message">text message</param>
        /// <param name="helpKeyword">help keyword </param>
        /// <param name="senderName">name of event sender</param>
        /// <param name="importance">importance of the message</param>
        public BuildMessageEventArgs
        (
            string subcategory,
            string code,
            string file,
            int lineNumber,
            int columnNumber,
            int endLineNumber,
            int endColumnNumber,
            string message,
            string helpKeyword,
            string senderName,
            MessageImportance importance
        )
            : this(subcategory, code, file, lineNumber, columnNumber, endLineNumber, endColumnNumber, message, helpKeyword, senderName, importance, DateTime.UtcNow)
        {
            // do nothing
        }

        /// <summary>
        /// This constructor allows timestamp to be set
        /// </summary>
        /// <param name="subcategory">event subcategory</param>
        /// <param name="code">event code</param>
        /// <param name="file">file associated with the event</param>
        /// <param name="lineNumber">line number (0 if not applicable)</param>
        /// <param name="columnNumber">column number (0 if not applicable)</param>
        /// <param name="endLineNumber">end line number (0 if not applicable)</param>
        /// <param name="endColumnNumber">end column number (0 if not applicable)</param>
        /// <param name="message">text message</param>
        /// <param name="helpKeyword">help keyword </param>
        /// <param name="senderName">name of event sender</param>
        /// <param name="importance">importance of the message</param>
        /// <param name="eventTimestamp">custom timestamp for the event</param>
        public BuildMessageEventArgs
        (
            string subcategory,
            string code,
            string file,
            int lineNumber,
            int columnNumber,
            int endLineNumber,
            int endColumnNumber,
            string message,
            string helpKeyword,
            string senderName,
            MessageImportance importance,
            DateTime eventTimestamp
        )
            : this(subcategory, code, file, lineNumber, columnNumber, endLineNumber, endColumnNumber, message, helpKeyword, senderName, importance, eventTimestamp, messageArgs: null)
        {
            // do nothing
        }

        /// <summary>
        /// This constructor allows timestamp to be set
        /// </summary>
        /// <param name="subcategory">event subcategory</param>
        /// <param name="code">event code</param>
        /// <param name="file">file associated with the event</param>
        /// <param name="lineNumber">line number (0 if not applicable)</param>
        /// <param name="columnNumber">column number (0 if not applicable)</param>
        /// <param name="endLineNumber">end line number (0 if not applicable)</param>
        /// <param name="endColumnNumber">end column number (0 if not applicable)</param>
        /// <param name="message">text message</param>
        /// <param name="helpKeyword">help keyword </param>
        /// <param name="senderName">name of event sender</param>
        /// <param name="importance">importance of the message</param>
        /// <param name="eventTimestamp">custom timestamp for the event</param>
        /// <param name="messageArgs">message arguments</param>
        public BuildMessageEventArgs
        (
            string subcategory,
            string code,
            string file,
            int lineNumber,
            int columnNumber,
            int endLineNumber,
            int endColumnNumber,
            string message,
            string helpKeyword,
            string senderName,
            MessageImportance importance,
            DateTime eventTimestamp,
            params object[] messageArgs
        )
            : base(message, helpKeyword, senderName, eventTimestamp, messageArgs)
        {
            this.importance = importance;
            this.subcategory = subcategory;
            this.code = code;
            this.file = file;
            this.lineNumber = lineNumber;
            this.columnNumber = columnNumber;
            this.endLineNumber = endLineNumber;
            this.endColumnNumber = endColumnNumber;
        }

        private MessageImportance importance;

        [OptionalField(VersionAdded = 2)]
        private string subcategory;

        [OptionalField(VersionAdded = 2)]
        private string code;

        [OptionalField(VersionAdded = 2)]
        private string file;

        [OptionalField(VersionAdded = 2)]
        private string projectFile;

        [OptionalField(VersionAdded = 2)]
        private int lineNumber;

        [OptionalField(VersionAdded = 2)]
        private int columnNumber;

        [OptionalField(VersionAdded = 2)]
        private int endLineNumber;

        [OptionalField(VersionAdded = 2)]
        private int endColumnNumber;

        #region CustomSerializationToStream
        /// <summary>
        /// Serializes to a stream through a binary writer
        /// </summary>
        /// <param name="writer">Binary writer which is attached to the stream the event will be serialized into</param>
        internal override void WriteToStream(BinaryWriter writer)
        {
            base.WriteToStream(writer);

            writer.Write((Int32)importance);

            writer.WriteOptionalString(subcategory);
            writer.WriteOptionalString(code);
            writer.WriteOptionalString(file);
            writer.WriteOptionalString(projectFile);

            writer.Write((Int32)lineNumber);
            writer.Write((Int32)columnNumber);
            writer.Write((Int32)endLineNumber);
            writer.Write((Int32)endColumnNumber);
        }

        /// <summary>
        /// Deserializes from a stream through a binary reader
        /// </summary>
        /// <param name="reader">Binary reader which is attached to the stream the event will be deserialized from</param>
        /// <param name="version">The version of the runtime the message packet was created from</param>
        internal override void CreateFromStream(BinaryReader reader, int version)
        {
            base.CreateFromStream(reader, version);
            importance = (MessageImportance)reader.ReadInt32();

            //The data in the stream beyond this point are new to 4.0
            if (version > 20)
            {
                subcategory = reader.ReadByte() == 0 ? null : reader.ReadString();
                code = reader.ReadByte() == 0 ? null : reader.ReadString();
                file = reader.ReadByte() == 0 ? null : reader.ReadString();
                projectFile = reader.ReadByte() == 0 ? null : reader.ReadString();

                lineNumber = reader.ReadInt32();
                columnNumber = reader.ReadInt32();
                endLineNumber = reader.ReadInt32();
                endColumnNumber = reader.ReadInt32();
            }
        }
        #endregion

        /// <summary>
        /// Importance of the message
        /// </summary>
        public MessageImportance Importance
        {
            get => importance;
            internal set => importance = value;
        }

        /// <summary>
        /// The custom sub-type of the event.
        /// </summary>
        public string Subcategory => subcategory;

        /// <summary>
        /// Code associated with event. 
        /// </summary>
        public string Code => code;

        /// <summary>
        /// File associated with event.
        /// </summary>
        public string File => file;

        /// <summary>
        /// Line number of interest in associated file. 
        /// </summary>
        public int LineNumber
        {
            get => lineNumber;
            internal set => lineNumber = value;
        }

        /// <summary>
        /// Column number of interest in associated file. 
        /// </summary>
        public int ColumnNumber
        {
            get => columnNumber;
            internal set => columnNumber = value;
        }

        /// <summary>
        /// Ending line number of interest in associated file. 
        /// </summary>
        public int EndLineNumber => endLineNumber;

        /// <summary>
        /// Ending column number of interest in associated file. 
        /// </summary>
        public int EndColumnNumber => endColumnNumber;

        /// <summary>
        /// The project which was building when the message was issued.
        /// </summary>
        public string ProjectFile
        {
            get => projectFile;
            set => projectFile = value;
        }
    }
}
