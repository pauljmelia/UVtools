﻿/*
 *                     GNU AFFERO GENERAL PUBLIC LICENSE
 *                       Version 3, 19 November 2007
 *  Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
 *  Everyone is permitted to copy and distribute verbatim copies
 *  of this license document, but changing it is not allowed.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Xml;
using ZLinq;

namespace UVtools.Core.Extensions;

public static class ZipArchiveExtensions
{
    /// <summary>
    /// Used to specify what our overwrite policy
    /// is for files we are extracting.
    /// </summary>
    public enum Overwrite
    {
        Always,
        IfNewer,
        Never
    }

    /// <summary>
    /// Used to identify what we will do if we are
    /// trying to create a zip file and it already
    /// exists.
    /// </summary>
    public enum ArchiveAction
    {
        Merge,
        Replace,
        Error,
        Ignore
    }

    /// <summary>
    /// Unzips the specified file to the given folder in a safe
    /// manner.  This plans for missing paths and existing files
    /// and handles them gracefully.
    /// </summary>
    /// <param name="sourceArchiveFileName">
    /// The name of the zip file to be extracted
    /// </param>
    /// <param name="destinationDirectoryName">
    /// The directory to extract the zip file to
    /// </param>
    /// <param name="overwriteMethod">
    /// Specifies how we are going to handle an existing file.
    /// The default is IfNewer.
    /// </param>
    public static void ImprovedExtractToDirectory(string sourceArchiveFileName, string destinationDirectoryName, Overwrite overwriteMethod = Overwrite.IfNewer)
    {
        //Opens the zip file up to be read
        using var archive = ZipFile.OpenRead(sourceArchiveFileName);
        archive.ImprovedExtractToDirectory(destinationDirectoryName, overwriteMethod);
    }

    /// <summary>
    /// Unzips the specified file to the given folder in a safe
    /// manner.  This plans for missing paths and existing files
    /// and handles them gracefully.
    /// </summary>
    /// <param name="archive">
    /// The zip file to be extracted
    /// </param>
    /// <param name="destinationDirectoryName">
    /// The directory to extract the zip file to
    /// </param>
    /// <param name="overwriteMethod">
    /// Specifies how we are going to handle an existing file.
    /// The default is IfNewer.
    /// </param>
    /// <returns>The number of extracted files</returns>
    public static int ImprovedExtractToDirectory(this ZipArchive archive, string destinationDirectoryName, Overwrite overwriteMethod = Overwrite.IfNewer)
    {
        int count = 0;
        //Loops through each file in the zip file
        foreach (var file in archive.Entries)
        {
            if(!string.IsNullOrEmpty(file.ImprovedExtractToFile(destinationDirectoryName, true, overwriteMethod))) count++;
        }

        return count;
    }

    /// <summary>
    /// Safely extracts a single file from a zip file
    /// </summary>
    /// <param name="entry">
    /// The zip entry we are pulling the file from
    /// </param>
    /// <param name="destinationPath">
    /// The root of where the file is going
    /// </param>
    /// <param name="preserveFullName">True to preserve full name and create all directories up to the file, otherwise false to extract the file just to <paramref name="destinationPath"/></param>
    /// <param name="overwriteMethod">
    /// Specifies how we are going to handle an existing file.
    /// The default is Overwrite.IfNewer.
    /// </param>
    /// <returns>The extracted file path</returns>
    public static string? ImprovedExtractToFile(this ZipArchiveEntry entry, string destinationPath, bool preserveFullName = true, Overwrite overwriteMethod = Overwrite.IfNewer)
    {
        //Gets the complete path for the destination file, including any
        //relative paths that were in the zip file
        var destFileName = Path.GetFullPath(Path.Combine(destinationPath, preserveFullName ? entry.FullName : entry.Name));
        var fullDestDirPath = Path.GetFullPath(Path.Combine(destinationPath, (preserveFullName ? Path.GetDirectoryName(entry.FullName) : string.Empty)!) + Path.DirectorySeparatorChar);
        if (!destFileName.StartsWith(fullDestDirPath)) return null; // Entry is outside the target dir

        //Creates the directory (if it doesn't exist) for the new path
        Directory.CreateDirectory(fullDestDirPath);

        //Determines what to do with the file based upon the
        //method of overwriting chosen
        switch (overwriteMethod)
        {
            case Overwrite.Always:
                //Just put the file in and overwrite anything that is found
                entry.ExtractToFile(destFileName, true);
                break;
            case Overwrite.IfNewer:
                //Checks to see if the file exists, and if so, if it should
                //be overwritten
                if (!File.Exists(destFileName) || File.GetLastWriteTime(destFileName) < entry.LastWriteTime)
                {
                    //Either the file didn't exist or this file is newer, so
                    //we will extract it and overwrite any existing file
                    entry.ExtractToFile(destFileName, true);
                }
                break;
            case Overwrite.Never:
                //Put the file in if it is new but ignores the
                //file if it already exists
                if (!File.Exists(destFileName))
                {
                    entry.ExtractToFile(destFileName);
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(overwriteMethod), overwriteMethod, null);
        }

        return destFileName;
    }

    /// <summary>
    /// Allows you to add files to an archive, whether the archive
    /// already exists or not
    /// </summary>
    /// <param name="archiveFullName">
    /// The name of the archive to you want to add your files to
    /// </param>
    /// <param name="files">
    /// A set of file names that are to be added
    /// </param>
    /// <param name="action">
    /// Specifies how we are going to handle an existing archive
    /// </param>
    /// <param name="fileOverwrite"></param>
    /// <param name="compression">
    /// Specifies what type of compression to use - defaults to Optimal
    /// </param>
    public static void AddToArchive(string archiveFullName,
        List<string> files,
        ArchiveAction action = ArchiveAction.Replace,
        Overwrite fileOverwrite = Overwrite.IfNewer,
        CompressionLevel compression = CompressionLevel.Optimal)
    {
        //Identifies the mode we will be using - the default is Create
        ZipArchiveMode mode = ZipArchiveMode.Create;

        //Determines if the zip file even exists
        bool archiveExists = File.Exists(archiveFullName);

        //Figures out what to do based upon our specified overwrite method
        switch (action)
        {
            case ArchiveAction.Merge:
                //Sets the mode to update if the file exists, otherwise
                //the default of Create is fine
                if (archiveExists)
                {
                    mode = ZipArchiveMode.Update;
                }
                break;
            case ArchiveAction.Replace:
                //Deletes the file if it exists.  Either way, the default
                //mode of Create is fine
                if (archiveExists)
                {
                    File.Delete(archiveFullName);
                }
                break;
            case ArchiveAction.Error:
                //Throws an error if the file exists
                if (archiveExists)
                {
                    throw new IOException($"The zip file {archiveFullName} already exists.");
                }
                break;
            case ArchiveAction.Ignore:
                //Closes the method silently and does nothing
                if (archiveExists)
                {
                    return;
                }
                break;
        }

        //Opens the zip file in the mode we specified
        using ZipArchive zipFile = ZipFile.Open(archiveFullName, mode);
        //This is a bit of a hack and should be refactored - I am
        //doing a similar foreach loop for both modes, but for Create
        //I am doing very little work while Update gets a lot of
        //code.  This also does not handle any other mode (of
        //which there currently wouldn't be one since we don't
        //use Read here).
        if (mode == ZipArchiveMode.Create)
        {
            foreach (string file in files)
            {
                //Adds the file to the archive
                zipFile.CreateEntryFromFile(file, Path.GetFileName(file), compression);
            }
        }
        else
        {
            foreach (string file in files)
            {
                var fileInZip = (from f in zipFile.Entries.AsValueEnumerable()
                    where f.Name == Path.GetFileName(file)
                    select f).FirstOrDefault();

                switch (fileOverwrite)
                {
                    case Overwrite.Always:
                        //Deletes the file if it is found
                        if (fileInZip != null)
                        {
                            fileInZip.Delete();
                        }

                        //Adds the file to the archive
                        zipFile.CreateEntryFromFile(file, Path.GetFileName(file), compression);

                        break;
                    case Overwrite.IfNewer:
                        //This is a bit trickier - we only delete the file if it is
                        //newer, but if it is newer or if the file isn't already in
                        //the zip file, we will write it to the zip file
                        if (fileInZip != null)
                        {
                            //Deletes the file only if it is older than our file.
                            //Note that the file will be ignored if the existing file
                            //in the archive is newer.
                            if (fileInZip.LastWriteTime < File.GetLastWriteTime(file))
                            {
                                fileInZip.Delete();

                                //Adds the file to the archive
                                zipFile.CreateEntryFromFile(file, Path.GetFileName(file), compression);
                            }
                        }
                        else
                        {
                            //The file wasn't already in the zip file so add it to the archive
                            zipFile.CreateEntryFromFile(file, Path.GetFileName(file), compression);
                        }
                        break;
                    case Overwrite.Never:
                        //Don't do anything - this is a decision that you need to
                        //consider, however, since this will mean that no file will
                        //be written.  You could write a second copy to the zip with
                        //the same name (not sure that is wise, however).
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Creates a new entry in the archive and returns a stream to write to it
    /// </summary>
    /// <param name="input"></param>
    /// <param name="entryName"></param>
    /// <returns></returns>
    public static Stream CreateEntryStream(this ZipArchive input, string entryName)
    {
        return input.CreateEntry(entryName).Open();
    }

    /// <summary>
    /// Creates a new entry in the archive and returns a stream to write to it
    /// </summary>
    /// <param name="input"></param>
    /// <param name="entryName"></param>
    /// <param name="compressionLevel"></param>
    /// <returns></returns>
    public static Stream CreateEntryStream(this ZipArchive input, string entryName, CompressionLevel compressionLevel)
    {
        return input.CreateEntry(entryName, compressionLevel).Open();
    }

    /// <summary>
    /// Gets an entry from the archive or creates it if it doesn't exist
    /// </summary>
    /// <param name="input"><see cref="ZipArchive"/></param>
    /// <param name="entryName">Filename to get and/or create</param>
    /// <returns>Created <see cref="ZipArchiveEntry"/></returns>
    public static ZipArchiveEntry GetOrCreateEntry(this ZipArchive input, string entryName)
    {
        return input.GetEntry(entryName) ?? input.CreateEntry(entryName);
    }

    /// <summary>
    /// Gets a stream from an entry of the archive or creates it if it doesn't exist
    /// </summary>
    /// <param name="input"><see cref="ZipArchive"/></param>
    /// <param name="entryName">Filename to get and/or create</param>
    /// <returns>Created <see cref="ZipArchiveEntry"/></returns>
    public static Stream GetOrCreateStream(this ZipArchive input, string entryName)
    {
        var entry = input.GetOrCreateEntry(entryName);
        var stream = entry.Open();
        return stream;
    }

    /// <summary>
    /// Create or update a file into archive and write content to it
    /// </summary>
    /// <param name="input"><see cref="ZipArchive"/></param>
    /// <param name="entryName">Filename to create</param>
    /// <param name="content">Content to write</param>
    /// <param name="mode"></param>
    /// <returns>Created <see cref="ZipArchiveEntry"/></returns>
    public static ZipArchiveEntry CreateEntryFromContent(this ZipArchive input, string entryName, string? content, ZipArchiveMode mode)
    {
        ZipArchiveEntry entry;
        if (mode == ZipArchiveMode.Update)
        {
            entry = input.GetEntry(entryName) ?? input.CreateEntry(entryName);
        }
        else
        {
            entry = input.CreateEntry(entryName);
        }

        if (string.IsNullOrEmpty(content)) return entry;
        using var stream = entry.Open();
        if (mode == ZipArchiveMode.Update) stream.SetLength(0);
        using TextWriter tw = new StreamWriter(stream);
        tw.Write(content);
        return entry;
    }

    /// <summary>
    /// Create or update a file into archive and write content to it
    /// </summary>
    /// <param name="input"><see cref="ZipArchive"/></param>
    /// <param name="entryName">Filename to create</param>
    /// <param name="content">Content to write</param>
    /// <param name="mode"></param>
    /// <returns>Created <see cref="ZipArchiveEntry"/></returns>
    public static ZipArchiveEntry CreateEntryFromContent(this ZipArchive input, string entryName, byte[]? content, ZipArchiveMode mode)
    {
        ZipArchiveEntry entry;
        if (mode == ZipArchiveMode.Update)
        {
            entry = input.GetEntry(entryName) ?? input.CreateEntry(entryName);
        }
        else
        {
            entry = input.CreateEntry(entryName);
        }

        if (content is null) return entry;
        using var stream = entry.Open();
        if (mode == ZipArchiveMode.Update) stream.SetLength(0);
        stream.Write(content, 0, content.Length);
        return entry;
    }

    /// <summary>
    /// Create or update a file into archive and write content to it
    /// </summary>
    /// <param name="input"><see cref="ZipArchive"/></param>
    /// <param name="entryName">Filename to create</param>
    /// <param name="content">Content to write</param>
    /// <param name="mode"></param>
    /// <returns>Created <see cref="ZipArchiveEntry"/></returns>
    public static ZipArchiveEntry CreateEntryFromContent(this ZipArchive input, string entryName, Stream? content, ZipArchiveMode mode)
    {
        ZipArchiveEntry entry;
        if (mode == ZipArchiveMode.Update)
        {
            entry = input.GetEntry(entryName) ?? input.CreateEntry(entryName);
        }
        else
        {
            entry = input.CreateEntry(entryName);
        }

        if (content is null) return entry;
        using var stream = entry.Open();
        if (mode == ZipArchiveMode.Update) stream.SetLength(0);
        content.Position = 0;
        content.CopyTo(stream);
        return entry;
    }

    /// <summary>
    /// Create or update a file into archive and write content to it
    /// </summary>
    /// <param name="input"></param>
    /// <param name="entryName"></param>
    /// <param name="classObject"></param>
    /// <param name="mode"></param>
    /// <param name="xmlOptions"></param>
    /// <param name="noNamespace"></param>
    /// <returns></returns>
    public static ZipArchiveEntry CreateEntryFromSerializeXml(this ZipArchive input, string entryName, object? classObject, ZipArchiveMode mode, XmlWriterSettings? xmlOptions = null, bool noNamespace = false)
    {
        ZipArchiveEntry entry;
        if (mode == ZipArchiveMode.Update)
        {
            entry = input.GetEntry(entryName) ?? input.CreateEntry(entryName);
        }
        else
        {
            entry = input.CreateEntry(entryName);
        }

        if (classObject is null) return entry;
        using var stream = entry.Open();
        if (mode == ZipArchiveMode.Update) stream.SetLength(0);

        if(xmlOptions is null)
        {
            XmlExtensions.Serialize(classObject, stream, noNamespace);
        }
        else
        {
            XmlExtensions.Serialize(classObject, stream, xmlOptions, noNamespace);
        }


        return entry;
    }

    /// <summary>
    /// Create or update a file into archive and write content to it
    /// </summary>
    /// <param name="input"></param>
    /// <param name="entryName"></param>
    /// <param name="classObject"></param>
    /// <param name="mode"></param>
    /// <param name="jsonOptions"></param>
    /// <returns></returns>
    public static ZipArchiveEntry CreateEntryFromSerializeJson(this ZipArchive input, string entryName, object? classObject, ZipArchiveMode mode, JsonSerializerOptions? jsonOptions = null)
    {
        ZipArchiveEntry entry;
        if (mode == ZipArchiveMode.Update)
        {
            entry = input.GetEntry(entryName) ?? input.CreateEntry(entryName);
        }
        else
        {
            entry = input.CreateEntry(entryName);
        }

        if (classObject is null) return entry;
        using var stream = entry.Open();
        if (mode == ZipArchiveMode.Update) stream.SetLength(0);

        jsonOptions ??= JsonSerializerOptions.Default;
        JsonSerializer.Serialize(stream, classObject, jsonOptions);

        return entry;
    }
}