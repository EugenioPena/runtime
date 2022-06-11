// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CoreLab
{
    using System;
    using System.Reflection;
    using System.Diagnostics;

    internal static class Program
    {
        private static void Main(string[] args)
        {
            try
            {
                System.Convert.FromHexString(null);
            }
            catch (Exception)
            {
            }
        }
    }
}
