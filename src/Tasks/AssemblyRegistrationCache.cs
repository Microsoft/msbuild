// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Tasks
{
    /// <remarks>
    /// This class is a caching mechanism for the Register/UnregisterAssembly task to keep track of registered assemblies to clean up
    /// </remarks>
    /// Serializable should be included in all state files. It permits BinaryFormatter-based calls, including from GenerateResource, which we cannot move off BinaryFormatter.
    [Serializable]
    internal sealed class AssemblyRegistrationCache : StateFileBase, ITranslatable
    {
        /// <summary>
        /// The list of registered assembly files.
        /// </summary>
        internal List<string> _assemblies = new List<string>();

        /// <summary>
        /// The list of registered type library files.
        /// </summary>
        internal List<string> _typeLibraries = new List<string>();

        /// <summary>
        /// The number of entries in the state file
        /// </summary>
        internal int Count
        {
            get
            {
                ErrorUtilities.VerifyThrow(_assemblies.Count == _typeLibraries.Count, "Internal assembly and type library lists should have the same number of entries in AssemblyRegistrationCache");
                return _assemblies.Count;
            }
        }

        /// <summary>
        /// Sets the entry with the specified index
        /// </summary>
        internal void AddEntry(string assemblyPath, string typeLibraryPath)
        {
            _assemblies.Add(assemblyPath);
            _typeLibraries.Add(typeLibraryPath);
        }

        /// <summary>
        /// Gets the entry with the specified index
        /// </summary>
        internal void GetEntry(int index, out string assemblyPath, out string typeLibraryPath)
        {
            ErrorUtilities.VerifyThrow((index >= 0) && (index < _assemblies.Count), "Invalid index in the call to AssemblyRegistrationCache.GetEntry");
            assemblyPath = _assemblies[index];
            typeLibraryPath = _typeLibraries[index];
        }

        public AssemblyRegistrationCache(ITranslator translator)
        {
            Translate(translator);
        }

        public AssemblyRegistrationCache() { }

        public override void Translate(ITranslator translator)
        {
            ErrorUtilities.VerifyThrowArgumentNull(translator, nameof(translator));
            translator.Translate(ref _assemblies);
            translator.Translate(ref _typeLibraries);
        }
    }
}
