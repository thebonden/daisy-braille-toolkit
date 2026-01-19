using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DAISY_Braille_Toolkit.Models;

namespace DAISY_Braille_Toolkit.Services
{
    public sealed class JobWorkspace
    {
        public string JobFolder { get; }
        public string SegmentsFolder => Path.Combine(JobFolder, "segments");

        public JobWorkspace(string jobFolder)
        {
            if (string.IsNullOrWhiteSpace(jobFolder))
                throw new ArgumentException("Job folder is required.", nameof(jobFolder));

            JobFolder = jobFolder;
            Directory.CreateDirectory(JobFolder);
            Directory.CreateDirectory(SegmentsFolder);
        }

        public List<SegmentItem> BuildSegmentsFromSourceText(string sourceText, string modelId)
        {
            var safeMax = TextSegmenter.GetSafeMaxChars(modelId);
            var segments = TextSegmenter.SplitToSegments(sourceText, safeMax);

            var list = new List<SegmentItem>();
            for (int i = 0; i < segments.Count; i++)
            {
                var idx = i + 1;
                var sourcePath = Path.Combine(SegmentsFolder, $"{idx:0000}.source.txt");
                var ttsPath = Path.Combine(SegmentsFolder, $"{idx:0000}.tts.txt");

                File.WriteAllText(sourcePath, segments[i], Encoding.UTF8);

                if (!File.Exists(ttsPath))
                    File.WriteAllText(ttsPath, segments[i], Encoding.UTF8);

                list.Add(new SegmentItem { Index = idx, SourcePath = sourcePath, TtsPath = ttsPath });
            }

            return list;
        }
    }
}
