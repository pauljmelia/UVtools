using UVtools.Core.FileFormats;
using UVtools.UI.Controls;
using ZLinq;

namespace UVtools.UI.Windows;

public partial class VersionSelectorWindow : WindowEx
{
    private uint _version;

    public string DescriptionText =>
        $"This file format \"{FileExtension.Description}\" contains multiple available versions. Some versions may require a specific firmware version in order to run.\n" +
        "Select the version you wish to use on the output file.\n" +
        $"If unsure, use the default version {SlicerFile.DefaultVersion}.";

#pragma warning disable CS8765
    public sealed override FileFormat SlicerFile { get; set; } = null!;
#pragma warning restore CS8765

    public FileExtension FileExtension { get; init; } = null!;

    public uint[] AvailableVersions { get; init; } = null!;

    public uint Version
    {
        get => _version;
        set => RaiseAndSetIfChanged(ref _version, value);
    }

    public VersionSelectorWindow()
    {
        InitializeComponent();
        DialogResult = DialogResults.Cancel;
    }

    public VersionSelectorWindow(FileFormat slicerFile, FileExtension fileExtension, uint[] availableVersions) : this()
    {
        SlicerFile = slicerFile;
        FileExtension = fileExtension;
        Version = availableVersions.AsValueEnumerable().Contains(slicerFile.DefaultVersion) ? SlicerFile.DefaultVersion : availableVersions[^1];
        AvailableVersions = availableVersions;
        Title += $" - {FileExtension.Description}";
        DataContext = this;
    }

    public void SelectVersion()
    {
        DialogResult = DialogResults.OK;
        Close();
    }

    public void SelectDefault()
    {
        Version = AvailableVersions.AsValueEnumerable().Contains(SlicerFile.DefaultVersion) ? SlicerFile.DefaultVersion : AvailableVersions[^1];
        DialogResult = DialogResults.OK;
        Close();
    }
}