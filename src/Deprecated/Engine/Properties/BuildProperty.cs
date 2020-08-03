// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Xml;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;

using Microsoft.Build.BuildEngine.Shared;
using System.Collections.Generic;

namespace Microsoft.Build.BuildEngine
{
    /// <summary>
    /// This is an enumeration of property types.  Each one is explained further
    /// below.
    /// </summary>
    /// <owner>rgoel</owner>
    internal enum PropertyType
    {
        // A "normal" property is the kind that is settable by the project
        // author from within the project file.  They are arbitrarily named
        // by the author.
        NormalProperty,

        // An "imported" property is like a "normal" property, except that 
        // instead of coming directly from the project file, its definition
        // is in one of the imported files (e.g., "CSharp.buildrules").
        ImportedProperty,

        // A "global" property is the kind that is set outside of the project file.
        // Once such a property is set, it cannot be overridden by the project file.
        // For example, when the user sets a property via a switch on the XMake 
        // command-line, this is a global property.  In the IDE case, "Configuration"
        // would be a global property set by the IDE.
        GlobalProperty,

        // A "reserved" property behaves much like a read-only property, except 
        // that the names are not arbitrary; they are chosen by us.  Also,
        // no user can ever set or override these properties.  For example,
        // "XMakeProjectName" would be a property that is only settable by
        // XMake code.  Any attempt to set this property via the project file
        // or any other mechanism should result in an error.
        ReservedProperty,

        // An "environment" property is one that came from an environment variable.
        EnvironmentProperty,

        // An "output" property is generated by a task. Properties of this type
        // override all properties except "reserved" ones.
        OutputProperty
    }

    /// <summary>
    /// This class holds an MSBuild property.  This may be a property that is
    /// represented in the MSBuild project file by an XML element, or it
    /// may not be represented in any real XML file (e.g., global properties,
    /// environment properties, etc.)
    /// </summary>
    /// <owner>rgoel</owner>
    [DebuggerDisplay("BuildProperty (Name = { Name }, Value = { Value }, FinalValue = { FinalValue }, Condition = { Condition })")]
    public class BuildProperty
    {
        #region Member Data
        // This is an alternative location for property data: if propertyElement
        // is null, which means the property is not persisted, we should not store
        // the name/value pair in an XML attribute, because the property name
        // may contain illegal XML characters.
        private string propertyName = null;

        // We'll still store the value no matter what, because fetching the inner
        // XML can be an expensive operation.
        private string propertyValue = null;

        // This is the final evaluated value for the property.
        private string finalValueEscaped = String.Empty;

        // This the type of the property from the PropertyType enumeration defined
        // above.
        private PropertyType type = PropertyType.NormalProperty;

        // This is the XML element for this property.  This contains the name and
        // value for this property as well as the condition.  For example,
        // this node may look like this:
        //      <WarningLevel Condition="...">4</WarningLevel>
        //
        // If this property is not represented by an actual XML element in the 
        // project file, it's okay if this is null.
        private XmlElement propertyElement = null;

        // This is the specific XML attribute in the above XML element which 
        // contains the "Condition".
        private XmlAttribute conditionAttribute = null;

        // If this property is persisted in the project file, then we need to 
        // store a reference to the parent <PropertyGroup>.
        private BuildPropertyGroup parentPersistedPropertyGroup = null;

        // Dictionary to intern the value and finalEscapedValue strings as they are deserialized
        private static Dictionary<string, string> customInternTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        #endregion

        #region CustomSerializationToStream
        internal void WriteToStream(BinaryWriter writer)
        {
            // Cannot be null
            writer.Write(propertyName);
            writer.Write(propertyValue);

            // Only bother serializing the finalValueEscaped
            // if it's not identical to the Value (it often is)
            if (propertyValue == finalValueEscaped)
            {
                writer.Write((byte)1);
            }
            else
            {
                writer.Write((byte)0);
                  writer.Write(finalValueEscaped);
            }
            writer.Write((Int32)type);
        }

        /// <summary>
        /// Avoid creating duplicate strings when deserializing. We are using a custom intern table
        /// because String.Intern keeps a reference to the string until the appdomain is unloaded.
        /// </summary>
        private static string Intern(string stringToIntern)
        {
            string value = stringToIntern;
            if (!customInternTable.TryGetValue(stringToIntern, out value))
            {
                customInternTable.Add(stringToIntern, stringToIntern);
                value = stringToIntern;
            }

            return value;
        }

