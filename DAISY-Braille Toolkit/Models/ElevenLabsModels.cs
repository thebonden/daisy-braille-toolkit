using System.Collections.Generic;

namespace DAISY_Braille_Toolkit.Models
{
    public static class ElevenLabsModels
    {
        // Simple, stable defaults. Du kan ændre listen senere i UI uden hardcode i pipeline.
        public const string Default = "eleven_multilingual_v2";

        public static readonly List<string> All = new()
        {
            "eleven_v3",
            "eleven_multilingual_v2",
            "eleven_turbo_v2_5",
            "eleven_flash_v2_5"
        };
    }
}
