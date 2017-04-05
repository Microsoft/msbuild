﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
internal static class FXAssembly
{
    internal const string Version = "4.0.0.0";
}

#pragma warning disable 436
internal static class AssemblyRef
{
    internal const string EcmaPublicKey = "b77a5c561934e089";
    internal const string EcmaPublicKeyToken = "b77a5c561934e089";
    internal const string EcmaPublicKeyFull = "00000000000000000400000000000000";
    internal const string PlatformPublicKey = EcmaPublicKey;
    internal const string Mscorlib = "mscorlib, Version=" + FXAssembly.Version + ", Culture=neutral, PublicKeyToken=" + PlatformPublicKey;
    internal const string SystemData = "System.Data, Version=" + FXAssembly.Version + ", Culture=neutral, PublicKeyToken=" + EcmaPublicKey;
    internal const string SystemXml = "System.Xml, Version=" + FXAssembly.Version + ", Culture=neutral, PublicKeyToken=" + EcmaPublicKey;
    internal const string MicrosoftPublicKey = "b03f5f7f11d50a3a";
    internal const string SharedLibPublicKey = "31bf3856ad364e35";
    internal const string ASPBrowserCapsPublicKey = "b7bd7678b977bd8f";
}
#pragma warning restore 436
