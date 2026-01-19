namespace DAISY_Braille_Toolkit.Models
{
    public sealed class SegmentItem
    {
        public int Index { get; set; }
        public string SourcePath { get; set; } = "";
        public string TtsPath { get; set; } = "";

        public string Title => $"{Index:0000}";
        public override string ToString() => Title;
    }
}