        internal static BuildProperty CreateFromStream(BinaryReader reader)
        {
            string name = reader.ReadString();
            string value = Intern(reader.ReadString());

            byte marker = reader.ReadByte();
            string finalValueEscaped;

            if (marker == (byte)1)
            {
                finalValueEscaped = value;
            }
            else
            {
                finalValueEscaped = Intern(reader.ReadString());
            }

            PropertyType type = (PropertyType)reader.ReadInt32();

            BuildProperty property = new BuildProperty(name, value, type);
            property.finalValueEscaped = finalValueEscaped;
            return property;
        }

        /// <summary>
        /// Clear the static intern table, so that the memory can be released
        /// when a build is released and the node is waiting for re-use.
        /// </summary>
        internal static void ClearInternTable()
        {
            customInternTable.Clear();
        }
        #endregion

        #region Constructors

        /// <summary>
        /// Constructor, that initializes the property with an existing XML element.
        /// </summary>
        /// <param name="propertyElement"></param>
        /// <param name="propertyType"></param>
        /// <owner>rgoel</owner>
        internal BuildProperty
        (
            XmlElement      propertyElement,
            PropertyType    propertyType
        ) :
            this(propertyElement,
                 propertyElement != null ? Utilities.GetXmlNodeInnerContents(propertyElement) : null,
                 propertyType)
        {
            ProjectErrorUtilities.VerifyThrowInvalidProject(XMakeElements.IllegalItemPropertyNames[this.Name] == null,
                propertyElement, "CannotModifyReservedProperty", this.Name);
        }

        /// <summary>
        /// Constructor, that initializes the property with cloned information.
        ///
        /// Callers -- Please ensure that the propertyValue passed into this constructor
        /// is actually computed by calling GetXmlNodeInnerContents on the propertyElement.
        /// </summary>
        /// <param name="propertyElement"></param>
        /// <param name="propertyValue"></param>
        /// <param name="propertyType"></param>
        /// <owner>rgoel</owner>
        private BuildProperty
        (
            XmlElement propertyElement,
            string propertyValue,
            PropertyType propertyType
        )
        {
            // Make sure the property node has been given to us.
            ErrorUtilities.VerifyThrow(propertyElement != null,
                "Need an XML node representing the property element.");

            // Validate that the property name doesn't contain any illegal characters.
            XmlUtilities.VerifyThrowProjectValidElementName(propertyElement);

            this.propertyElement = propertyElement;

            // Loop through the list of attributes on the property element.
            foreach (XmlAttribute propertyAttribute in propertyElement.Attributes)
            {
                switch (propertyAttribute.Name)
                {
                    case XMakeAttributes.condition:
                        // We found the "condition" attribute.  Process it.
                        this.conditionAttribute = propertyAttribute;
                        break;

                    default:
                        ProjectXmlUtilities.ThrowProjectInvalidAttribute(propertyAttribute);
                        break;
                }
            }

            this.propertyValue = propertyValue;
            this.finalValueEscaped = propertyValue;
            this.type = propertyType;
        }

        /// <summary>
        /// Constructor, that initializes the property from the raw data, like
        /// the property name and property value.  This constructor actually creates
        /// a new XML element to represent the property, and so it needs the owner
        /// XML document.
        /// </summary>
        /// <param name="ownerDocument"></param>
        /// <param name="propertyName"></param>
        /// <param name="propertyValue"></param>
        /// <param name="propertyType"></param>
        /// <owner>rgoel</owner>
        internal BuildProperty
        (
            XmlDocument ownerDocument,
            string propertyName,
            string propertyValue,
            PropertyType propertyType
        )
        {
            ErrorUtilities.VerifyThrowArgumentLength(propertyName, "propertyName");
            ErrorUtilities.VerifyThrowArgumentNull(propertyValue, "propertyValue");

            // Validate that the property name doesn't contain any illegal characters.
            XmlUtilities.VerifyThrowValidElementName(propertyName);

            // If we've been given an owner XML document, create a new property
            // XML element in that document.
            if (ownerDocument != null)
            {
                // Create the new property XML element.
                this.propertyElement = ownerDocument.CreateElement(propertyName, XMakeAttributes.defaultXmlNamespace);

                // Set the value
                Utilities.SetXmlNodeInnerContents(this.propertyElement, propertyValue);

                // Get the value back.  Because of some XML weirdness (particularly whitespace between XML attribute),
                // what you set may not be exactly what you get back.  That's why we ask XML to give us the value
                // back, rather than assuming it's the same as the string we set.
                this.propertyValue = Utilities.GetXmlNodeInnerContents(this.propertyElement);
            }
            else
            {
                // Otherwise this property is not going to be persisted, so we don't
                // need an XML element.
                this.propertyName = propertyName;
                this.propertyValue = propertyValue;

                this.propertyElement = null;
            }

            ErrorUtilities.VerifyThrowInvalidOperation(XMakeElements.IllegalItemPropertyNames[this.Name] == null,
                "CannotModifyReservedProperty", this.Name);

            // Initialize the final evaluated value of this property to just the
            // normal unevaluated value.  We actually can't evaluate it in isolation ...
            // we need the context of all the properties in the project file.
            this.finalValueEscaped = propertyValue;

            // We default to a null condition.  Setting a condition must be done
            // through the "Condition" property after construction.
            this.conditionAttribute = null;

            // Assign the property type.
            this.type = propertyType;
        }

