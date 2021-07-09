// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Build.Framework
{
    /// <summary>
    /// This interface extends <see cref="IBuildEngine9" /> to provide a reference to the <see cref="BuildEngineInterface" /> class.
    /// Future engine API should be added to the class as opposed to introducing yet another version of the IBuildEngine interface.
    /// </summary>
    public interface IBuildEngine10 : IBuildEngine9
    {
        /// <summary>
        /// Returns the new build engine interface.
        /// </summary>
        BuildEngineInterface EngineInterface { get; }
    }
}
