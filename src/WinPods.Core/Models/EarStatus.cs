namespace WinPods.Core.Models
{
    /// <summary>
    /// Ear detection state for AirPods.
    /// </summary>
    public enum EarDetectionState
    {
        /// <summary>
        /// Both earbuds are in the case.
        /// </summary>
        BothInCase,

        /// <summary>
        /// Left earbud in ear, right in case.
        /// </summary>
        LeftInEar,

        /// <summary>
        /// Right earbud in ear, left in case.
        /// </summary>
        RightInEar,

        /// <summary>
        /// Both earbuds in ear.
        /// </summary>
        BothInEar,

        /// <summary>
        /// One or both earbuds out (not in ear, not in case).
        /// </summary>
        OneOrBothOut
    }

    /// <summary>
    /// Helper methods for parsing ear status from the status byte.
    /// Status byte format: [bit 7: broadcast side] [bits 4-6: right status] [bits 0-3: left status]
    /// Common values: 0x3 = in ear, 0x5 = in case
    /// </summary>
    public static class EarStatusParser
    {
        /// <summary>
        /// Parses the ear detection state from the status byte.
        /// </summary>
        /// <param name="status">Status byte from AirPods advertisement</param>
        /// <returns>Current ear detection state</returns>
        public static EarDetectionState ParseFromStatus(byte status)
        {
            // Remove the broadcast bit (bit 7) to get the actual status
            byte statusWithoutBroadcast = (byte)(status & 0x7F);

            // Extract left and right status from nibbles
            // Low nibble (bits 0-3): left earbud status
            // High nibble (bits 4-6): right earbud status
            byte leftStatus = (byte)(statusWithoutBroadcast & 0x0F);
            byte rightStatus = (byte)((statusWithoutBroadcast >> 4) & 0x07);

            // Parse individual earbud states
            // 0x3 = in ear, 0x5 = in case
            bool leftInEar = leftStatus == 0x03;
            bool rightInEar = rightStatus == 0x03;
            bool leftInCase = leftStatus == 0x05;
            bool rightInCase = rightStatus == 0x05;

            // Determine overall state
            if (leftInEar && rightInEar)
            {
                return EarDetectionState.BothInEar;
            }
            else if (leftInEar && !rightInEar)
            {
                return EarDetectionState.LeftInEar;
            }
            else if (!leftInEar && rightInEar)
            {
                return EarDetectionState.RightInEar;
            }
            else if (leftInCase && rightInCase)
            {
                return EarDetectionState.BothInCase;
            }
            else
            {
                // One or both are out (not in ear, not in case)
                return EarDetectionState.OneOrBothOut;
            }
        }

        /// <summary>
        /// Checks if the given state represents at least one earbud in ear.
        /// </summary>
        public static bool IsAnyInEar(EarDetectionState state)
        {
            return state == EarDetectionState.LeftInEar ||
                   state == EarDetectionState.RightInEar ||
                   state == EarDetectionState.BothInEar;
        }

        /// <summary>
        /// Checks if both earbuds are in ear.
        /// </summary>
        public static bool AreBothInEar(EarDetectionState state)
        {
            return state == EarDetectionState.BothInEar;
        }

        /// <summary>
        /// Checks if both earbuds are in the case.
        /// </summary>
        public static bool AreBothInCase(EarDetectionState state)
        {
            return state == EarDetectionState.BothInCase;
        }

        /// <summary>
        /// Gets a human-readable description of the ear detection state.
        /// </summary>
        public static string GetDescription(EarDetectionState state)
        {
            return state switch
            {
                EarDetectionState.BothInCase => "Both in case",
                EarDetectionState.LeftInEar => "Left in ear",
                EarDetectionState.RightInEar => "Right in ear",
                EarDetectionState.BothInEar => "Both in ear",
                EarDetectionState.OneOrBothOut => "One or both out",
                _ => "Unknown"
            };
        }
    }
}
