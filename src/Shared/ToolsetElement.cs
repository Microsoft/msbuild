// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using Microsoft.Build.Collections;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Evaluation
{
    /// <summary>
    /// Helper class for reading toolsets out of the configuration file.
    /// </summary>
    internal static class ToolsetConfigurationReaderHelpers
    {
        internal static ToolsetConfigurationSection ReadToolsetConfigurationSection(Configuration configuration)
        {
            ToolsetConfigurationSection configurationSection = null;

            // This will be null if the application config file does not have the following section
            // definition for the msbuildToolsets section as the first child element.
            //   <configSections>
            //     <section name=""msbuildToolsets"" type=""Microsoft.Build.Evaluation.ToolsetConfigurationSection, Microsoft.Build"" />
            //   </configSections>";
            // Note that the application config file may or may not contain an msbuildToolsets element.
            // For example:
            // If section definition is present and section is not present, this value is not null
            // If section definition is not present and section is also not present, this value is null
            // If the section definition is not present and section is present, then this value is null
            if (configuration != null)
            {
                ConfigurationSection msbuildSection = configuration.GetSection("msbuildToolsets");
                configurationSection = msbuildSection as ToolsetConfigurationSection;

                if (configurationSection == null && msbuildSection != null) // we found msbuildToolsets but the wrong type of handler
                {
                    if (String.IsNullOrEmpty(msbuildSection.SectionInformation.Type) ||
                        msbuildSection.SectionInformation.Type.IndexOf("Microsoft.Build", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // Set the configuration type handler to the current ToolsetConfigurationSection type
                        msbuildSection.SectionInformation.Type = typeof(ToolsetConfigurationSection).AssemblyQualifiedName;

                        try
                        {
                            // fabricate a temporary config file with the correct section handler type in it
                            string tempFileName = FileUtilities.GetTemporaryFile();

                            // Save the modified config
                            configuration.SaveAs(tempFileName + ".config");

                            // Open the configuration again, the new type for the section handler will do its stuff
                            // Note that the OpenExeConfiguration call uses the config filename *without* the .config
                            // extension
                            configuration = ConfigurationManager.OpenExeConfiguration(tempFileName);

                            // Get the toolset information from the section using our real handler
                            configurationSection = configuration.GetSection("msbuildToolsets") as ToolsetConfigurationSection;

                            File.Delete(tempFileName + ".config");
                            File.Delete(tempFileName);
                        }
                        catch (Exception ex) when (ExceptionHandling.IsIoRelatedException(ex))
                        {
                        }
                    }
                }
            }

            return configurationSection;
        }
    }

    /// <summary>
    /// Class representing the Toolset element
    /// </summary>
    /// <remarks>
    /// Internal for unit testing only
    /// </remarks>
    internal sealed class ToolsetElement : ConfigurationElement
    {
        /// <summary>
        /// ToolsVersion attribute of the element
        /// </summary>
        [ConfigurationProperty("toolsVersion", IsKey = true, IsRequired = true)]
        public string toolsVersion
        {
            get
            {
                return (string)base["toolsVersion"];
            }

            set
            {
                base[nameof(toolsVersion)] = value;
            }
        }

        /// <summary>
        /// Property element collection
        /// </summary>
        [ConfigurationProperty("", IsDefaultCollection = true)]
        public PropertyElementCollection PropertyElements
        {
            get
            {
                return (PropertyElementCollection)base[""];
            }
        }

        /// <summary>
        /// Collection of all the search paths for project imports, per OS
        /// </summary>
        [ConfigurationProperty("projectImportSearchPaths")]
        public ExtensionsPathsElementCollection AllProjectImportSearchPaths
        {
            get
            {
                return (ExtensionsPathsElementCollection)base["projectImportSearchPaths"];
            }
        }

        /// <summary>
        /// Class representing all the per-OS search paths for MSBuildExtensionsPath*
        /// </summary>
        internal sealed class ExtensionsPathsElementCollection : ConfigurationElementCollection
        {
            /// <summary>
            /// We use this dictionary to track whether or not we've seen a given
            /// searchPaths definition before, since the .NET configuration classes
            /// won't perform this check without respect for case.
            /// </summary>
            private Dictionary<string, string> _previouslySeenOS = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Type of the collection
            /// This has to be public as cannot change access modifier when overriding
            /// </summary>
            public override ConfigurationElementCollectionType CollectionType
            {
                get
                {
                    return ConfigurationElementCollectionType.BasicMap;
                }
            }

            /// <summary>
            /// Throw exception if an element with a duplicate key is added to the collection
            /// </summary>
            protected override bool ThrowOnDuplicate
            {
                get
                {
                    return false;
                }
            }

            /// <summary>
            /// Name of the element
            /// </summary>
            protected override string ElementName
            {
                get
                {
                    return "searchPaths";
                }
            }

            /// <summary>
            /// Gets an element with the specified name
            /// </summary>
            /// <param name="os">OS of the element</param>
            /// <returns>element</returns>
            public ExtensionsPathElement GetElement(string os)
            {
                return (ExtensionsPathElement)this.BaseGet(os);
            }

            /// <summary>
            /// Gets an element based at the specified position
            /// </summary>
            /// <param name="index">position</param>
            /// <returns>element</returns>
            public ExtensionsPathElement GetElement(int index)
            {
                return (ExtensionsPathElement)this.BaseGet(index);
            }

            /// <summary>
            /// Returns the key value for the given element
            /// </summary>
            /// <param name="element">element whose key is returned</param>
            /// <returns>key</returns>
            protected override object GetElementKey(ConfigurationElement element)
            {
                return ((ExtensionsPathElement)element).OS;
            }

            /// <summary>
            /// Creates a new element of the collection
            /// </summary>
            /// <returns>Created element</returns>
            protected override ConfigurationElement CreateNewElement()
            {
                return new ExtensionsPathElement();
            }

            /// <summary>
            /// overridden so we can track previously seen elements
            /// </summary>
            protected override void BaseAdd(int index, ConfigurationElement element)
            {
                UpdateOSMap(element);

                base.BaseAdd(index, element);
            }

            /// <summary>
            /// overridden so we can track previously seen elements
            /// </summary>
            protected override void BaseAdd(ConfigurationElement element)
            {
                UpdateOSMap(element);

                base.BaseAdd(element);
            }

            /// <summary>
            /// Stores the name of the OS in a case-insensitive map
            /// so we can detect if it is specified more than once but with
            /// different case
            /// </summary>
            private void UpdateOSMap(ConfigurationElement element)
            {
                string os = GetElementKey(element).ToString();

                if (_previouslySeenOS.ContainsKey(os))
                {
                    string locationString = String.Empty;
                    if (!String.IsNullOrEmpty(element.ElementInformation.Source))
                    {
                        if (element.ElementInformation.LineNumber != 0)
                        {
                            locationString = String.Format("{0} ({1})", element.ElementInformation.Source, element.ElementInformation.LineNumber);
                        }
                        else
                        {
                            locationString = element.ElementInformation.Source;
                        }
                    }

                    string message = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("MultipleDefinitionsForSameExtensionsPathOS", os, locationString);

                    throw new ConfigurationErrorsException(message, element.ElementInformation.Source, element.ElementInformation.LineNumber);
                }

                _previouslySeenOS.Add(os, string.Empty);
            }
        }

        /// <summary>
        /// Class representing searchPaths element for a single OS
        /// </summary>
        internal sealed class ExtensionsPathElement : ConfigurationElement
        {
            /// <summary>
            /// OS attribute of the element
            /// </summary>
            [ConfigurationProperty("os", IsKey = true, IsRequired = true)]
            public string OS
            {
                get
                {
                    return (string)base["os"];
                }

                set
                {
                    base["os"] = value;
                }
            }

            /// <summary>
            /// Property element collection
            /// </summary>
            [ConfigurationProperty("", IsDefaultCollection = true)]
            public PropertyElementCollection PropertyElements
            {
                get
                {
                    return (PropertyElementCollection)base[""];
                }
            }
        }

        /// <summary>
        /// Class representing collection of property elements
        /// </summary>
        internal sealed class PropertyElementCollection : ConfigurationElementCollection
        {
            #region Private Fields

            /// <summary>
            /// We use this dictionary to track whether or not we've seen a given
            /// property definition before, since the .NET configuration classes
            /// won't perform this check without respect for case.
            /// </summary>
            private Dictionary<string, string> _previouslySeenPropertyNames = new Dictionary<string, string>(MSBuildNameIgnoreCaseComparer.Default);

            #endregion

            #region Properties

            /// <summary>
            /// Collection type
            /// This has to be public as cannot change access modifier when overriding
            /// </summary>
            public override ConfigurationElementCollectionType CollectionType
            {
                get
                {
                    return ConfigurationElementCollectionType.BasicMap;
                }
            }

            /// <summary>
            /// Throw exception if an element with a duplicate is added
            /// </summary>
            protected override bool ThrowOnDuplicate
            {
                get
                {
                    return false;
                }
            }

            /// <summary>
            /// name of the element
            /// </summary>
            protected override string ElementName
            {
                get
                {
                    return "property";
                }
            }

            #endregion

            #region Methods

            /// <summary>
            /// Gets an element with the specified name
            /// </summary>
            /// <param name="name">name of the element</param>
            /// <returns>element</returns>
            public PropertyElement GetElement(string name)
            {
                return (PropertyElement)this.BaseGet(name);
            }

            /// <summary>
            /// Gets an element at the specified position
            /// </summary>
            /// <param name="index">position</param>
            /// <returns>element</returns>
            public PropertyElement GetElement(int index)
            {
                return (PropertyElement)this.BaseGet(index);
            }

            /// <summary>
            /// Creates a new element
            /// </summary>
            /// <returns>element</returns>
            protected override ConfigurationElement CreateNewElement()
            {
                return new PropertyElement();
            }

            /// <summary>
            /// overridden so we can track previously seen property names
            /// </summary>
            protected override void BaseAdd(int index, ConfigurationElement element)
            {
                UpdatePropertyNameMap(element);

                base.BaseAdd(index, element);
            }

            /// <summary>
            /// overridden so we can track previously seen property names
            /// </summary>
            protected override void BaseAdd(ConfigurationElement element)
            {
                UpdatePropertyNameMap(element);

                base.BaseAdd(element);
            }

            /// <summary>
            /// Gets the key for the element
            /// </summary>
            /// <param name="element">element</param>
            /// <returns>key</returns>
            protected override object GetElementKey(ConfigurationElement element)
            {
                return ((PropertyElement)element).Name;
            }

            /// <summary>
            /// Stores the name of the tools version in a case-insensitive map
            /// so we can detect if it is specified more than once but with
            /// different case
            /// </summary>
            private void UpdatePropertyNameMap(ConfigurationElement element)
            {
                string propertyName = GetElementKey(element).ToString();

                if (_previouslySeenPropertyNames.ContainsKey(propertyName))
                {
                    string message = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("MultipleDefinitionsForSameProperty", propertyName);

                    throw new ConfigurationErrorsException(message, element.ElementInformation.Source, element.ElementInformation.LineNumber);
                }

                _previouslySeenPropertyNames.Add(propertyName, string.Empty);
            }

            #endregion
        }

        /// <summary>
        /// This class represents property element
        /// </summary>
        internal sealed class PropertyElement : ConfigurationElement
        {
            /// <summary>
            /// name attribute
            /// </summary>
            [ConfigurationProperty("name", IsKey = true, IsRequired = true)]
            public string Name
            {
                get
                {
                    return (string)base["name"];
                }

                set
                {
                    base["name"] = value;
                }
            }

            /// <summary>
            /// value attribute
            /// </summary>
            [ConfigurationProperty("value", IsRequired = true)]
            public string Value
            {
                get
                {
                    return (string)base["value"];
                }

                set
                {
                    base["value"] = value;
                }
            }
        }
    }

    /// <summary>
    /// Class representing the collection of toolset elements
    /// </summary>
    /// <remarks>
    /// Internal for unit testing only
    /// </remarks>
    internal sealed class ToolsetElementCollection : ConfigurationElementCollection
    {
        /// <summary>
        /// We use this dictionary to track whether or not we've seen a given
        /// toolset definition before, since the .NET configuration classes
        /// won't perform this check without respect for case.
        /// </summary>
        private Dictionary<string, string> _previouslySeenToolsVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Type of the collection
        /// This has to be public as cannot change access modifier when overriding
        /// </summary>
        public override ConfigurationElementCollectionType CollectionType
        {
            get
            {
                return ConfigurationElementCollectionType.BasicMap;
            }
        }

        /// <summary>
        /// Throw exception if an element with a duplicate key is added to the collection
        /// </summary>
        protected override bool ThrowOnDuplicate
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Name of the element
        /// </summary>
        protected override string ElementName
        {
            get
            {
                return "toolset";
            }
        }

        /// <summary>
        /// Gets an element with the specified name
        /// </summary>
        /// <param name="toolsVersion">toolsVersion of the element</param>
        /// <returns>element</returns>
        public ToolsetElement GetElement(string toolsVersion)
        {
            return (ToolsetElement)this.BaseGet(toolsVersion);
        }

        /// <summary>
        /// Gets an element based at the specified position
        /// </summary>
        /// <param name="index">position</param>
        /// <returns>element</returns>
        public ToolsetElement GetElement(int index)
        {
            return (ToolsetElement)this.BaseGet(index);
        }

        /// <summary>
        /// Returns the key value for the given element
        /// </summary>
        /// <param name="element">element whose key is returned</param>
        /// <returns>key</returns>
        protected override object GetElementKey(ConfigurationElement element)
        {
            return ((ToolsetElement)element).toolsVersion;
        }

        /// <summary>
        /// Creates a new element of the collection
        /// </summary>
        /// <returns>Created element</returns>
        protected override ConfigurationElement CreateNewElement()
        {
            return new ToolsetElement();
        }

        /// <summary>
        /// overridden so we can track previously seen tools versions
        /// </summary>
        protected override void BaseAdd(int index, ConfigurationElement element)
        {
            UpdateToolsVersionMap(element);

            base.BaseAdd(index, element);
        }

        /// <summary>
        /// overridden so we can track previously seen tools versions
        /// </summary>
        protected override void BaseAdd(ConfigurationElement element)
        {
            UpdateToolsVersionMap(element);

            base.BaseAdd(element);
        }

        /// <summary>
        /// Stores the name of the tools version in a case-insensitive map
        /// so we can detect if it is specified more than once but with
        /// different case
        /// </summary>
        private void UpdateToolsVersionMap(ConfigurationElement element)
        {
            string toolsVersion = GetElementKey(element).ToString();

            if (_previouslySeenToolsVersions.ContainsKey(toolsVersion))
            {
                string message = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("MultipleDefinitionsForSameToolset", toolsVersion);

                throw new ConfigurationErrorsException(message, element.ElementInformation.Source, element.ElementInformation.LineNumber);
            }

            _previouslySeenToolsVersions.Add(toolsVersion, string.Empty);
        }
    }

    /// <summary>
    /// This class is used to programmatically read msbuildToolsets section
    /// in from the configuration file.  An example of application config file:
    ///
    /// &lt;configuration&gt;
    ///     &lt;msbuildToolsets default="2.0"&gt;
    ///         &lt;toolset toolsVersion="2.0"&gt;
    ///             &lt;property name="MSBuildBinPath" value="D:\windows\Microsoft.NET\Framework\v2.0.x86ret\"/&gt;
    ///             &lt;property name="SomeOtherProperty" value="SomeOtherPropertyValue"/&gt;
    ///         &lt;/toolset&gt;
    ///         &lt;toolset toolsVersion="3.5"&gt;
    ///             &lt;property name="MSBuildBinPath" value="D:\windows\Microsoft.NET\Framework\v3.5.x86ret\"/&gt;
    ///         &lt;/toolset&gt;
    ///     &lt;/msbuildToolsets&gt;
    /// &lt;/configuration&gt;
    ///
    /// </summary>
    /// <remarks>
    /// Internal for unit testing only
    /// </remarks>
    internal sealed class ToolsetConfigurationSection : ConfigurationSection
    {
        /// <summary>
        /// toolsVersion element collection
        /// </summary>
        [ConfigurationProperty("", IsDefaultCollection = true)]
        public ToolsetElementCollection Toolsets
        {
            get
            {
                return (ToolsetElementCollection)base[""];
            }
        }

        /// <summary>
        /// default attribute on msbuildToolsets element, specifying the default ToolsVersion
        /// </summary>
        [ConfigurationProperty("default")]
        public string Default
        {
            get
            {
                // The ConfigurationPropertyAttribute constructor accepts a named parameter "DefaultValue"
                // that doesn't seem to work if null is the desired default value.  So here we return null
                // whenever the base class gives us an empty string.
                // Note this means we can't distinguish between the attribute being present but containing
                // an empty string for its value and the attribute not being present at all.
                string defaultValue = (string)base["default"];
                return String.IsNullOrEmpty(defaultValue) ? null : defaultValue;
            }

            set
            {
                base["default"] = value;
            }
        }

        /// <summary>
        /// MsBuildOverrideTasksPath attribute on msbuildToolsets element, specifying the path to find msbuildOverrideTasks files
        /// </summary>
        [ConfigurationProperty("msbuildOverrideTasksPath")] // This string is case sensitive, can't change it
        public string MSBuildOverrideTasksPath
        {
            get
            {
                // The ConfigurationPropertyAttribute constructor accepts a named parameter "DefaultValue"
                // that doesn't seem to work if null is the desired default value.  So here we return null
                // whenever the base class gives us an empty string.
                // Note this means we can't distinguish between the attribute being present but containing
                // an empty string for its value and the attribute not being present at all.
                string defaultValue = (string)base["msbuildOverrideTasksPath"];
                return String.IsNullOrEmpty(defaultValue) ? null : defaultValue;
            }

            set
            {
                base["msbuildOverrideTasksPath"] = value;
            }
        }

        /// <summary>
        /// DefaultOverrideToolsVersion attribute on msbuildToolsets element, specifying the toolsversion that should be used by
        /// default to build projects with this version of MSBuild.
        /// </summary>
        [ConfigurationProperty("DefaultOverrideToolsVersion")]
        public string DefaultOverrideToolsVersion
        {
            get
            {
                // The ConfigurationPropertyAttribute constructor accepts a named parameter "DefaultValue"
                // that doesn't seem to work if null is the desired default value.  So here we return null
                // whenever the base class gives us an empty string.
                // Note this means we can't distinguish between the attribute being present but containing
                // an empty string for its value and the attribute not being present at all.
                string defaultValue = (string)base["DefaultOverrideToolsVersion"];
                return String.IsNullOrEmpty(defaultValue) ? null : defaultValue;
            }

            set
            {
                base[nameof(DefaultOverrideToolsVersion)] = value;
            }
        }
    }
}
