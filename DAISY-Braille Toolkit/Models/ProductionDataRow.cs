using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DAISY_Braille_Toolkit.Models
{
    /// <summary>
    /// A flexible row for Production CSV output.
    /// Column names are loaded from Data/metadata daisy.txt and accessed via the string indexer.
    /// </summary>
    public sealed class ProductionDataRow : INotifyPropertyChanged
    {
        private readonly Dictionary<string, string> _fields = new(StringComparer.OrdinalIgnoreCase);

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Default indexer: allows WPF binding with Binding Path=["Column Name"].
        /// </summary>
        public string this[string key]
        {
            get
            {
                key ??= string.Empty;
                return _fields.TryGetValue(key, out var v) ? v ?? string.Empty : string.Empty;
            }
            set
            {
                key ??= string.Empty;
                _fields[key] = value ?? string.Empty;
                OnPropertyChanged("Item[]");
            }
        }

        public IReadOnlyDictionary<string, string> Fields => _fields;

        public static string CsvHeader(IReadOnlyList<string> columns)
            => string.Join(",", columns.Select(EscapeCsv));

        public string ToCsvRow(IReadOnlyList<string> columns)
            => string.Join(",", columns.Select(c => EscapeCsv(this[c])));

        private static string EscapeCsv(string? s)
        {
            s ??= string.Empty;
            if (s.Contains('"')) s = s.Replace("\"", "\"\"");
            if (s.Contains(',') || s.Contains('\n') || s.Contains('\r') || s.Contains('"'))
                return "\"" + s + "\"";
            return s;
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
    }
}
