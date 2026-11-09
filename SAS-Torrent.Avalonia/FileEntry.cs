using System.ComponentModel;
using System.Runtime.CompilerServices;
using SAS_Torrent.Core.Torrent;

namespace SAS_Torrent.Avalonia;

public sealed class FileEntry : INotifyPropertyChanged
{
    private readonly TorrentFile _file;

    public FileEntry(TorrentFile file, int pieceCount)
    {
        _file = file;
        PieceCount = pieceCount;
    }

    public string RelativePath => _file.RelativePath;
    public int PieceCount { get; }
    public string SizeText => $"{SizeFormatter.Format(_file.Length)} ({PieceCount} кус.)";

    public bool IsSelected
    {
        get => _file.IsSelected;
        set
        {
            if (_file.IsSelected == value) return;
            _file.IsSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}