// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace Microsoft.Build.Framework
{
    /// <summary>
    /// Exposes build engine functionality that was made available in newer versions of MSBuild.
    /// </summary>
    /// <remarks>
    /// Make all members virtual but not abstract, ensuring that implementations can override them and external implementations
    /// won't break when the class is extended with new members. This base implementation should be throwing <see cref="NotImplementedException"/>.
    /// </remarks>
    [Serializable]
    public abstract class BuildEngineInterface
    {
        /// <summary>
        /// Initial version with LogsMessagesOfImportance() as the only exposed member.
        /// </summary>
        public const int Version1 = 1;

        /// <summary>
        /// An explicit version of this class. Must be incremented whenever new members are added. Derived classes should override
        /// the property to return the version actually being implemented.
        /// </summary>
        public virtual int Version => Version1;

        /// <summary>
        /// Returns <see langword="true"/> if the given message importance is not guaranteed to be ignored by registered loggers.
        /// </summary>
        /// <param name="importance">The importance to check.</param>
        /// <returns>True if messages of the given importance should be logged, false if it's guaranteed that such messages would be ignored.</returns>
        /// <remarks>
        /// Example: If we know that no logger is interested in <see cref="MessageImportance.Low"/>, this method returns <see langword="true"/>
        /// for <see cref="MessageImportance.Normal"/> and <see cref="MessageImportance.High"/>, and returns <see langword="false"/>
        /// for <see cref="MessageImportance.Low"/>.
        /// </remarks>
        public virtual bool LogsMessagesOfImportance(MessageImportance importance) => throw new NotImplementedException();
    }
}