        /// <summary>
        /// Constructor, that initializes the property from the raw data, like
        /// the property name and property value.  This constructor actually creates
        /// a new XML element to represent the property, and creates this XML element
        /// under some dummy XML document.  This would be used if the property didn't
        /// need to be persisted in an actual XML file at any point, like a "global"
        /// property or an "environment" property".
        /// </summary>
        /// <param name="propertyName"></param>
        /// <param name="propertyValue"></param>
        /// <param name="propertyType"></param>
        /// <owner>rgoel</owner>
        internal BuildProperty
        (
            string propertyName,
            string propertyValue,
            PropertyType propertyType
        ) :
            this(null, propertyName, propertyValue, propertyType)
        {
        }

        /// <summary>
        /// Constructor, which initializes the property from just the property
        /// name and value, creating it as a "normal" property.  This ends up
        /// creating a new XML element for the property under a dummy XML document.
        /// </summary>
        /// <param name="propertyName"></param>
        /// <param name="propertyValue"></param>
        /// <owner>rgoel</owner>
        public BuildProperty
        (
            string propertyName,
            string propertyValue
        ) :
            this(propertyName, propertyValue, PropertyType.NormalProperty)
        {
        }

        /// <summary>
        /// Default constructor.  This is not allowed because it leaves the
        /// property in a bad state -- without a name or value.  But we have to
        /// have it, otherwise FXCop complains.
        /// </summary>
        /// <owner>sumedhk</owner>
        private BuildProperty
            (
            )
        {
            // not allowed.
        }

        #endregion

        #region Properties

        /// <summary>
        /// Accessor for the property name.  This is read-only, so one cannot
        /// change the property name once it's set ... your only option is
        /// to create a new BuildProperty object.  The reason is that BuildProperty objects
        /// are often stored in hash tables where the hash function is based
        /// on the property name.  Modifying the property name of an existing
        /// BuildProperty object would make the hash table incorrect.
        /// </summary>
        /// <owner>RGoel</owner>
        public string Name
        {
            get
            {
                if (propertyElement != null)
                {
                    // Get the property name directly off the XML element.
                    return this.propertyElement.Name;
                }
                else
                {
                    // If we are not persisted, propertyName and propertyValue must not be null.
                    ErrorUtilities.VerifyThrow((this.propertyName != null) && (this.propertyName.Length > 0) && (this.propertyValue != null),
                        "BuildProperty object doesn't have a name/value pair.");

                    // Get the property name from the string variable
                    return this.propertyName;
                }
            }
        }

        /// <summary>
        /// Accessor for the property value.  Normal properties can be modified;
        /// other property types cannot.
        /// </summary>
        /// <owner>RGoel</owner>
        public string Value
        {
            get
            {
                // If we are not persisted, propertyName and propertyValue must not be null.
                ErrorUtilities.VerifyThrow(this.propertyValue != null,
                    "BuildProperty object doesn't have a name/value pair.");

                return this.propertyValue;
            }

            set
            {
                ErrorUtilities.VerifyThrowInvalidOperation(this.type != PropertyType.ImportedProperty,
                    "CannotModifyImportedProjects", this.Name);

                ErrorUtilities.VerifyThrowInvalidOperation(this.type != PropertyType.EnvironmentProperty,
                    "CannotModifyEnvironmentProperty", this.Name);

                ErrorUtilities.VerifyThrowInvalidOperation(this.type != PropertyType.ReservedProperty,
                    "CannotModifyReservedProperty", this.Name);

                ErrorUtilities.VerifyThrowInvalidOperation(this.type != PropertyType.GlobalProperty,
                    "CannotModifyGlobalProperty", this.Name, "Project.GlobalProperties");

                SetValue(value);
            }
        }

