// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using Microsoft.Build.Construction;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Evaluation
{
    /// <summary>
    /// Maintains a cache of all loaded ProjectRootElement instances for design time purposes.
    ///
    /// This avoids the LRU cache and class-wide lock used within ProjectRootElementCache and replaces these mechanisms
    /// with a single ConcurrentDictionary as a tradeoff for increased performance when evaluating projects in parallel.
    /// As a tradeoff, this implementation uses more memory, and is not intended for use when the cache needs to be
    /// long-lived e.g. within Visual Studio.
    ///
    /// SimpleProjectRootElementCache is not currently intended for use outside of evaluation. Several code paths
    /// executed within a full build take a hard dependency on the strong/weak reference behavior used within
    /// ProjectRootElementCache, and further investigation is required to determine the best way to hide these behind
    /// an abstraction. As such, any method unused by evaluation will throw NotImplementedException.
    /// </summary>
    internal class SimpleProjectRootElementCache : ProjectRootElementCacheBase
    {
        private readonly ConcurrentDictionary<string, ProjectRootElement> _cache;

        internal SimpleProjectRootElementCache()
        {
            _cache = new ConcurrentDictionary<string, ProjectRootElement>(StringComparer.OrdinalIgnoreCase);
            LoadProjectsReadOnly = true;
        }

        internal override ProjectRootElement Get(
            string projectFile,
            OpenProjectRootElement openProjectRootElement,
            bool isExplicitlyLoaded,
            bool? preserveFormatting)
        {
            // Should already have been canonicalized
            ErrorUtilities.VerifyThrowInternalRooted(projectFile);

            return openProjectRootElement == null
                ? GetFromCache(projectFile)
                : GetFromOrAddToCache(projectFile, openProjectRootElement);
        }

        private ProjectRootElement GetFromCache(string projectFile)
        {
            if (_cache.TryGetValue(projectFile, out ProjectRootElement projectRootElement))
            {
                return projectRootElement;
            }

            return null;
        }

        private ProjectRootElement GetFromOrAddToCache(string projectFile, OpenProjectRootElement openFunc)
        {
            return _cache.GetOrAdd(projectFile, key =>
            {
                ProjectRootElement rootElement = openFunc(key, this);
                ErrorUtilities.VerifyThrowInternalNull(rootElement, "projectRootElement");
                ErrorUtilities.VerifyThrow(rootElement.FullPath.Equals(key, StringComparison.OrdinalIgnoreCase),
                    "Got project back with incorrect path");

                AddEntry(rootElement);

                ErrorUtilities.VerifyThrow(_cache.TryGetValue(key, out _),
                    "Open should have renamed into cache and boosted");

                return rootElement;
            });
        }

        internal override void AddEntry(ProjectRootElement projectRootElement)
        {
            if (_cache.TryAdd(projectRootElement.FullPath, projectRootElement))
            {
                RaiseProjectRootElementAddedToCacheEvent(projectRootElement);
            }
        }

        internal override void RenameEntry(string oldFullPath, ProjectRootElement projectRootElement)
        {
            throw new NotImplementedException();
        }

        internal override ProjectRootElement TryGet(string projectFile)
        {
            return TryGet(projectFile, null);
        }

        internal override ProjectRootElement TryGet(string projectFile, bool? preserveFormatting)
        {
            return Get(
                projectFile,
                null,
                false,
                preserveFormatting);
        }

        internal override void DiscardStrongReferences()
        {
        }

        internal override void Clear()
        {
            _cache.Clear();
        }

        internal override void DiscardImplicitReferences()
        {
            throw new NotImplementedException();
        }

        internal override void DiscardAnyWeakReference(ProjectRootElement projectRootElement)
        {
            ErrorUtilities.VerifyThrowArgumentNull(projectRootElement, nameof(projectRootElement));

            // A PRE may be unnamed if it was only used in memory.
            if (projectRootElement.FullPath != null)
            {
                _cache.TryRemove(projectRootElement.FullPath, out _);
            }
        }

        internal override void OnProjectRootElementDirtied(ProjectRootElement sender, ProjectXmlChangedEventArgs e)
        {
            throw new NotImplementedException();
        }

        internal override void OnProjectDirtied(Project sender, ProjectChangedEventArgs e)
        {
            throw new NotImplementedException();
        }

        protected override void RaiseProjectRootElementRemovedFromStrongCache(ProjectRootElement projectRootElement)
        {
             throw new NotImplementedException();
        }
    }
}
