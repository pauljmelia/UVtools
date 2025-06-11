﻿/*
 *                     GNU AFFERO GENERAL PUBLIC LICENSE
 *                       Version 3, 19 November 2007
 *  Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
 *  Everyone is permitted to copy and distribute verbatim copies
 *  of this license document, but changing it is not allowed.
 */

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace UVtools.Core.FileFormats;

/// <summary>
/// Represents a file extension for slicer file formats
/// </summary>
public sealed class FileExtension : IEquatable<FileExtension>, IEquatable<string>
{
    #region Properties
    /// <summary>
    /// Stores a specific <see cref="FileFormat"/> type that should be used to create with this FileExtension instance
    /// </summary>
    [JsonIgnore]
    [XmlIgnore]
    public Type FileFormatType { get; }

    /// <summary>
    /// Gets the extension name without the dot (.)
    /// </summary>
    public string Extension { get; }

    /// <summary>
    /// Gets the extension description
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets if the extension shows up on open file dialog filters
    /// </summary>
    public bool IsVisibleOnFileFilters { get; }

    /// <summary>
    /// Gets if the extension shows up on convert to menu
    /// </summary>
    public bool IsVisibleOnConvertMenu { get; }

    /// <summary>
    /// Gets a tag object
    /// </summary>
    [JsonIgnore]
    [XmlIgnore]
    public object? Tag { get; }

    /// <summary>
    /// Gets the file filter for open and save dialogs
    /// </summary>
    public string Filter => $"{Description} (*.{Extension})|*.{Extension}";
    #endregion

    #region Constructor

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="fileFormatType">The exact <see cref="FileFormat"/> type</param>
    /// <param name="extension">The extension name without the dot (.)</param>
    /// <param name="description">The extension description</param>
    /// <param name="isVisibleOnFileFilters">True if this extension is visible on open file dialog filters</param>
    /// <param name="isVisibleOnConvertMenu">True if this extension is visible on convert to menu</param>
    /// <param name="tag">Tag object</param>
    public FileExtension(Type fileFormatType, string extension, string description, bool isVisibleOnFileFilters = true, bool isVisibleOnConvertMenu = true, object? tag = null)
    {
        FileFormatType = fileFormatType;
        Extension = extension;
        Description = description;
        IsVisibleOnFileFilters = isVisibleOnFileFilters;
        IsVisibleOnConvertMenu = isVisibleOnConvertMenu;
        Tag = tag;
    }
    #endregion

    #region Overrides

    public override string ToString()
    {
        return $"{Description} ({Extension})";
    }

    public bool Equals(FileExtension? other)
    {
        return Extension.Equals(other?.Extension, StringComparison.OrdinalIgnoreCase);
    }

    public bool Equals(string? other)
    {
        return Extension.Equals(other, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is FileExtension other && Equals(other);
    }

    public override int GetHashCode()
    {
        return (Extension.GetHashCode());
    }

    private sealed class ExtensionEqualityComparer : IEqualityComparer<FileExtension>
    {
        public bool Equals(FileExtension? x, FileExtension? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(x, null)) return false;
            if (ReferenceEquals(y, null)) return false;
            if (x.GetType() != y.GetType()) return false;
            return x.Extension == y.Extension;
        }

        public int GetHashCode(FileExtension obj)
        {
            return (obj.Extension.GetHashCode());
        }
    }

    public static IEqualityComparer<FileExtension> ExtensionComparer { get; } = new ExtensionEqualityComparer();
    #endregion

    #region Methods

    public FileFormat? GetFileFormat(bool createNewInstance = false) => FileFormat.FindByType(FileFormatType, createNewInstance);
    /*FileFormatType is null
        ? FileFormat.FindByExtensionOrFilePath(Extension, createNewInstance)
        : FileFormat.FindByType(FileFormatType, createNewInstance);*/

    public static FileExtension? Find(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return null;
        if (extension.StartsWith('.')) extension = extension[..1];
        return FileFormat.FindExtension(extension);
    }

    public static IEnumerable<FileExtension> FindAll(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return [];
        if (extension.StartsWith('.')) extension = extension[..1];
        return FileFormat.FindExtensions(extension);
    }


    #endregion
}