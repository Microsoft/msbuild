// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using Microsoft.Build.Collections;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;

namespace Microsoft.Build.BackEnd
{
    /// <summary>
    /// This class is responsible for serializing and deserializing anything that is not 
    /// officially supported by ITranslator, but that we still want to do 
    /// custom translation of.  
    /// </summary>
    internal static class TranslatorExtensions
    {
        private static Lazy<ConcurrentDictionary<Type, ConstructorInfo>> parameterlessConstructorCache = new Lazy<ConcurrentDictionary<Type, ConstructorInfo>>(() => new ConcurrentDictionary<Type, ConstructorInfo>());

        /// <summary>
        /// Translates a PropertyDictionary of ProjectPropertyInstances.
        /// </summary>
        /// <param name="translator">The tranlator doing the translating</param>
        /// <param name="value">The dictionary to translate.</param>
        public static void TranslateProjectPropertyInstanceDictionary(this ITranslator translator, ref PropertyDictionary<ProjectPropertyInstance> value)
        {
            if (!translator.TranslateNullable(value))
            {
                return;
            }

            if (translator.Mode == TranslationDirection.ReadFromStream)
            {
                int count = 0;
                translator.Translate(ref count);

                value = new PropertyDictionary<ProjectPropertyInstance>(count);
                for (int i = 0; i < count; i++)
                {
                    ProjectPropertyInstance instance = null;
                    translator.Translate(ref instance, ProjectPropertyInstance.FactoryForDeserialization);
                    value[instance.Name] = instance;
                }
            }
            else // TranslationDirection.WriteToStream
            {
                int count = value.Count;
                translator.Translate(ref count);

                foreach (ProjectPropertyInstance instance in value)
                {
                    ProjectPropertyInstance instanceForSerialization = instance;
                    translator.Translate(ref instanceForSerialization, ProjectPropertyInstance.FactoryForDeserialization);
                }
            }
        }

        /// <summary>
        /// Deserialize a type or a subtype by its full name. The type must implement ITranslateable
        /// </summary>
        /// <typeparam name="T">Top level type. Serialized types can be of this type, or subtypes</typeparam>
        /// <returns></returns>
        public static T FactoryForDeserializingTypeWithName<T>(this ITranslator translator)
        {
            string typeName = null;
            translator.Translate(ref typeName);

            var type = Type.GetType(typeName);
            ErrorUtilities.VerifyThrowInvalidOperation(type != null, "type cannot be null");
            ErrorUtilities.VerifyThrowInvalidOperation(
                typeof(T).IsAssignableFrom(type),
                $"{typeName} must be a {typeof(T).FullName}");
            ErrorUtilities.VerifyThrowInvalidOperation(
                typeof(ITranslatable).IsAssignableFrom(type),
                $"{typeName} must be a {nameof(ITranslatable)}");

            var parameterlessConstructor = parameterlessConstructorCache.Value.GetOrAdd(
                type,
                t =>
                {
                    ConstructorInfo constructor = null;
#if FEATURE_TYPE_GETCONSTRUCTOR
                    constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
#else
                    constructor =
                        type
                            .GetTypeInfo()
                            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                            .FirstOrDefault(c => c.GetParameters().Length == 0);
#endif
                    ErrorUtilities.VerifyThrowInvalidOperation(
                        constructor != null,
                        $"{typeName} must have a private parameterless constructor");
                    return constructor;
                });

            var targetInstanceChild = (ITranslatable) parameterlessConstructor.Invoke(Array.Empty<object>());

            targetInstanceChild.Translate(translator);

            return (T) targetInstanceChild;
        }

        public static void TranslateOptionalBuildEventContext(this ITranslator translator, ref BuildEventContext buildEventContext)
        {
            if (translator.Mode == TranslationDirection.ReadFromStream)
            {
                buildEventContext = translator.Reader.ReadOptionalBuildEventContext();
            }
            else
            {
                translator.Writer.WriteOptionalBuildEventContext(buildEventContext);
            }
        }
    }
}