        /// <summary>
        /// Helper method to set the value of a BuildProperty.
        /// </summary>
        /// <owner>DavidLe</owner>
        /// <param name="value"></param>
        /// <returns>nothing</returns>
        internal void SetValue(string value)
        {
            ErrorUtilities.VerifyThrowArgumentNull(value, "Value");

            // NO OP if the value we set is the same we already have
            // This will prevent making the project dirty
            if (value == this.propertyValue)
                return;

            // NOTE: allow output properties to be modified -- they're just like normal properties (except for their
            // precedence), and it doesn't really matter if they are modified, since they are transient (virtual)

            if (this.propertyElement != null)
            {
                // If our XML element is not null, store the value in it.
                Utilities.SetXmlNodeInnerContents(this.propertyElement, value);

                // Get the value back.  Because of some XML weirdness (particularly whitespace between XML attribute),
                // what you set may not be exactly what you get back.  That's why we ask XML to give us the value
                // back, rather than assuming it's the same as the string we set.
                this.propertyValue = Utilities.GetXmlNodeInnerContents(this.propertyElement);
            }
            else
            {
                // Otherwise, store the value in the string variable.
                this.propertyValue = value;
            }

            this.finalValueEscaped = value;
            MarkPropertyAsDirty();
        }

        /// <summary>
        /// Accessor for the final evaluated property value.  This is read-only.
        /// To modify the raw value of a property, use BuildProperty.Value.
        /// </summary>
        /// <owner>RGoel</owner>
        internal string FinalValueEscaped
        {
            get
            {
                return this.finalValueEscaped;
            }
        }

        /// <summary>
        /// Returns the unescaped value of the property.
        /// </summary>
        /// <owner>RGoel</owner>
        public string FinalValue
        {
            get
            {
                return EscapingUtilities.UnescapeAll(this.FinalValueEscaped);
            }
        }

        /// <summary>
        /// Accessor for the property type.  This is internal, so that nobody
        /// calling the OM can modify the type.  We actually need to modify
        /// it in certain cases internally.  C# doesn't allow a different
        /// access mode for the "get" vs. the "set", so we've made them both
        /// internal.
        /// </summary>
        /// <owner>RGoel</owner>
        internal PropertyType Type
        {
            get
            {
                return this.type;
            }

            set
            {
                this.type = value;
            }
        }

        /// <summary>
        /// Did this property originate from an imported project file?
        /// </summary>
        /// <owner>RGoel</owner>
        public bool IsImported
        {
            get
            {
                return (this.type == PropertyType.ImportedProperty);
            }
        }

        /// <summary>
        /// Accessor for the condition on the property.
        /// </summary>
        /// <owner>RGoel</owner>
        public string Condition
        {
            get
            {
                return (this.conditionAttribute == null) ? String.Empty : this.conditionAttribute.Value;
            }

            set
            {
                // If this BuildProperty object is not actually represented by an
                // XML element in the project file, then do not allow
                // the caller to set the condition.
                ErrorUtilities.VerifyThrowInvalidOperation(this.propertyElement != null,
                    "CannotSetCondition");

                // If this property was imported from another project, we don't allow modifying it.
                ErrorUtilities.VerifyThrowInvalidOperation(this.Type != PropertyType.ImportedProperty,
                    "CannotModifyImportedProjects");

                this.conditionAttribute = ProjectXmlUtilities.SetOrRemoveAttribute(propertyElement, XMakeAttributes.condition, value);

                MarkPropertyAsDirty();
            }
        }

        /// <summary>
        /// Read-only accessor for accessing the XML attribute for "Condition".  Callers should
        /// never try and modify this.  Go through this.Condition to change the condition.
        /// </summary>
        /// <owner>RGoel</owner>
        internal XmlAttribute ConditionAttribute
        {
            get
            {
                return this.conditionAttribute;
            }
        }

        /// <summary>
        /// Accessor for the XmlElement representing this property.  This is internal
        /// to MSBuild, and is read-only.
        /// </summary>
        /// <owner>RGoel</owner>
        internal XmlElement PropertyElement
        {
            get
            {
                return this.propertyElement;
            }
        }

