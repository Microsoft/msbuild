// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Evaluation
{
    /// <summary>
    /// Class representing a reference to a project import path with property fall-back
    /// </summary>
    internal class ProjectImportPathMatch : ITranslatable
    {
        /// <summary>
        /// ProjectImportPathMatch instance representing no fall-back
        /// </summary>
        public static readonly ProjectImportPathMatch None = new ProjectImportPathMatch(string.Empty, new List<string>());

        internal ProjectImportPathMatch(string propertyName, List<string> searchPaths)
        {
            ErrorUtilities.VerifyThrowArgumentNull(propertyName, nameof(propertyName));
            ErrorUtilities.VerifyThrowArgumentNull(searchPaths, nameof(searchPaths));

            PropertyName = propertyName;
            SearchPaths = searchPaths;
            MsBuildPropertyFormat = $"$({PropertyName})";
        }

        public ProjectImportPathMatch(ITranslator translator)
        {
            ((ITranslatable)this).Translate(translator);
        }

        /// <summary>
        /// String representation of the property reference - eg. "MSBuildExtensionsPath32"
        /// </summary>
        public string PropertyName;

        /// <summary>
        /// Returns the corresponding property name - eg. "$(MSBuildExtensionsPath32)"
        /// </summary>
        public string MsBuildPropertyFormat;

        /// <summary>
        /// Enumeration of the search paths for the property.
        /// </summary>
        public List<string> SearchPaths;

        public void Translate(ITranslator translator)
        {
            translator.Translate(ref PropertyName);
            translator.Translate(ref MsBuildPropertyFormat);
            translator.Translate(ref SearchPaths);
        }

        /// <summary>
        /// Factory for serialization.
        /// </summary>
        internal static ProjectImportPathMatch FactoryForDeserialization(ITranslator translator)
        {
            return new ProjectImportPathMatch(translator);
        }
    }
}
