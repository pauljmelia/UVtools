﻿/*
 *                     GNU AFFERO GENERAL PUBLIC LICENSE
 *                       Version 3, 19 November 2007
 *  Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
 *  Everyone is permitted to copy and distribute verbatim copies
 *  of this license document, but changing it is not allowed.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UVtools.Core.Extensions;

public static class TypeExtensions
{
    /// <summary>
    /// Creates a new instance of this type
    /// </summary>
    /// <param name="type"></param>
    /// <param name="paramArray"></param>
    public static object? CreateInstance(this Type type, params object[]? paramArray)
    {
        return Activator.CreateInstance(type, paramArray);
    }

    /// <summary>
    /// Creates a new instance of this type
    /// </summary>
    /// <typeparam name="T">Target type</typeparam>
    /// <param name="type"></param>
    /// <param name="paramArray"></param>
    /// <returns>New instance of {T}</returns>
    public static T? CreateInstance<T>(this Type type, params object[]? paramArray)
    {
        var instance = Activator.CreateInstance(type, paramArray);
        if (instance is null) return default;
        return (T)instance;
    }

    public static IEnumerable<Type> GetTypesInNamespace(Assembly assembly, string nameSpace)
    {
        return assembly.GetTypes()
                .Where(t => string.Equals(t.Namespace, nameSpace, StringComparison.Ordinal));
    }

    public static bool IsPrimitive(this Type type)
    {
        return type.IsPrimitive || type.IsEnum || type.IsValueType || type == typeof(string);
    }
}