        /// <summary>
        /// We need to store a reference to the parent BuildPropertyGroup, so we can
        /// send up notifications.
        /// </summary>
        /// <owner>RGoel</owner>
        internal BuildPropertyGroup ParentPersistedPropertyGroup
        {
            get
            {
                return this.parentPersistedPropertyGroup;
            }

            set
            {
                ErrorUtilities.VerifyThrow( ((value == null) && (this.parentPersistedPropertyGroup != null)) || ((value != null) && (this.parentPersistedPropertyGroup == null)),
                    "Either new parent cannot be assigned because we already have a parent, or old parent cannot be removed because none exists.");

                this.parentPersistedPropertyGroup = value;
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Given a property bag, this method evaluates the current property,
        /// expanding any property references contained within.  It stores this
        /// evaluated value in the "finalValue" member.
        /// </summary>
        /// <owner>RGoel</owner>
        internal void Evaluate
        (
            Expander expander
        )
        {
            ErrorUtilities.VerifyThrow(expander != null, "Expander required to evaluated property.");

            this.finalValueEscaped = expander.ExpandAllIntoStringLeaveEscaped(this.Value, this.propertyElement);
        }

        /// <summary>
        /// Marks the parent project as dirty.
        /// </summary>
        /// <owner>RGoel</owner>
        private void MarkPropertyAsDirty
            (
            )
        {
            if (this.ParentPersistedPropertyGroup != null)
            {
                ErrorUtilities.VerifyThrow(this.ParentPersistedPropertyGroup.ParentProject != null, "Persisted BuildPropertyGroup doesn't have parent project.");
                this.ParentPersistedPropertyGroup.MarkPropertyGroupAsDirty();
            }
        }

        /// <summary>
        /// Creates a shallow or deep clone of this BuildProperty object.
        ///
        /// A shallow clone points at the same XML element as the original, so
        /// that modifications to the name or value will be reflected in both
        /// copies.  However, the two copies could have different a finalValue.
        ///
        /// A deep clone actually clones the XML element as well, so that the
        /// two copies are completely independent of each other.
        /// </summary>
        /// <param name="deepClone"></param>
        /// <returns></returns>
        /// <owner>rgoel</owner>
        public BuildProperty Clone
        (
            bool deepClone
        )
        {
            BuildProperty clone;

            // If this property object is represented as an XML element.
            if (this.propertyElement != null)
            {
                XmlElement newPropertyElement;

                if (deepClone)
                {
                    // Clone the XML element itself.  The new XML element will be
                    // associated with the same XML document as the original property,
                    // but won't actually get added to the XML document.
                    newPropertyElement = (XmlElement)this.propertyElement.Clone();
                }
                else
                {
                    newPropertyElement = this.propertyElement;
                }

                // Create the cloned BuildProperty object, and return it.
                clone = new BuildProperty(newPropertyElement, this.propertyValue, this.Type);
            }
            else
            {
                // Otherwise, it's just an in-memory property.  We can't do a shallow
                // clone for this type of property, because there's no XML element for
                // the clone to share.
                ErrorUtilities.VerifyThrowInvalidOperation(deepClone, "ShallowCloneNotAllowed");

                // Create a new property, using the same name, value, and property type.
                clone = new BuildProperty(this.Name, this.Value, this.Type);
            }

            // Do not set the ParentPersistedPropertyGroup on the cloned property, because it isn't really
            // part of the property group.

            // Be certain we didn't copy the value string: it's a waste of memory
            ErrorUtilities.VerifyThrow(Object.ReferenceEquals(clone.Value, this.Value), "Clone value should be identical reference");

            return clone;
        }

        /// <summary>
        /// Compares two BuildProperty objects ("this" and "compareToProperty") to determine
        /// if all the fields within the BuildProperty are the same.
        /// </summary>
        /// <param name="compareToProperty"></param>
        /// <returns>true if the properties are equivalent, false otherwise</returns>
        internal bool IsEquivalent
        (
            BuildProperty compareToProperty
        )
        {
            // Intentionally do not compare parentPersistedPropertyGroup, because this is 
            // just a back-pointer, and doesn't really contribute to the "identity" of
            // the property.

            return
                (compareToProperty != null) &&
                (0 == String.Compare(compareToProperty.propertyName, this.propertyName, StringComparison.OrdinalIgnoreCase)) &&
                (compareToProperty.propertyValue                == this.propertyValue) &&
                (compareToProperty.FinalValue                   == this.FinalValue) &&
                (compareToProperty.type                         == this.type);
        }

        /// <summary>
        /// Returns the property value.
        /// </summary>
        /// <owner>RGoel</owner>
        public override string ToString
            (
            )
        {
            return (string) this;
        }

        #endregion

        #region Operators

        /// <summary>
        /// This allows an implicit typecast from a "BuildProperty" to a "string"
        /// when trying to access the property's value.
        /// </summary>
        /// <param name="propertyToCast"></param>
        /// <returns></returns>
        /// <owner>rgoel</owner>
        public static explicit operator string
        (
            BuildProperty propertyToCast
        )
        {
            if (propertyToCast == null)
            {
                return String.Empty;
            }

            return propertyToCast.FinalValue;
        }

        #endregion
    }
}
