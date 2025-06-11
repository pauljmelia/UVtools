﻿/*
 *                     GNU AFFERO GENERAL PUBLIC LICENSE
 *                       Version 3, 19 November 2007
 *  Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
 *  Everyone is permitted to copy and distribute verbatim copies
 *  of this license document, but changing it is not allowed.
 */

using System.CommandLine;
using System.IO;

namespace UVtools.Cmd.Symbols;

internal static class GlobalOptions
{
    internal static Option<bool> DummyOption { get; } = new(["--dummy"], "Do not save alterations to file");
    internal static Option<bool> QuietOption { get; } = new(["-q", "--quiet"], "Make output silent but exceptions error will still show");
    internal static Option<bool> NoProgressOption { get; } = new(["--no-progress"], "Show no progress");
    internal static Option<FileInfo> OutputFile { get; } = new(["-o", "--output"], "Output file to save");

    internal static Option<bool> OpenInPartialMode { get; } = new(["--partial-mode"], "Fast load the file in partial mode");
    internal static Option<bool> OpenInFullMode { get; } = new(["--full-mode"], "Load the file in full/complete mode");
}