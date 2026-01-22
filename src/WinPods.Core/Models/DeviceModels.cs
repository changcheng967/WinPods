namespace WinPods.Core.Models
{
    /// <summary>
    /// AirPods and Beats device model identifiers.
    /// </summary>
    public enum AirPodsModel : ushort
    {
        // AirPods
        AirPods1 = 0x0220,
        AirPods2 = 0x0F20,
        AirPods3 = 0x1320,
        AirPods4 = 0x1720,   // Unconfirmed

        // AirPods Pro
        AirPodsPro = 0x0E20,
        AirPodsPro2 = 0x1420,
        AirPodsPro2_USBC = 0x1520,   // USB-C variant
        AirPodsPro2_2024 = 0x2024,   // AirPods Pro 2 (2024 USB-C model)
        AirPodsPro2_Variant = 0x1920,   // Another Pro 2 variant

        // AirPods Max
        AirPodsMax = 0x0A20,

        // Beats (same protocol) - Note: Some values may overlap with AirPods
        PowerbeatsPro = 0x0B20,
        PowerbeatsPro2 = 0x1120,
        BeatsFitPro = 0x1220,
        BeatsStudioBuds = 0x1020,
        BeatsStudioPro = 0x1820,
        BeatsSolo3 = 0x0620,
        BeatsSoloP = 0x0C20,
        BeatsX = 0x0520,

        Unknown = 0x0000
    }

    /// <summary>
    /// Device color codes.
    /// </summary>
    public enum DeviceColor : byte
    {
        White = 0x00,
        Black = 0x01,
        Red = 0x02,
        Blue = 0x03,
        Pink = 0x04,
        Gray = 0x05,
        Silver = 0x06,
        Gold = 0x07,
        RoseGold = 0x08,
        SpaceGray = 0x09,
        DarkCherry = 0x0A,
        Green = 0x0B
    }

    /// <summary>
    /// Device position/status in ear or case.
    /// </summary>
    [Flags]
    public enum PodStatus : byte
    {
        None = 0x00,
        LeftInEar = 0x01,
        RightInEar = 0x02,
        BothInEar = 0x03,
        LeftInCase = 0x10,
        RightInCase = 0x20,
        BothInCase = 0x30,
        CaseOpen = 0x40
    }

    /// <summary>
    /// Helper methods for device model detection.
    /// </summary>
    public static class DeviceModelHelper
    {
        /// <summary>
        /// Gets the human-readable name for a device model.
        /// </summary>
        public static string GetDisplayName(AirPodsModel model)
        {
            return model switch
            {
                AirPodsModel.AirPods1 => "AirPods (1st Generation)",
                AirPodsModel.AirPods2 => "AirPods (2nd Generation)",
                AirPodsModel.AirPods3 => "AirPods (3rd Generation)",
                AirPodsModel.AirPods4 => "AirPods (4th Generation)",
                AirPodsModel.AirPodsPro => "AirPods Pro",
                AirPodsModel.AirPodsPro2 => "AirPods Pro (2nd Generation)",
                AirPodsModel.AirPodsPro2_USBC => "AirPods Pro (2nd Generation, USB-C)",
                AirPodsModel.AirPodsPro2_2024 => "AirPods Pro (2nd Generation, USB-C)",
                AirPodsModel.AirPodsPro2_Variant => "AirPods Pro (2nd Generation)",
                AirPodsModel.AirPodsMax => "AirPods Max",
                AirPodsModel.PowerbeatsPro => "Powerbeats Pro",
                AirPodsModel.PowerbeatsPro2 => "Powerbeats Pro 2",
                AirPodsModel.BeatsFitPro => "Beats Fit Pro",
                AirPodsModel.BeatsStudioBuds => "Beats Studio Buds",
                AirPodsModel.BeatsStudioPro => "Beats Studio Pro",
                AirPodsModel.BeatsSolo3 => "Beats Solo3",
                AirPodsModel.BeatsSoloP => "Beats Solo Pro",
                AirPodsModel.BeatsX => "BeatsX",
                _ => "Unknown Device"
            };
        }

        /// <summary>
        /// Detects device model from model ID.
        /// </summary>
        public static AirPodsModel FromModelId(ushort modelId)
        {
            // Try exact match first
            if (Enum.IsDefined(typeof(AirPodsModel), modelId))
            {
                return (AirPodsModel)modelId;
            }

            // Fallback for unknown models
            return AirPodsModel.Unknown;
        }

        /// <summary>
        /// Checks if the model is an AirPods device (vs Beats).
        /// </summary>
        public static bool IsAirPods(AirPodsModel model)
        {
            return model switch
            {
                AirPodsModel.AirPods1 or AirPodsModel.AirPods2 or AirPodsModel.AirPods3
                    or AirPodsModel.AirPods4
                    or AirPodsModel.AirPodsPro or AirPodsModel.AirPodsPro2
                    or AirPodsModel.AirPodsPro2_USBC or AirPodsModel.AirPodsPro2_2024
                    or AirPodsModel.AirPodsPro2_Variant
                    or AirPodsModel.AirPodsMax => true,
                _ => false
            };
        }
    }
}
